using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Basic compression: removes excessive empty lines and normalizes whitespace.
/// Safe for all text files.
/// </summary>
public class SmartCompressionStrategy : ICompressionStrategy
{
    public virtual string Process(string content, string filePath)
    {
        // Normalize line endings is handled by FileContentProcessor, but we ensure safety here too.
        // Collapse 3+ newlines into 2.
        string processed = Regex.Replace(content, @"\n{3,}", "\n\n");
        return processed.Trim();
    }
}