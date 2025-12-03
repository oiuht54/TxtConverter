namespace TxtConverter.Core.Logic.Processing;

/// <summary>
/// Defines a unified contract for any file content processing/compression algorithm.
/// </summary>
public interface ICompressionStrategy
{
    /// <summary>
    /// Processes the raw file content and returns the optimized version.
    /// </summary>
    /// <param name="content">Raw string content of the file.</param>
    /// <param name="filePath">Full path to the file (used for context, e.g. class names based on filename).</param>
    /// <returns>Processed content string.</returns>
    string Process(string content, string filePath);
}