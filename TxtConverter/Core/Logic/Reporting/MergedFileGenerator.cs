using System.IO;
using System.Text;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

/// <summary>
/// Responsible for creating the big merged text file ("_Full_Source_code.txt").
/// Combines processed files and stubs.
/// </summary>
public class MergedFileGenerator
{
    private readonly string _projectName;
    private readonly Dictionary<string, string> _processedFilesMap; // SourcePath -> DestPath (where processed file lives)
    private readonly HashSet<string> _filesSelectedForMerge; // Files that should be fully included
    private readonly CompressionLevel _compressionLevel;

    public MergedFileGenerator(
        string projectName,
        Dictionary<string, string> processedFilesMap,
        HashSet<string> filesSelectedForMerge,
        CompressionLevel compressionLevel)
    {

        _projectName = projectName;
        _processedFilesMap = processedFilesMap;
        _filesSelectedForMerge = filesSelectedForMerge;
        _compressionLevel = compressionLevel;
    }

    public void Generate(string outputFilePath)
    {
        var sb = new StringBuilder();

        // 1. Header
        if (_compressionLevel != CompressionLevel.None)
        {
            sb.Append($"# Project: {_projectName}\n");
            sb.Append(Loc("report_stub_warning")).Append("\n\n");
        }
        else
        {
            sb.Append(string.Format(Loc("report_merged_header"), _projectName)).Append('\n');
            sb.Append(string.Format(Loc("report_generated_date"), DateTime.Now)).Append('\n');
            sb.Append(Loc("report_stub_warning")).Append("\n\n");
        }

        // 2. Body
        foreach (var entry in _processedFilesMap.OrderBy(e => e.Key))
        {
            string originalPath = entry.Key;
            string processedPath = entry.Value;
            string fileName = Path.GetFileName(originalPath);

            // File Header
            if (_compressionLevel != CompressionLevel.None)
            {
                sb.Append($"\n>>> {fileName}\n");
            }
            else
            {
                sb.Append($"\n--- {string.Format(Loc("report_file_header"), fileName)} ---\n");
            }

            // File Content (Full or Stub)
            if (_filesSelectedForMerge.Contains(originalPath))
            {
                try
                {
                    // Read the file we already processed and saved in the output dir
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
                // Stub marker
                sb.Append(Loc("report_omitted")).Append("\n\n");
            }
        }

        // 3. Write
        File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}