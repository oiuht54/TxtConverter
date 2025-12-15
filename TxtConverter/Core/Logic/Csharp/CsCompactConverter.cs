using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Csharp;

public class CsCompactConverter {
    // Removed BlockCommentRegex as we now preserve comments

    public static string Convert(string content) {
        // Logic Update: Block comments are preserved.
        // content = BlockCommentRegex.Replace(content, "");

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(content.Length);

        string? previousLine = null;

        foreach (var rawLine in lines) {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Logic Update: Line comments are preserved.
            // if (line.StartsWith("//")) continue;

            if (line == "{") {
                // Safety Check: We can only merge the brace up if the previous line
                // DOES NOT contain a comment. Otherwise, the brace becomes commented out.
                if (previousLine != null && !ContainsComment(previousLine)) {
                    sb.Append(" {");
                    previousLine += " {";
                }
                else {
                    // Must start a new line
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("{");
                    previousLine = "{";
                }
            }
            else {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
                previousLine = line;
            }
        }

        return sb.ToString();
    }

    private static bool ContainsComment(string line) {
        return line.Contains("//");
    }
}