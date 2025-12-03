using System.IO;
using System.Text;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

/// <summary>
/// Responsible for generating the _FileStructure.md file.
/// Supports both Tree view and Flat view.
/// </summary>
public class StructureReportGenerator
{
    private readonly string _rootPath;
    private readonly HashSet<string> _processedFiles; // Files that were actually converted
    private readonly HashSet<string> _filesSelectedForMerge; // Files selected by user for full content
    private readonly List<string> _ignoredFolders;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _compactMode;

    public StructureReportGenerator(
        string rootPath,
        HashSet<string> processedFiles,
        HashSet<string> filesSelectedForMerge,
        List<string> ignoredFolders,
        CompressionLevel compressionLevel,
        bool compactMode)
    {

        _rootPath = rootPath;
        _processedFiles = processedFiles;
        _filesSelectedForMerge = filesSelectedForMerge;
        _ignoredFolders = ignoredFolders;
        _compressionLevel = compressionLevel;
        _compactMode = compactMode;
    }

    public void Generate(string outputDir)
    {
        string reportPath = Path.Combine(outputDir, ProjectConstants.ReportStructureFile);
        var sb = new StringBuilder();

        // 1. Header
        sb.Append(Loc("report_structure_header")).Append('\n');
        sb.Append(string.Format(Loc("report_generated_date"), DateTime.Now)).Append("\n\n");

        if (_compressionLevel == CompressionLevel.None)
        {
            sb.Append("### Legend / Легенда:\n");
            sb.Append("- `[ M ]` Merged: Full content included.\n");
            sb.Append("- `[ S ]` Stub: File included as a stub.\n\n");
            sb.Append("```text\n");
        }
        else
        {
            sb.Append(_compressionLevel == CompressionLevel.Maximum ? "(Flat Structure Mode)\n" : "(Compact Tree Mode)\n");
        }

        // 2. Body Generation
        if (_compressionLevel == CompressionLevel.Maximum)
        {
            GenerateFlatStructure(sb);
        }
        else
        {
            if (_compressionLevel != CompressionLevel.None)
            {
                string rootName = new DirectoryInfo(_rootPath).Name;
                sb.Append(_compressionLevel == CompressionLevel.Smart ? $"{rootName}/\n" : $"[ROOT] {rootName}\n");
            }

            bool simpleTree = (_compressionLevel == CompressionLevel.Smart);
            // We use WalkDirectoryTree to traverse the actual file system but filter based on processed files
            WalkDirectoryTree(_rootPath, "", sb, simpleTree);
        }

        if (_compressionLevel == CompressionLevel.None) sb.Append("```\n");

        // 3. Write
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }

    private void GenerateFlatStructure(StringBuilder sb)
    {
        // In Flat Mode, we mostly show what was processed
        // But we might iterate real files to show ignored ones if compact mode is off
        var dirInfo = new DirectoryInfo(_rootPath);

        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (!ShouldIncludeInStructure(file.FullName, _rootPath)) continue;

            bool isProcessed = _processedFiles.Contains(file.FullName);

            // If CompactMode is ON, we only show files that are actually in the output
            if (_compactMode && !isProcessed) continue;

            string relPath = Path.GetRelativePath(_rootPath, file.FullName).Replace('\\', '/');

            if (isProcessed)
                sb.Append(relPath).Append('\n');
            else
                sb.Append(relPath).Append(" [ignore]\n");
        }
    }

    private void WalkDirectoryTree(string currentDirPath, string prefix, StringBuilder sb, bool simpleTree)
    {
        var dirInfo = new DirectoryInfo(currentDirPath);
        FileSystemInfo[] children;
        try
        {
            children = dirInfo.GetFileSystemInfos();
        }
        catch { return; }

        var nodesToShow = new List<FileSystemInfo>();
        var filesToCollapse = new List<FileInfo>();

        foreach (var child in children)
        {
            if (!ShouldIncludeInStructure(child.FullName, currentDirPath)) continue;

            if (child is DirectoryInfo)
            {
                nodesToShow.Add(child);
            }
            else if (child is FileInfo fi)
            {
                // Determine if we show this file explicitly
                if (_processedFiles.Contains(child.FullName))
                {
                    nodesToShow.Add(child);
                }
                else if (!_compactMode)
                {
                    filesToCollapse.Add(fi);
                }
            }
        }

        // Collapse Logic: If we have many ignored files, group them
        if (!_compactMode && filesToCollapse.Count > 0 && filesToCollapse.Count <= 5)
        {
            nodesToShow.AddRange(filesToCollapse);
            filesToCollapse.Clear();
        }

        // Sort: Directories first, then files
        nodesToShow.Sort((a, b) => {
            bool da = a is DirectoryInfo;
            bool db = b is DirectoryInfo;
            if (da && !db) return -1;
            if (!da && db) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        int totalItems = nodesToShow.Count + (filesToCollapse.Count > 0 ? 1 : 0);
        int currentIndex = 0;

        foreach (var node in nodesToShow)
        {
            bool isLast = (currentIndex == totalItems - 1);
            PrintTreeNode(node, prefix, isLast, sb, simpleTree);
            currentIndex++;
        }

        // Print collapsed summary if any
        if (filesToCollapse.Count > 0)
        {
            var extStats = filesToCollapse
                .GroupBy(f => f.Extension)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key}({g.Count()})");

            string statsStr = string.Join(", ", extStats);

            if (simpleTree)
            {
                sb.Append($"{prefix}  ... ({filesToCollapse.Count}: {statsStr})\n");
            }
            else
            {
                sb.Append($"{prefix}└── [ ... {filesToCollapse.Count} ignored: {statsStr} ... ]\n");
            }
        }
    }

    private void PrintTreeNode(FileSystemInfo node, string prefix, bool isLast, StringBuilder sb, bool simpleTree)
    {
        if (simpleTree)
        {
            // Smart/Compact Tree Style
            string currentIndent = prefix + "  ";
            if (node is DirectoryInfo di)
            {
                sb.Append($"{currentIndent}{node.Name}/\n");
                WalkDirectoryTree(di.FullName, currentIndent, sb, true);
            }
            else
            {
                sb.Append($"{currentIndent}{node.Name}\n");
            }
        }
        else
        {
            // Detailed Tree Style (Original)
            string connector = isLast ? "└── " : "├── ";
            string childPrefix = prefix + (isLast ? "    " : "│   ");

            if (node is DirectoryInfo di)
            {
                sb.Append($"{prefix}{connector}[DIR] {node.Name}\n");
                WalkDirectoryTree(di.FullName, childPrefix, sb, false);
            }
            else
            {
                string size = FormatSize(((FileInfo)node).Length);
                string status = GetFileStatus(node.FullName);
                sb.Append($"{prefix}{connector}[FILE] {node.Name} ({size}) {status}\n");
            }
        }
    }

    private bool ShouldIncludeInStructure(string path, string rootOfWalk)
    {
        string name = Path.GetFileName(path);
        // Standard filters
        if (name == ProjectConstants.OutputDirName) return false;
        if (name.EndsWith(".import") || name.EndsWith(".tmp") || name.EndsWith(".uid")) return false;
        if (name.StartsWith(".") && name != ".gitignore") return false;

        if (Directory.Exists(path))
        {
            if (_ignoredFolders.Contains(name.ToLower())) return false;
        }
        return true;
    }

    private string GetFileStatus(string path)
    {
        if (_filesSelectedForMerge.Contains(path)) return "[ M ]"; // Merged
        if (_processedFiles.Contains(path)) return "[ S ]"; // Stub
        return "[ - ]";
    }

    private string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        return (bytes / 1024) + " KB";
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}