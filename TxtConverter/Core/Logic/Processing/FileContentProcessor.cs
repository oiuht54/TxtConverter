using System.IO;
using System.Text;
using TxtConverter.Core.Enums;

namespace TxtConverter.Core.Logic.Processing;

/// <summary>
/// A unified service to read and process file content.
/// It encapsulates File IO, Line Ending Normalization, and Strategy execution.
/// </summary>
public class FileContentProcessor
{
    private readonly CompressionLevel _compressionLevel;

    public FileContentProcessor(CompressionLevel compressionLevel)
    {
        _compressionLevel = compressionLevel;
    }

    /// <summary>
    /// Reads the file at the given path and applies the configured compression.
    /// </summary>
    /// <param name="sourceFilePath">Absolute path to the source file.</param>
    /// <returns>Processed content string.</returns>
    public string ReadAndProcess(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"File not found: {sourceFilePath}");
        }

        // 1. Read Content
        string content = File.ReadAllText(sourceFilePath, Encoding.UTF8);

        // 2. Normalize Line Endings (Critical for LLM consistency)
        content = content.Replace("\r\n", "\n").Replace('\r', '\n');

        // 3. Get Strategy
        var strategy = CompressionFactory.GetStrategy(_compressionLevel, sourceFilePath);

        // 4. Execute Strategy
        return strategy.Process(content, sourceFilePath);
    }
}