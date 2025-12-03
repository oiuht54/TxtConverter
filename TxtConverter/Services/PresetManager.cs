using System.IO;

namespace TxtConverter.Services;

public class PresetManager
{
    private static PresetManager? _instance;
    public static PresetManager Instance => _instance ??= new PresetManager();

    private readonly Dictionary<string, string> _presets = new();
    private readonly Dictionary<string, string> _ignoredFolderPresets = new();

    private PresetManager()
    {
        SetupPresets();
    }

    private void SetupPresets()
    {
        _presets.Add("Manual", "");

        // Godot
        _presets.Add("Godot Engine", "gd, tscn, tres, gdshader, godot");
        _presets.Add("Godot Engine (GDExtension / C++)", "gd, tscn, tres, gdshader, godot, gdextension, cpp, h, hpp, c, cc");

        // Unity Engine
        // Оставили unity, prefab, убрали asset
        _presets.Add("Unity Engine", "cs, shader, cginc, json, xml, asmdef, inputactions, unity, prefab");

        // C#
        _presets.Add("C# (.NET / Visual Studio)", "cs, csproj, sln, xaml, config, json, cshtml, razor, sql, xml, props, targets");

        // Java
        _presets.Add("Java (Maven/Gradle)", "java, xml, properties, fxml, gradle, groovy");

        // Web
        _presets.Add("Web (JavaScript / Classic)", "js, mjs, html, css, json");
        _presets.Add("Web (TypeScript / React)", "ts, tsx, jsx, html, css, scss, less, json, vue, svelte");

        // Python
        _presets.Add("Python", "py, requirements.txt, yaml, yml, json");


        // --- Ignored Folders ---

        _ignoredFolderPresets.Add("Manual", "");

        string godotIgnored = ".godot, export_presets, .import";
        _ignoredFolderPresets.Add("Godot Engine", godotIgnored);
        _ignoredFolderPresets.Add("Godot Engine (GDExtension / C++)", godotIgnored + ", .scons_cache, bin, obj, build");

        // UPDATED: Added "TextMesh Pro", "Plugins", "Packages", "Examples"
        _ignoredFolderPresets.Add("Unity Engine", "Library, Temp, obj, bin, ProjectSettings, Logs, UserSettings, .vs, .idea, Builds, Build, Fonts, StreamingAssets, TextMesh Pro, Plugins, Packages, Examples");

        _ignoredFolderPresets.Add("C# (.NET / Visual Studio)", "bin, obj, .vs, packages, TestResults, .git, .idea, .vscode");
        _ignoredFolderPresets.Add("Java (Maven/Gradle)", "target, .idea, build, .settings, bin, out");

        string webIgnored = "node_modules, dist, build, .next, .nuxt, coverage, .git, .vscode, .idea";
        _ignoredFolderPresets.Add("Web (JavaScript / Classic)", webIgnored);
        _ignoredFolderPresets.Add("Web (TypeScript / React)", webIgnored);

        _ignoredFolderPresets.Add("Python", "__pycache__, venv, env, .venv, .git, .idea, .vscode, build, dist, egg-info");
    }

    public IEnumerable<string> GetPresetNames() => _presets.Keys;

    public string GetExtensionsFor(string presetName) =>
        _presets.TryGetValue(presetName, out var val) ? val : "";

    public string GetIgnoredFoldersFor(string presetName) =>
        _ignoredFolderPresets.TryGetValue(presetName, out var val) ? val : "";

    public bool HasPreset(string presetName) => _presets.ContainsKey(presetName);

    public string? AutoDetectPreset(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "project.godot")))
        {
            if (File.Exists(Path.Combine(rootPath, "SConstruct")) ||
                HasFileByPattern(rootPath, "*.gdextension") ||
                HasFileByPattern(rootPath, "*.cpp"))
            {
                return "Godot Engine (GDExtension / C++)";
            }
            return "Godot Engine";
        }

        if (Directory.Exists(Path.Combine(rootPath, "Assets")) && Directory.Exists(Path.Combine(rootPath, "ProjectSettings"))) return "Unity Engine";

        if (HasFileByPattern(rootPath, "*.sln")) return "C# (.NET / Visual Studio)";
        if (HasFileByPattern(rootPath, "*.csproj")) return "C# (.NET / Visual Studio)";

        if (File.Exists(Path.Combine(rootPath, "pom.xml")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle.kts")))
        {
            return "Java (Maven/Gradle)";
        }

        if (File.Exists(Path.Combine(rootPath, "requirements.txt")) ||
            File.Exists(Path.Combine(rootPath, "pyproject.toml")) ||
            Directory.Exists(Path.Combine(rootPath, "venv")) ||
            Directory.Exists(Path.Combine(rootPath, ".venv")))
        {
            return "Python";
        }

        if (File.Exists(Path.Combine(rootPath, "package.json")))
        {
            if (File.Exists(Path.Combine(rootPath, "tsconfig.json")) ||
                File.Exists(Path.Combine(rootPath, "vite.config.ts")))
            {
                return "Web (TypeScript / React)";
            }
            return "Web (JavaScript / Classic)";
        }

        return null;
    }

    private bool HasFileByPattern(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern).Any();
        }
        catch
        {
            return false;
        }
    }
}