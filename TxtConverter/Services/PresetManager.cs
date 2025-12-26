using System.IO;

namespace TxtConverter.Services;

public class PresetManager {
    private static PresetManager? _instance;
    public static PresetManager Instance => _instance ??= new PresetManager();

    private readonly Dictionary<string, string> _presets = new();
    private readonly Dictionary<string, string> _ignoredFolderPresets = new();

    private PresetManager() {
        SetupPresets();
    }

    private void SetupPresets() {
        _presets.Add("Manual", "");

        // Game Engines
        _presets.Add("Godot Engine", "gd, tscn, tres, gdshader, godot");
        _presets.Add("Godot Engine (GDExtension / C++)", "gd, tscn, tres, gdshader, godot, gdextension, cpp, h, hpp, c, cc");
        _presets.Add("Unity Engine", "cs, shader, cginc, json, xml, asmdef, inputactions, unity, prefab, mat, meta");

        // General Programming
        _presets.Add("C# (.NET / Visual Studio)", "cs, csproj, sln, xaml, config, json, cshtml, razor, sql, xml, props, targets, vb, fs");
        _presets.Add("Java (Maven/Gradle)", "java, xml, properties, fxml, gradle, groovy");
        _presets.Add("Python", "py, requirements.txt, yaml, yml, json, toml, ini");
        
        // Systems & Frameworks
        // FIX: Removed 'lock' to avoid Cargo.lock/package-lock.json garbage.
        // Kept 'json' for config, but user should be aware schemas might slip in if not in ignored folders.
        _presets.Add("Rust / Tauri", "rs, toml, json, js, mjs, ts, jsx, tsx, html, css, scss");

        // Web
        _presets.Add("Web (TypeScript / React)", "ts, tsx, jsx, html, css, scss, less, json, vue, svelte");
        _presets.Add("Web (JavaScript / Classic)", "js, mjs, html, css, json");

        // Ignored Folders
        _ignoredFolderPresets.Add("Manual", "");

        string godotIgnored = ".godot, export_presets, .import";
        _ignoredFolderPresets.Add("Godot Engine", godotIgnored);
        _ignoredFolderPresets.Add("Godot Engine (GDExtension / C++)", godotIgnored + ", .scons_cache, bin, obj, build, out");
        _ignoredFolderPresets.Add("Unity Engine", "Library, Temp, obj, bin, ProjectSettings, Logs, UserSettings, .vs, .idea, Builds, Build, Fonts, StreamingAssets, TextMesh Pro, Plugins, Packages, Examples");
        
        _ignoredFolderPresets.Add("C# (.NET / Visual Studio)", "bin, obj, .vs, packages, TestResults, .git, .idea, .vscode, artifacts");
        _ignoredFolderPresets.Add("Java (Maven/Gradle)", "target, .idea, build, .settings, bin, out, .gradle");
        
        string webIgnored = "node_modules, dist, build, .next, .nuxt, coverage, .git, .vscode, .idea";
        _ignoredFolderPresets.Add("Web (JavaScript / Classic)", webIgnored);
        _ignoredFolderPresets.Add("Web (TypeScript / React)", webIgnored);
        
        _ignoredFolderPresets.Add("Python", "__pycache__, venv, env, .venv, .git, .idea, .vscode, build, dist, egg-info");
        
        // Rust / Tauri specific ignores
        // Added: icons (binary assets), gen (generated code), .github (CI/CD noise), coverage
        _ignoredFolderPresets.Add("Rust / Tauri", "target, node_modules, dist, build, .git, .vscode, .idea, icons, gen, .github, coverage");
    }

    public IEnumerable<string> GetPresetNames() => _presets.Keys;

    public string GetExtensionsFor(string presetName) =>
        _presets.TryGetValue(presetName, out var val) ? val : "";

    public string GetIgnoredFoldersFor(string presetName) =>
        _ignoredFolderPresets.TryGetValue(presetName, out var val) ? val : "";

    public bool HasPreset(string presetName) => _presets.ContainsKey(presetName);

    /// <summary>
    /// Analyzes the folder structure to determine the most likely project type.
    /// </summary>
    public string? AutoDetectPreset(string rootPath) {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return null;

        // 1. Godot Engine Check
        if (File.Exists(Path.Combine(rootPath, "project.godot"))) {
            if (File.Exists(Path.Combine(rootPath, "SConstruct")) ||
                HasFileByPattern(rootPath, "*.gdextension") ||
                HasFileByPattern(rootPath, "*.cpp")) {
                return "Godot Engine (GDExtension / C++)";
            }
            return "Godot Engine";
        }

        // 2. Unity Engine Check
        if (Directory.Exists(Path.Combine(rootPath, "Assets")) &&
            Directory.Exists(Path.Combine(rootPath, "ProjectSettings"))) {
            return "Unity Engine";
        }
        
        // 3. Rust / Tauri Check
        // Priority check for Tauri configuration or Rust Cargo manifest
        if (Directory.Exists(Path.Combine(rootPath, "src-tauri")) || 
            File.Exists(Path.Combine(rootPath, "tauri.conf.json"))) {
            return "Rust / Tauri";
        }
        if (File.Exists(Path.Combine(rootPath, "Cargo.toml"))) {
            return "Rust / Tauri";
        }

        // 4. C# / .NET Check
        if (HasFileByPattern(rootPath, "*.sln") ||
            HasFileByPattern(rootPath, "*.csproj") ||
            HasFileByPattern(rootPath, "*.vbproj") ||
            HasFileByPattern(rootPath, "*.fsproj")) {
            return "C# (.NET / Visual Studio)";
        }

        // 5. Java Check
        if (File.Exists(Path.Combine(rootPath, "pom.xml")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle.kts"))) {
            return "Java (Maven/Gradle)";
        }

        // 6. Python Check
        if (File.Exists(Path.Combine(rootPath, "requirements.txt")) ||
            File.Exists(Path.Combine(rootPath, "pyproject.toml")) ||
            File.Exists(Path.Combine(rootPath, "setup.py")) ||
            Directory.Exists(Path.Combine(rootPath, "venv")) ||
            Directory.Exists(Path.Combine(rootPath, ".venv"))) {
            return "Python";
        }

        // 7. Web Ecosystem Check
        if (File.Exists(Path.Combine(rootPath, "package.json"))) {
            if (File.Exists(Path.Combine(rootPath, "tsconfig.json")) ||
                File.Exists(Path.Combine(rootPath, "vite.config.ts")) ||
                File.Exists(Path.Combine(rootPath, "next.config.js"))) {
                return "Web (TypeScript / React)";
            }
            return "Web (JavaScript / Classic)";
        }

        // --- Fallbacks ---
        
        if (HasFileByPattern(rootPath, "*.cs")) {
            return "C# (.NET / Visual Studio)";
        }

        if (HasFileByPattern(rootPath, "*.py")) {
            return "Python";
        }
        
        // Fallback for pure Rust projects without Cargo.toml (rare, but possible scripts)
        if (HasFileByPattern(rootPath, "*.rs")) {
            return "Rust / Tauri";
        }

        return null;
    }

    private bool HasFileByPattern(string path, string pattern) {
        try {
            return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch {
            return false;
        }
    }
}