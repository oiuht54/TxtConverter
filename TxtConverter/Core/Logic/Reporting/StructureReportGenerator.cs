using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

public class StructureReportGenerator {
    private readonly string _rootPath;
    private readonly HashSet<string> _processedFiles; // Файлы, которые были фактически преобразованы
    private readonly HashSet<string> _filesSelectedForMerge; // Файлы, выбранные пользователем для полного слияния
    private readonly HashSet<string> _ignoredFolderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredRelativePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _compactMode;

    public StructureReportGenerator(
        string rootPath,
        HashSet<string> processedFiles,
        HashSet<string> filesSelectedForMerge,
        List<string> ignoredFolders,
        CompressionLevel compressionLevel,
        bool compactMode) {
        _rootPath = rootPath;
        _processedFiles = processedFiles;
        _filesSelectedForMerge = filesSelectedForMerge;
        _compressionLevel = compressionLevel;
        _compactMode = compactMode;

        // Инициализируем те же правила фильтрации папок, что и в сканере, для консистентности отчета структуры
        foreach (var folder in ignoredFolders) {
            string trimmed = folder.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            string normalized = trimmed.Replace('\\', '/');
            if (normalized.Contains('/')) {
                _ignoredRelativePaths.Add(normalized.TrimStart('/').ToLower());
            } else {
                _ignoredFolderNames.Add(normalized.ToLower());
            }
        }
    }

    /// <summary>
    /// Генерирует отчет о структуре и сохраняет его в файл.
    /// </summary>
    public string Generate(string outputDir) {
        string reportPath = Path.Combine(outputDir, ProjectConstants.ReportStructureFile);
        var sb = new StringBuilder();
        sb.Append(Loc("report_structure_header")).Append('\n');
        sb.Append(string.Format(Loc("report_generated_date"), DateTime.Now)).Append("\n\n");

        if (_compressionLevel == CompressionLevel.None) {
            sb.Append("### Legend / Легенда:\n");
            sb.Append("- `[ M ]` Merged: Full content included.\n");
            sb.Append("- `[ S ]` Omitted: Content excluded (Context Saver).\n\n");
            sb.Append("```text\n");
        }
        else {
            sb.Append(_compressionLevel == CompressionLevel.Maximum ? "(Flat Structure Mode)\n" : "(Compact Tree Mode)\n");
        }

        if (_compressionLevel == CompressionLevel.Maximum) {
            GenerateFlatStructure(sb);
        }
        else {
            if (_compressionLevel != CompressionLevel.None) {
                string rootName = new DirectoryInfo(_rootPath).Name;
                sb.Append(_compressionLevel == CompressionLevel.Smart ? $"{rootName}/\n" : $"[ROOT] {rootName}\n");
            }
            bool simpleTree = (_compressionLevel == CompressionLevel.Smart);
            WalkDirectoryTree(_rootPath, "", sb, simpleTree);
        }

        if (_compressionLevel == CompressionLevel.None) sb.Append("```\n");
        string finalContent = sb.ToString();
        File.WriteAllText(reportPath, finalContent, Encoding.UTF8);
        return finalContent;
    }

