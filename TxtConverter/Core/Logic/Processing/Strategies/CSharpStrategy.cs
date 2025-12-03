using TxtConverter.Core.Logic.Csharp;

namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Maximum compression for C# source code.
/// Removes comments and compacts braces.
/// </summary>
public class CSharpStrategy : ICompressionStrategy
{
    public string Process(string content, string filePath)
    {
        try
        {
            return CsCompactConverter.Convert(content);
        }
        catch
        {
            return content;
        }
    }
}