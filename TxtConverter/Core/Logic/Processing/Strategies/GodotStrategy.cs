using TxtConverter.Core.Logic.Godot;

namespace TxtConverter.Core.Logic.Processing.Strategies;

/// <summary>
/// Maximum compression for Godot Engine files (.tscn, .tres).
/// Delegates to the specialized GodotCompactConverter.
/// </summary>
public class GodotStrategy : ICompressionStrategy
{
    public string Process(string content, string filePath)
    {
        try
        {
            // The static converter handles the heavy lifting of parsing the scene tree.
            return GodotCompactConverter.Convert(content, System.IO.Path.GetFileName(filePath));
        }
        catch
        {
            // Fallback to original content if parsing fails to avoid data loss.
            return content;
        }
    }
}