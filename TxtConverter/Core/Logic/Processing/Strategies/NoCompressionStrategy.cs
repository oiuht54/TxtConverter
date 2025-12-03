namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Strategy that performs no modification to the content.
/// Used when Compression Level is set to 'None'.
/// </summary>
public class NoCompressionStrategy : ICompressionStrategy
{
    public string Process(string content, string filePath)
    {
        return content;
    }
}