using System.IO;
using System.Text;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Processing;

namespace TxtConverter.Core.Logic;

/// <summary>
/// Builds the context string for AI analysis.
/// Now uses FileContentProcessor to ensure consistent processing logic with the main converter.
/// </summary>
public class ContextBuilder
{
    private readonly string _rootPath;
    private readonly List<string> _files;
    private readonly FileContentProcessor _processor;

    public ContextBuilder(string rootPath, List<string> files, CompressionLevel compression)
    {
        _rootPath = rootPath;
        _files = files;
        // Reuse the same processor logic
        _processor = new FileContentProcessor(compression);
    }

    public async Task<string> BuildContextAsync(IProgress<string> statusReporter)
    {
        return await Task.Run(() => {
            var sb = new StringBuilder();
            sb.AppendLine("# Project Context");
            sb.AppendLine($"# Total Files: {_files.Count}");
            sb.AppendLine();

            int counter = 0;
            foreach (var file in _files)
            {
                counter++;
                if (counter % 10 == 0)
                    statusReporter.Report($"Preparing context: {counter}/{_files.Count}");

                string relPath = Path.GetRelativePath(_rootPath, file).Replace("\\", "/");
                sb.AppendLine($">>> {relPath}");

                try
                {
                    // Unified processing call
                    string content = _processor.ReadAndProcess(file);
                    sb.AppendLine(content);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Error reading file: {ex.Message}]");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        });
    }
}