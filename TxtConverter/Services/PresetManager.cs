using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace TxtConverter.Services;

public class PresetManager {
    private static PresetManager? _instance;
    public static PresetManager Instance => _instance ??= new PresetManager();
    
    private readonly Dictionary<string, string> _presets = new();
    private readonly Dictionary<string, string> _ignoredFolderPresets = new();
    private readonly HashSet<string> _builtInNames = new();

    private PresetManager() {
        SetupPresets();
    }

    private void SetupPresets() {
        _presets.Clear();
        _ignoredFolderPresets.Clear();
        _builtInNames.Clear();

        _presets.Add("Manual", "");
        
        // Game Engines
        _presets.Add("Godot Engine", "gd, tscn, tres, gdshader, godot");
        _presets.Add("Godot Engine (GDExtension / C++)", "gd, tscn, tres, gdshader, godot, gdextension, cpp, h, hpp, c, cc");
        _presets.Add("Unity Engine", "cs, shader, cginc, json, xml, asmdef, inputactions, unity, prefab, mat, meta");
        
        // General Programming
        _presets.Add("C# (.NET / Visual Studio)", "cs, csproj, sln, xaml, config, json, cshtml, razor, sql, xml, props, targets, vb, fs");
        _presets.Add("Java (Maven/Gradle)", "java, xml, properties, fxml, gradle, groovy");
        _presets.Add("Python", "py, requirements.txt, yaml, yml, json, toml, ini");
        _presets.Add("Go (Golang)", "go, mod, sum, yaml, yml, json, toml");
        
        // Systems & Frameworks
        _presets.Add("Rust / Tauri", "rs, toml, json, js, mjs, ts, jsx, tsx, html, css, scss");
        
        // Web
        _presets.Add("Web (TypeScript / React)", "ts, tsx, jsx, html, css, scss, less, json, vue, svelte");
        _presets.Add("Web (JavaScript / Classic)", "js, mjs, html, css, json");
        
        // Ignored Folders Setup
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
        _ignoredFolderPresets.Add("Go (Golang)", "vendor, bin, pkg, .git, .idea, .vscode, coverage");
        _ignoredFolderPresets.Add("Rust / Tauri", "target, node_modules, dist, build, .git, .vscode, .idea, icons, gen, .github, coverage");

        // Record default built-in presets
        foreach (var key in _presets.Keys) {
            _builtInNames.Add(key);
        }

        // Load custom user-created presets from PreferenceManager
        LoadCustomPresets();
    }

    public void LoadCustomPresets() {
        // Clear old loaded custom entries to avoid duplicates on re-load
        var toRemove = _presets.Keys.Where(k => !_builtInNames.Contains(k)).ToList();
        foreach (var key in toRemove) {
            _presets.Remove(key);
            _ignoredFolderPresets.Remove(key);
        }

        var customList = PreferenceManager.Instance.GetCustomPresets();
        foreach (var item in customList) {
            if (!_presets.ContainsKey(item.Name)) {
                _presets.Add(item.Name, item.Extensions);
                _ignoredFolderPresets.Add(item.Name, item.IgnoredFolders);
            }
        }
    }

    public IEnumerable<string> GetPresetNames() => _presets.Keys;
    public string GetExtensionsFor(string presetName) => _presets.TryGetValue(presetName, out var val) ? val : "";
    public string GetIgnoredFoldersFor(string presetName) => _ignoredFolderPresets.TryGetValue(presetName, out var val) ? val : "";
    public bool HasPreset(string presetName) => _presets.ContainsKey(presetName);
    public bool IsPresetBuiltIn(string presetName) => _builtInNames.Contains(presetName);

    public void AddOrUpdatePreset(string name, string extensions, string ignoredFolders) {
        if (IsPresetBuiltIn(name)) return;

        _presets[name] = extensions;
        _ignoredFolderPresets[name] = ignoredFolders;

        var custom = PreferenceManager.Instance.GetCustomPresets();
        var existing = custom.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            existing.Extensions = extensions;
            existing.IgnoredFolders = ignoredFolders;
        }
        else {
            custom.Add(new PresetModel {
                Name = name,
                Extensions = extensions,
                IgnoredFolders = ignoredFolders
            });
        }
        PreferenceManager.Instance.SetCustomPresets(custom);
    }

    public void DeletePreset(string name) {
        if (IsPresetBuiltIn(name)) return;

        _presets.Remove(name);
        _ignoredFolderPresets.Remove(name);

        var custom = PreferenceManager.Instance.GetCustomPresets();
        var existing = custom.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            custom.Remove(existing);
            PreferenceManager.Instance.SetCustomPresets(custom);
        }
    }

    public string? AutoDetectPreset(string rootPath) {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return null;
        
        if (File.Exists(Path.Combine(rootPath, "project.godot"))) {
            if (File.Exists(Path.Combine(rootPath, "SConstruct")) ||
                HasFileByPattern(rootPath, "*.gdextension") ||
                HasFileByPattern(rootPath, "*.cpp")) {
                return "Godot Engine (GDExtension / C++)";
            }
            return "Godot Engine";
        }
        
        if (Directory.Exists(Path.Combine(rootPath, "Assets")) &&
            Directory.Exists(Path.Combine(rootPath, "ProjectSettings"))) {
            return "Unity Engine";
        }
        
        if (Directory.Exists(Path.Combine(rootPath, "src-tauri")) ||
            File.Exists(Path.Combine(rootPath, "tauri.conf.json"))) {
            return "Rust / Tauri";
        }
        if (File.Exists(Path.Combine(rootPath, "Cargo.toml"))) {
            return "Rust / Tauri";
        }
        
        if (HasFileByPattern(rootPath, "*.sln") ||
            HasFileByPattern(rootPath, "*.csproj") ||
            HasFileByPattern(rootPath, "*.vbproj") ||
            HasFileByPattern(rootPath, "*.fsproj")) {
            return "C# (.NET / Visual Studio)";
        }
        
        if (File.Exists(Path.Combine(rootPath, "pom.xml")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle")) ||
            File.Exists(Path.Combine(rootPath, "build.gradle.kts"))) {
            return "Java (Maven/Gradle)";
        }
        
        if (File.Exists(Path.Combine(rootPath, "requirements.txt")) ||
            File.Exists(Path.Combine(rootPath, "pyproject.toml")) ||
            File.Exists(Path.Combine(rootPath, "setup.py")) ||
            Directory.Exists(Path.Combine(rootPath, "venv")) ||
            Directory.Exists(Path.Combine(rootPath, ".venv"))) {
            return "Python";
        }
        
        if (File.Exists(Path.Combine(rootPath, "go.mod"))) {
            return "Go (Golang)";
        }
        
        if (File.Exists(Path.Combine(rootPath, "package.json"))) {
            if (File.Exists(Path.Combine(rootPath, "tsconfig.json")) ||
                File.Exists(Path.Combine(rootPath, "vite.config.ts")) ||
                File.Exists(Path.Combine(rootPath, "next.config.js"))) {
                return "Web (TypeScript / React)";
            }
            return "Web (JavaScript / Classic)";
        }
        
        if (HasFileByPattern(rootPath, "*.cs")) {
            return "C# (.NET / Visual Studio)";
        }
        if (HasFileByPattern(rootPath, "*.py")) {
            return "Python";
        }
        if (HasFileByPattern(rootPath, "*.go")) {
            return "Go (Golang)";
        }
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