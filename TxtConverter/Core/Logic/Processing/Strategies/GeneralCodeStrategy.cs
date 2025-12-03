using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Aggressive compression for general code (JS, TS, Java, etc.) when in Maximum mode.
/// Removes block comments and attempts to tighten the layout.
/// </summary>
public class GeneralCodeStrategy : SmartCompressionStrategy
{
    private static readonly Regex BlockCommentRegex = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);

    public override string Process(string content, string filePath)
    {
        string ext = System.IO.Path.GetExtension(filePath).ToLower();

        // Do not strip comments from Markdown or plain text files, as text IS the content.
        if (ext == ".md" || ext == ".txt")
        {
            return base.Process(content, filePath);
        }

        // 1. Remove Block Comments /* ... */
        content = BlockCommentRegex.Replace(content, "");

        // 2. Process lines (remove line comments // or #)
        var lines = content.Split('\n');
        var sb = new StringBuilder(content.Length);

        // Check if we should preserve indentation (Whitespace sensitive languages)
        bool isSensitive = IsWhitespaceSensitive(ext);

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed)) continue;

            // Skip single line comments
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

            if (isSensitive)
            {
                // For Python/GDScript/YAML, we must keep indentation
                sb.Append(line.TrimEnd()).Append('\n');
            }
            else
            {
                // For C-like languages, we can trim strictly
                sb.Append(trimmed).Append('\n');
            }
        }

        return sb.ToString().Trim();
    }

    private bool IsWhitespaceSensitive(string ext)
    {
        return ext == ".gd" || ext == ".py" || ext == ".yaml" || ext == ".yml";
    }
}