using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Godot;
using TxtConverter.Core.Logic.Unity;
using TxtConverter.Core.Logic.Csharp; // Добавлен namespace
using TxtConverter.Services;

namespace TxtConverter.Core.Logic;

public class Converter
{
    private readonly string _sourceDirPath;
    private readonly List<string> _filesToProcess;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly List<string> _ignoredFolders;
    private readonly bool _genStructure;
    private readonly bool _compactMode;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _genMerged;

    private static readonly Regex BlockCommentRegex = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);

    public Converter(string sourceDirPath, List<string> filesToProcess, HashSet<string> filesSelectedForMerge,
        List<string> ignoredFolders, bool genStructure, bool compactMode,
        CompressionLevel compressionLevel, bool genMerged)
    {
        _sourceDirPath = sourceDirPath;
        _filesToProcess = filesToProcess;
        _filesSelectedForMerge = filesSelectedForMerge;
        _ignoredFolders = ignoredFolders;
        _genStructure = genStructure;
        _compactMode = compactMode;
        _compressionLevel = compressionLevel;
        _genMerged = genMerged;
    }

    public async Task RunConversionAsync(IProgress<double> progress, IProgress<string> status)
    {
        await Task.Run(() =>
        {
            status.Report(Loc("task_preparing"));
            string outputDir = Path.Combine(_sourceDirPath, ProjectConstants.OutputDirName);
            PrepareOutputDirectory(outputDir);

            var processedFilesMap = new Dictionary<string, string>(); // SourcePath -> DestPath

            int total = _filesToProcess.Count;
            int count = 0;

            foreach (var sourceFile in _filesToProcess)
            {
                count++;
                progress.Report((double)count / total);

                string fileName = Path.GetFileName(sourceFile);
                status.Report(string.Format(Loc("task_processing"), fileName));

                string destFileName = fileName.ToLower().EndsWith(".md") ? fileName : fileName + ".txt";
                string destFile = Path.Combine(outputDir, destFileName);

                if (_compressionLevel != CompressionLevel.None && !fileName.ToLower().EndsWith(".md"))
                {
                    try
                    {
                        string content = File.ReadAllText(sourceFile, Encoding.UTF8)
                            .Replace("\r\n", "\n")
                            .Replace('\r', '\n');

                        string compressed;

                        // === UPDATED LOGIC ===
                        if (_compressionLevel == CompressionLevel.Maximum)
                        {
                            if (IsGodotFile(fileName))
                                compressed = GodotCompactConverter.Convert(content, fileName);
                            else if (IsUnityYamlFile(fileName))
                                compressed = UnityCompactConverter.Convert(content);
                            else if (IsCSharpFile(fileName)) // C# Processing
                                compressed = CsCompactConverter.Convert(content);
                            else
                                compressed = ApplyCompression(content, sourceFile);
                        }
                        else
                        {
                            compressed = ApplyCompression(content, sourceFile);
                        }

                        File.WriteAllText(destFile, compressed, Encoding.UTF8);
                    }
                    catch
                    {
                        File.Copy(sourceFile, destFile, true);
                    }
                }
                else
                {
                    File.Copy(sourceFile, destFile, true);
                }

                processedFilesMap[sourceFile] = destFile;
            }

            if (_genStructure)
            {
                status.Report(Loc("task_generating_structure"));
                GenerateDeepStructureReport(outputDir, _sourceDirPath, processedFilesMap);
            }

            if (_genMerged && processedFilesMap.Count > 0)
            {
                status.Report(Loc("task_merging"));
                GenerateMergedFile(outputDir, processedFilesMap);
            }

            status.Report(Loc("task_done"));
            progress.Report(1.0);
        });
    }

    private string ApplyCompression(string content, string filePath)
    {
        if (_compressionLevel == CompressionLevel.Maximum)
        {
            return CompressMax(content, filePath);
        }
        else if (_compressionLevel == CompressionLevel.Smart)
        {
            return Regex.Replace(content, @"\n{3,}", "\n\n").Trim();
        }

        return content;
    }

    private string CompressMax(string content, string filePath)
    {
        content = BlockCommentRegex.Replace(content, "");
        var lines = content.Split('\n');
        var sb = new StringBuilder(content.Length / 2);

        bool isSensitive = IsWhitespaceSensitive(filePath);

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

            if (isSensitive)
                sb.Append(line.TrimEnd()).Append('\n');
            else
                sb.Append(trimmed).Append('\n');
        }

        return sb.ToString().Trim();
    }

    private bool IsWhitespaceSensitive(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return ext == ".gd" || ext == ".py" || ext == ".yaml" || ext == ".yml";
    }

    private bool IsGodotFile(string fileName)
    {
        string lower = fileName.ToLower();
        return lower.EndsWith(".tscn") || lower.EndsWith(".tres");
    }

    private bool IsUnityYamlFile(string fileName)
    {
        string lower = fileName.ToLower();
        return lower.EndsWith(".unity") || lower.EndsWith(".prefab");
    }

    private bool IsCSharpFile(string fileName)
    {
        return fileName.ToLower().EndsWith(".cs");
    }

    private void PrepareOutputDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.GetFiles()) file.Delete();
            foreach (var sub in dir.GetDirectories()) sub.Delete(true);
        }
        else
        {
            Directory.CreateDirectory(path);
        }
    }

    private void GenerateDeepStructureReport(string outputDir, string rootPath, Dictionary<string, string> processedFiles)
    {
        string reportPath = Path.Combine(outputDir, ProjectConstants.ReportStructureFile);
        var sb = new StringBuilder();

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

        if (_compressionLevel == CompressionLevel.Maximum)
        {
            GenerateFlatStructure(rootPath, sb, processedFiles.Keys.ToHashSet());
        }
        else
        {
            if (_compressionLevel != CompressionLevel.None)
            {
                string rootName = new DirectoryInfo(rootPath).Name;
                sb.Append(_compressionLevel == CompressionLevel.Smart ? $"{rootName}/\n" : $"[ROOT] {rootName}\n");
            }

            bool simpleTree = (_compressionLevel == CompressionLevel.Smart);
            WalkDirectoryTree(rootPath, "", sb, processedFiles.Keys.ToHashSet(), simpleTree);
        }

        if (_compressionLevel == CompressionLevel.None) sb.Append("```\n");

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }

    private void GenerateFlatStructure(string currentDir, StringBuilder sb, HashSet<string> processedSet)
    {
        var dirInfo = new DirectoryInfo(currentDir);
        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (!ShouldIncludeInStructure(file.FullName, currentDir)) continue;
            bool isProcessed = processedSet.Contains(file.FullName);

            if (_compactMode && !isProcessed) continue;

            string relPath = Path.GetRelativePath(currentDir, file.FullName).Replace('\\', '/');
            if (isProcessed)
                sb.Append(relPath).Append('\n');
            else
                sb.Append(relPath).Append(" [ignore]\n");
        }
    }

    private void WalkDirectoryTree(string currentDirPath, string prefix, StringBuilder sb, HashSet<string> processedSet, bool simpleTree)
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
                if (processedSet.Contains(child.FullName))
                    nodesToShow.Add(child);
                else if (!_compactMode)
                    filesToCollapse.Add(fi);
            }
        }

        if (!_compactMode && filesToCollapse.Count > 0 && filesToCollapse.Count <= 5)
        {
            nodesToShow.AddRange(filesToCollapse);
            filesToCollapse.Clear();
        }

        nodesToShow.Sort((a, b) =>
        {
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
            PrintTreeNode(node, prefix, isLast, sb, processedSet, simpleTree);
            currentIndex++;
        }

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

    private void PrintTreeNode(FileSystemInfo node, string prefix, bool isLast, StringBuilder sb, HashSet<string> processedSet, bool simpleTree)
    {
        if (simpleTree)
        {
            string currentIndent = prefix + "  ";
            if (node is DirectoryInfo di)
            {
                sb.Append($"{currentIndent}{node.Name}/\n");
                WalkDirectoryTree(di.FullName, currentIndent, sb, processedSet, true);
            }
            else
            {
                sb.Append($"{currentIndent}{node.Name}\n");
            }
        }
        else
        {
            string connector = isLast ? "└── " : "├── ";
            string childPrefix = prefix + (isLast ? "    " : "│   ");

            if (node is DirectoryInfo di)
            {
                sb.Append($"{prefix}{connector}[DIR] {node.Name}\n");
                WalkDirectoryTree(di.FullName, childPrefix, sb, processedSet, false);
            }
            else
            {
                string size = FormatSize(((FileInfo)node).Length);
                string status = GetFileStatus(node.FullName, processedSet);
                sb.Append($"{prefix}{connector}[FILE] {node.Name} ({size}) {status}\n");
            }
        }
    }

    private void GenerateMergedFile(string outputDir, Dictionary<string, string> processedFiles)
    {
        string projectName = Path.GetFileName(_sourceDirPath);
        string outputFileName = "_" + projectName + ProjectConstants.MergedFileSuffix;
        string destPath = Path.Combine(outputDir, outputFileName);

        var sb = new StringBuilder();

        if (_compressionLevel != CompressionLevel.None)
        {
            sb.Append($"# Project: {projectName}\n");
            sb.Append(Loc("report_stub_warning")).Append("\n\n");
        }
        else
        {
            sb.Append(string.Format(Loc("report_merged_header"), projectName)).Append('\n');
            sb.Append(string.Format(Loc("report_generated_date"), DateTime.Now)).Append('\n');
            sb.Append(Loc("report_stub_warning")).Append("\n\n");
        }

        foreach (var entry in processedFiles.OrderBy(e => e.Key))
        {
            string originalPath = entry.Key;
            string processedPath = entry.Value;
            string fileName = Path.GetFileName(originalPath);

            if (_compressionLevel != CompressionLevel.None)
            {
                sb.Append($"\n>>> {fileName}\n");
            }
            else
            {
                sb.Append($"\n--- {string.Format(Loc("report_file_header"), fileName)} ---\n");
            }

            if (_filesSelectedForMerge.Contains(originalPath))
            {
                try
                {
                    string content = File.ReadAllText(processedPath, Encoding.UTF8);
                    sb.Append(content).Append('\n');
                }
                catch (Exception ex)
                {
                    sb.Append($"!!! Error: {ex.Message}\n");
                }
            }
            else
            {
                sb.Append(Loc("report_omitted")).Append("\n\n");
            }
        }

        File.WriteAllText(destPath, sb.ToString(), Encoding.UTF8);
    }

    private bool ShouldIncludeInStructure(string path, string rootOfWalk)
    {
        string name = Path.GetFileName(path);
        if (name == ProjectConstants.OutputDirName) return false;
        if (name.EndsWith(".import") || name.EndsWith(".tmp") || name.EndsWith(".uid")) return false;
        if (name.StartsWith(".") && name != ".gitignore") return false;

        if (Directory.Exists(path))
        {
            if (_ignoredFolders.Contains(name.ToLower())) return false;
        }

        return true;
    }

    private string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        return (bytes / 1024) + " KB";
    }

    private string GetFileStatus(string path, HashSet<string> processedSet)
    {
        if (_filesSelectedForMerge.Contains(path)) return "[ M ]";
        if (processedSet.Contains(path)) return "[ S ]";
        return "[ - ]";
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}