using System.IO;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Processing.Strategies;

namespace TxtConverter.Core.Logic.Processing;

/// <summary>
/// Factory pattern responsible for selecting the correct ICompressionStrategy
/// based on the global CompressionLevel and the specific file type.
/// </summary>
public static class CompressionFactory
{

    public static ICompressionStrategy GetStrategy(CompressionLevel level, string filePath)
    {
        if (level == CompressionLevel.None)
        {
            return new NoCompressionStrategy();
        }

        if (level == CompressionLevel.Smart)
        {
            return new SmartCompressionStrategy();
        }

        if (level == CompressionLevel.Maximum)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            // 1. Godot Specific
            if (ext == ".tscn" || ext == ".tres")
            {
                return new GodotStrategy();
            }

            // 2. Unity Specific
            if (ext == ".unity" || ext == ".prefab")
            {
                return new UnityStrategy();
            }

            // 3. C# Specific
            if (ext == ".cs")
            {
                return new CSharpStrategy();
            }

            // 4. Fallback for other code files (JS, Java, Python, etc.)
            // GeneralCodeStrategy cleans comments and whitespace aggressively.
            return new GeneralCodeStrategy();
        }

        // Default fallback
        return new SmartCompressionStrategy();
    }
}