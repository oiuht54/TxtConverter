using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Godot;

namespace TxtConverter.Core.Logic;

public class ContextBuilder
{
    private readonly string _rootPath;
    private readonly List<string> _files;
    private readonly CompressionLevel _compression;

    public ContextBuilder(string rootPath, List<string> files, CompressionLevel compression)
    {
        _rootPath = rootPath;
        _files = files;
        _compression = compression;
    }

    public async Task<string> BuildContextAsync(IProgress<string> statusReporter)
    {
        return await Task.Run(() =>
        {
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
                    string content = File.ReadAllText(file, Encoding.UTF8)
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n');

                    if (_compression != CompressionLevel.None)
                    {
                        content = ApplyCompression(content, file);
                    }

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

    private string ApplyCompression(string content, string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();

        // Godot Special Optimization
        if (_compression == CompressionLevel.Maximum && (ext == ".tscn" || ext == ".tres"))
        {
            try
            {
                return GodotCompactConverter.Convert(content, Path.GetFileName(filePath));
            }
            catch
            {
                return content; // Fallback
            }
        }

        // General Smart Compression
        if (_compression == CompressionLevel.Smart || _compression == CompressionLevel.Maximum)
        {
            // Remove excessive newlines
            content = Regex.Replace(content, @"\n{3,}", "\n\n");

            // Remove comments for Maximum compression (simple regex, can be improved)
            if (_compression == CompressionLevel.Maximum && ext != ".md" && ext != ".txt")
            {
                // Block comments
                content = Regex.Replace(content, @"/\*[\s\S]*?\*/", "");
                // Line comments (basic C-style and Python/Godot style)
                // Note: This is aggressive and might strip URLs, but valid for Maximum
                var lines = content.Split('\n');
                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;
                    sb.AppendLine(line);
                }
                return sb.ToString().Trim();
            }

            return content.Trim();
        }

        return content;
    }
}