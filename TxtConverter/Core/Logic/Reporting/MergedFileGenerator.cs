using System.IO;
using System.Linq;
using System.Text;
using System;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

/// <summary>
/// Generates the single merged text file.
/// Safely prepends the prompt-friendly legend, non-All-Caps warning, 
/// horizontal layout structures, and numbered file blocks.
/// </summary>
public class MergedFileGenerator {
    private readonly string _sourceDirPath;
    private readonly string _projectName;
    private readonly Dictionary<string, string> _processedFilesMap;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly CompressionLevel _compressionLevel;
    private readonly string _structureContent;

    public MergedFileGenerator(
        string sourceDirPath,
        Dictionary<string, string> processedFilesMap,
        HashSet<string> filesSelectedForMerge,
        CompressionLevel compressionLevel,
        string structureContent) {
        _sourceDirPath = sourceDirPath;
        _projectName = Path.GetFileName(sourceDirPath);
        _processedFilesMap = processedFilesMap;
        _filesSelectedForMerge = filesSelectedForMerge;
        _compressionLevel = compressionLevel;
        _structureContent = structureContent;
    }

    public void Generate(string outputFilePath) {
        var sb = new StringBuilder();

        // 1. Refined Warning / Additional Information at the very top (Mixed-Case)
        sb.AppendLine(Loc("report_stub_warning"));
        sb.AppendLine();

        // 2. Project Header Details
        if (_compressionLevel != CompressionLevel.None) {
            sb.Append($"# Project: {_projectName}\n\n");
        }
        else {
            sb.Append(string.Format(Loc("report_merged_header"), _projectName)).Append('\n');
            sb.Append(string.Format(Loc("report_generated_date"), DateTime.Now)).Append("\n\n");
        }

        // 3. Horizontal Project Structure Block
        if (!string.IsNullOrWhiteSpace(_structureContent)) {
            sb.Append(_structureContent);
            sb.AppendLine();
            sb.AppendLine("====================================================================");
            sb.AppendLine();
        }

        // 4. File Blocks - Sorted strictly to match exact indexing sequence
        var sortedEntries = _processedFilesMap
            .OrderBy(e => Path.GetRelativePath(_sourceDirPath, e.Key).Replace("\\", "/"), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int index = 0;
        foreach (var entry in sortedEntries) {
            index++;
            string originalPath = entry.Key;
            string processedPath = entry.Value;
            string relPath = Path.GetRelativePath(_sourceDirPath, originalPath).Replace("\\", "/");

            // Format numbered header block
            if (_compressionLevel != CompressionLevel.None) {
                sb.Append($"\n{index}. >>> {relPath}\n");
            }
            else {
                sb.Append($"\n{index}. --- {string.Format(Loc("report_file_header"), relPath)} ---\n");
            }

            // Append code or Stub message
            if (_filesSelectedForMerge.Contains(originalPath)) {
                try {
                    string content = File.ReadAllText(processedPath, Encoding.UTF8);
                    sb.Append(content).Append('\n');
                }
                catch (Exception ex) {
                    sb.Append($"!!! {Loc("report_read_error")}: {ex.Message} !!!\n");
                }
            }
            else {
                // Localized Omitted Stub Warning
                sb.Append(Loc("report_omitted")).Append("\n\n");
            }
        }

        // 5. Save output file
        File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}