    private void GenerateFlatStructure(StringBuilder sb) {
        var dirInfo = new DirectoryInfo(_rootPath);
        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories)) {
            if (!ShouldIncludeInStructure(file.FullName, _rootPath)) continue;
            bool isProcessed = _processedFiles.Contains(file.FullName);
            if (_compactMode && !isProcessed) continue;
            string relPath = Path.GetRelativePath(_rootPath, file.FullName).Replace('\\', '/');
            if (isProcessed)
                sb.Append(relPath).Append('\n');
            else
                sb.Append(relPath).Append(" [ignore]\n");
        }
    }

    private void WalkDirectoryTree(string currentDirPath, string prefix, StringBuilder sb, bool simpleTree) {
        var dirInfo = new DirectoryInfo(currentDirPath);
        FileSystemInfo[] children;
        try {
            children = dirInfo.GetFileSystemInfos();
        }
        catch { return; }

        var nodesToShow = new List<FileSystemInfo>();
        var filesToCollapse = new List<FileInfo>();

        foreach (var child in children) {
            if (!ShouldIncludeInStructure(child.FullName, currentDirPath)) continue;
            if (child is DirectoryInfo) {
                nodesToShow.Add(child);
            }
            else if (child is FileInfo fi) {
                if (_processedFiles.Contains(child.FullName)) {
                    nodesToShow.Add(child);
                }
                else if (!_compactMode) {
                    filesToCollapse.Add(fi);
                }
            }
        }

        if (!_compactMode && filesToCollapse.Count > 0 && filesToCollapse.Count <= 5) {
            nodesToShow.AddRange(filesToCollapse);
            filesToCollapse.Clear();
        }

        nodesToShow.Sort((a, b) => {
            bool da = a is DirectoryInfo;
            bool db = b is DirectoryInfo;
            if (da && !db) return -1;
            if (!da && db) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        int totalItems = nodesToShow.Count + (filesToCollapse.Count > 0 ? 1 : 0);
        int currentIndex = 0;

        foreach (var node in nodesToShow) {
            bool isLast = (currentIndex == totalItems - 1);
            PrintTreeNode(node, prefix, isLast, sb, simpleTree);
            currentIndex++;
        }

        if (filesToCollapse.Count > 0) {
            var extStats = filesToCollapse
                .GroupBy(f => f.Extension)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key}({g.Count()})");
            string statsStr = string.Join(", ", extStats);
            if (simpleTree) {
                sb.Append($"{prefix} ... ({filesToCollapse.Count}: {statsStr})\n");
            }
            else {
                sb.Append($"{prefix}└── [ ... {filesToCollapse.Count} ignored: {statsStr} ... ]\n");
            }
        }
    }

    private void PrintTreeNode(FileSystemInfo node, string prefix, bool isLast, StringBuilder sb, bool simpleTree) {
        if (simpleTree) {
            string currentIndent = prefix + " ";
            if (node is DirectoryInfo di) {
                sb.Append($"{currentIndent}{node.Name}/\n");
                WalkDirectoryTree(di.FullName, currentIndent, sb, true);
            }
            else {
                sb.Append($"{currentIndent}{node.Name}\n");
            }
        }
        else {
            string connector = isLast ? "└── " : "├── ";
            string childPrefix = prefix + (isLast ? " " : "│ ");
            if (node is DirectoryInfo di) {
                sb.Append($"{prefix}{connector}[DIR] {node.Name}\n");
                WalkDirectoryTree(di.FullName, childPrefix, sb, false);
            }
            else {
                string size = FormatSize(((FileInfo)node).Length);
                string status = GetFileStatus(node.FullName);
                sb.Append($"{prefix}{connector}[FILE] {node.Name} ({size}) {status}\n");
            }
        }
    }

    private bool ShouldIncludeInStructure(string path, string rootOfWalk) {
        string name = Path.GetFileName(path);
        if (name == ProjectConstants.OutputDirName) return false;
        if (name.EndsWith(".import") || name.EndsWith(".tmp") || name.EndsWith(".uid")) return false;
        if (name.StartsWith(".") && name != ".gitignore") return false;

        if (Directory.Exists(path)) {
            string dirName = name.ToLower();
            if (_ignoredFolderNames.Contains(dirName)) return false;

            try {
                string relPath = Path.GetRelativePath(_rootPath, path).Replace('\\', '/').ToLower().TrimStart('/');
                if (_ignoredRelativePaths.Contains(relPath)) return false;

                foreach (var ignoredRel in _ignoredRelativePaths) {
                    string prefix = ignoredRel.EndsWith("/") ? ignoredRel : ignoredRel + "/";
                    if (relPath.StartsWith(prefix)) return false;
                }
            }
            catch {
                return false;
            }
        }
        return true;
    }

    private string GetFileStatus(string path) {
        if (_filesSelectedForMerge.Contains(path)) return "[ M ]"; // Merged
        if (_processedFiles.Contains(path)) return "[ S ]"; // Stub / Omitted
        return "[ - ]";
    }

    private string FormatSize(long bytes) {
        if (bytes < 1024) return bytes + " B";
        return (bytes / 1024) + " KB";
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}