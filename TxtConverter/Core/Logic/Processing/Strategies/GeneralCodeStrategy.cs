using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Processing.Strategies;

public class GeneralCodeStrategy : SmartCompressionStrategy {
    // Removed BlockCommentRegex as we now preserve comments

    public override string Process(string content, string filePath) {
        string ext = System.IO.Path.GetExtension(filePath).ToLower();

        // For Markdown and Text files, use standard smart processing
        if (ext == ".md" || ext == ".txt") {
            return base.Process(content, filePath);
        }

        // Logic Update: We no longer strip block comments here.
        // content = BlockCommentRegex.Replace(content, ""); 

        var lines = content.Split('\n');
        var sb = new StringBuilder(content.Length);
        
        bool isSensitive = IsWhitespaceSensitive(ext);

        foreach (var line in lines) {
            string trimmed = line.Trim();
            
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Logic Update: We no longer skip lines starting with comments.
            // if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

            if (isSensitive) {
                // For Python, GDScript, YAML: Keep indentation (TrimEnd only)
                sb.Append(line.TrimEnd()).Append('\n');
            }
            else {
                // For C++, Java, JS, etc.: We still flatten indentation to save tokens (tabs/spaces),
                // but now we preserve the comments.
                sb.Append(trimmed).Append('\n');
            }
        }

        return sb.ToString().Trim();
    }

    private bool IsWhitespaceSensitive(string ext) {
        return ext == ".gd" || ext == ".py" || ext == ".yaml" || ext == ".yml";
    }
}