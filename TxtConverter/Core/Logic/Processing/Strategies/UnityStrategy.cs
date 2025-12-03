using TxtConverter.Core.Logic.Unity;

namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Maximum compression for Unity Engine YAML files (.unity, .prefab).
/// Delegates to the specialized UnityCompactConverter.
/// </summary>
public class UnityStrategy : ICompressionStrategy
{
    public string Process(string content, string filePath)
    {
        try
        {
            return UnityCompactConverter.Convert(content);
        }
        catch
        {
            return content;
        }
    }
}