using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TxtConverter.Core;

namespace TxtConverter.Services;

public class PresetManager {
    private static PresetManager? _instance;
    public static PresetManager Instance => _instance ??= new PresetManager();

    private readonly Dictionary<string, string> _presets = new();
    private readonly Dictionary<string, string> _ignoredFolderPresets = new();
    private readonly Dictionary<string, string> _exclusionsPresets = new();
    private readonly HashSet<string> _builtInNames = new();

    private static readonly HashSet<string> DetectionIgnoredDirs = new(StringComparer.OrdinalIgnoreCase) {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "target", "build", "dist",
        "out", "library", "temp", "logs", ".godot", ".import", ".idea", ".vs", ".vscode",
        "venv", ".venv", "__pycache__", ProjectConstants.OutputDirName
    };

    private PresetManager() {
        SetupPresets();
    }

    private void SetupPresets() {
        _presets.Clear();
        _ignoredFolderPresets.Clear();
        _exclusionsPresets.Clear();
        _builtInNames.Clear();

        _presets.Add("Manual", "");
        _ignoredFolderPresets.Add("Manual", "");
        _exclusionsPresets.Add("Manual", "");

        // Game Engines
        _presets.Add("Godot Engine", "gd, tscn, tres, gdshader, godot");
        _ignoredFolderPresets.Add("Godot Engine", ".godot, export_presets, .import");
        _exclusionsPresets.Add("Godot Engine", "");

        _presets.Add("Godot Engine (GDExtension / C++)", "gd, tscn, tres, gdshader, godot, gdextension, cpp, h, hpp, c, cc");
        _ignoredFolderPresets.Add("Godot Engine (GDExtension / C++)", ".godot, export_presets, .import, .scons_cache, bin, obj, build, out");
        _exclusionsPresets.Add("Godot Engine (GDExtension / C++)", "");

        _presets.Add("Unity Engine", "cs, shader, cginc, json, xml, asmdef, inputactions, unity, prefab, mat, meta");
        _ignoredFolderPresets.Add("Unity Engine", "Library, Temp, obj, bin, ProjectSettings, Logs, UserSettings, .vs, .idea, Builds, Build, Fonts, StreamingAssets, TextMesh Pro, Plugins, Packages, Examples");
        _exclusionsPresets.Add("Unity Engine", "");

        // General Programming
        _presets.Add("C# (.NET / Visual Studio)", "cs, csproj, sln, xaml, config, json, cshtml, razor, sql, xml, props, targets, vb, fs");
        _ignoredFolderPresets.Add("C# (.NET / Visual Studio)", "bin, obj, .vs, packages, TestResults, .git, .idea, .vscode, artifacts");
        _exclusionsPresets.Add("C# (.NET / Visual Studio)", "");

        _presets.Add("Java (Maven/Gradle)", "java, xml, properties, fxml, gradle, groovy");
        _ignoredFolderPresets.Add("Java (Maven/Gradle)", "target, .idea, build, .settings, bin, out, .gradle");
        _exclusionsPresets.Add("Java (Maven/Gradle)", "");

        _presets.Add("Python", "py, requirements.txt, yaml, yml, json, toml, ini");
        _ignoredFolderPresets.Add("Python", "__pycache__, venv, env, .venv, .git, .idea, .vscode, build, dist, egg-info");
        _exclusionsPresets.Add("Python", "");

        _presets.Add("Go (Golang)", "go, mod, sum, yaml, yml, json, toml");
        _ignoredFolderPresets.Add("Go (Golang)", "vendor, bin, pkg, .git, .idea, .vscode, coverage");
        _exclusionsPresets.Add("Go (Golang)", "");

        // Systems & Frameworks
        _presets.Add("Rust / Tauri", "rs, toml, json, js, mjs, ts, jsx, tsx, html, css, scss");
        _ignoredFolderPresets.Add("Rust / Tauri", "target, node_modules, dist, build, .git, .vscode, .idea, icons, gen, .github, coverage");
        _exclusionsPresets.Add("Rust / Tauri", "");

        // Web
        string webIgnored = "node_modules, dist, build, .next, .nuxt, coverage, .git, .vscode, .idea";
        _presets.Add("Web (TypeScript / React)", "ts, tsx, jsx, html, css, scss, less, json, vue, svelte");
        _ignoredFolderPresets.Add("Web (TypeScript / React)", webIgnored);
        _exclusionsPresets.Add("Web (TypeScript / React)", "");

        _presets.Add("Web (JavaScript / Classic)", "js, mjs, html, css, json");
        _ignoredFolderPresets.Add("Web (JavaScript / Classic)", webIgnored);
        _exclusionsPresets.Add("Web (JavaScript / Classic)", "");

        foreach (var key in _presets.Keys) {
            _builtInNames.Add(key);
        }

        LoadCustomPresets();
    }

    public void LoadCustomPresets() {
        var toRemove = _presets.Keys.Where(k => !_builtInNames.Contains(k)).ToList();
        foreach (var key in toRemove) {
            _presets.Remove(key);
            _ignoredFolderPresets.Remove(key);
            _exclusionsPresets.Remove(key);
        }

        var customList = PreferenceManager.Instance.GetCustomPresets();
        foreach (var item in customList) {
            if (!_presets.ContainsKey(item.Name)) {
                _presets.Add(item.Name, item.Extensions);
                _ignoredFolderPresets.Add(item.Name, item.IgnoredFolders);
                _exclusionsPresets.Add(item.Name, item.Exclusions);
            }
        }
    }

    public IEnumerable<string> GetPresetNames() => _presets.Keys;
    public string GetExtensionsFor(string presetName) => _presets.TryGetValue(presetName, out var val) ? val : "";
    public string GetIgnoredFoldersFor(string presetName) => _ignoredFolderPresets.TryGetValue(presetName, out var val) ? val : "";
    public string GetExclusionsFor(string presetName) => _exclusionsPresets.TryGetValue(presetName, out var val) ? val : "";
    public bool HasPreset(string presetName) => _presets.ContainsKey(presetName);
    public bool IsPresetBuiltIn(string presetName) => _builtInNames.Contains(presetName);

    public void AddOrUpdatePreset(string name, string extensions, string ignoredFolders, string exclusions) {
        if (IsPresetBuiltIn(name)) return;
        _presets[name] = extensions;
        _ignoredFolderPresets[name] = ignoredFolders;
        _exclusionsPresets[name] = exclusions;

        var custom = PreferenceManager.Instance.GetCustomPresets();
        var existing = custom.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            existing.Extensions = extensions;
            existing.IgnoredFolders = ignoredFolders;
            existing.Exclusions = exclusions;
        }
        else {
            custom.Add(new PresetModel {
                Name = name,
                Extensions = extensions,
                IgnoredFolders = ignoredFolders,
                Exclusions = exclusions
            });
        }
        PreferenceManager.Instance.SetCustomPresets(custom);
    }

    public void DeletePreset(string name) {
        if (IsPresetBuiltIn(name)) return;
        _presets.Remove(name);
        _ignoredFolderPresets.Remove(name);
        _exclusionsPresets.Remove(name);

        var custom = PreferenceManager.Instance.GetCustomPresets();
        var existing = custom.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
            custom.Remove(existing);
            PreferenceManager.Instance.SetCustomPresets(custom);
        }
    }

    /// <summary>
    /// Robust heuristic and structural auto-detector.
    /// Performs a shallow walk up to depth 3, skipping noise folders, to accurately classify projects.
    /// </summary>
    public string? AutoDetectPreset(string rootPath) {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return null;

        try {
            var rootDir = new DirectoryInfo(rootPath);
            if (!rootDir.Exists) return null;

            // 1. Collect candidate entries (shallow walk up to depth 3, max 600 files to stay under 5ms)
            var scannedFiles = new List<FileInfo>();
            var scannedDirs = new List<DirectoryInfo>();
            CollectShallowEntries(rootDir, scannedFiles, scannedDirs, currentDepth: 0, maxDepth: 3, maxFiles: 600);

            var fileNames = new HashSet<string>(scannedFiles.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var dirNames = new HashSet<string>(scannedDirs.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

            // 2. High-Confidence Detection: Game Engines & System Frameworks
            // Godot Engine
            bool hasGodotProject = fileNames.Contains("project.godot");
            if (hasGodotProject) {
                bool hasGdExtension = scannedFiles.Any(f => f.Extension.Equals(".gdextension", StringComparison.OrdinalIgnoreCase));
                bool hasSConstruct = fileNames.Contains("SConstruct");
                bool hasCppSource = scannedFiles.Any(f =>
                    f.Extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".h", StringComparison.OrdinalIgnoreCase)
                );

                if (hasGdExtension || hasSConstruct || hasCppSource) {
                    return "Godot Engine (GDExtension / C++)";
                }
                return "Godot Engine";
            }

            // Unity Engine
            bool hasUnityAssets = dirNames.Contains("Assets");
            bool hasUnityProjectSettings = dirNames.Contains("ProjectSettings");
            bool hasUnityScenesOrPrefabs = scannedFiles.Any(f =>
                f.Extension.Equals(".unity", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase)
            );
            bool hasUnityManifest = scannedFiles.Any(f =>
                f.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                f.DirectoryName != null && f.DirectoryName.EndsWith("Packages", StringComparison.OrdinalIgnoreCase)
            );

            if ((hasUnityAssets && hasUnityProjectSettings) || (hasUnityAssets && hasUnityScenesOrPrefabs) || hasUnityManifest) {
                return "Unity Engine";
            }

            // Tauri / Rust
            bool hasTauriDir = dirNames.Contains("src-tauri");
            bool hasTauriConf = fileNames.Contains("tauri.conf.json") || fileNames.Contains("tauri.conf.json5");
            if (hasTauriDir || hasTauriConf) {
                return "Rust / Tauri";
            }

            // 3. Project Configuration Marker Detection
            // C# (.NET / Visual Studio)
            bool hasCsProj = scannedFiles.Any(f =>
                f.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            );

            // Java (Maven / Gradle)
            bool hasJavaBuild = fileNames.Contains("pom.xml") ||
                                fileNames.Contains("build.gradle") ||
                                fileNames.Contains("build.gradle.kts") ||
                                fileNames.Contains("settings.gradle") ||
                                fileNames.Contains("settings.gradle.kts") ||
                                fileNames.Contains("gradlew") ||
                                fileNames.Contains("mvnw");

            // Go (Golang)
            bool hasGoMod = fileNames.Contains("go.mod") ||
                            fileNames.Contains("go.sum") ||
                            fileNames.Contains("go.work");

            // Python
            bool hasPythonConfig = fileNames.Contains("pyproject.toml") ||
                                   fileNames.Contains("requirements.txt") ||
                                   fileNames.Contains("setup.py") ||
                                   fileNames.Contains("setup.cfg") ||
                                   fileNames.Contains("Pipfile") ||
                                   fileNames.Contains("environment.yml") ||
                                   fileNames.Contains("poetry.lock") ||
                                   dirNames.Contains("venv") ||
                                   dirNames.Contains(".venv");

            // Rust (Plain Cargo)
            bool hasCargoToml = fileNames.Contains("Cargo.toml") || fileNames.Contains("Cargo.lock");

            // Web (package.json)
            bool hasPackageJson = fileNames.Contains("package.json");

            // C# Solution Priority (Avoids false positive when .NET has a ClientApp/package.json)
            if (hasCsProj) {
                return "C# (.NET / Visual Studio)";
            }

            if (hasCargoToml) {
                return "Rust / Tauri";
            }

            if (hasGoMod) {
                return "Go (Golang)";
            }

            if (hasJavaBuild) {
                return "Java (Maven/Gradle)";
            }

            if (hasPythonConfig) {
                return "Python";
            }

            // Web: Differentiate TypeScript / React from Classic JavaScript
            if (hasPackageJson) {
                bool hasTsConfig = scannedFiles.Any(f => f.Name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) && f.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase));
                bool hasViteTs = fileNames.Contains("vite.config.ts");
                bool hasNextConfig = fileNames.Contains("next.config.js") || fileNames.Contains("next.config.mjs") || fileNames.Contains("next.config.ts");
                bool hasTsFiles = scannedFiles.Any(f =>
                    f.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".vue", StringComparison.OrdinalIgnoreCase) ||
                    f.Extension.Equals(".svelte", StringComparison.OrdinalIgnoreCase)
                );

                if (hasTsConfig || hasViteTs || hasNextConfig || hasTsFiles) {
                    return "Web (TypeScript / React)";
                }

                var pkgFile = scannedFiles.FirstOrDefault(f => f.Name.Equals("package.json", StringComparison.OrdinalIgnoreCase));
                if (pkgFile != null) {
                    try {
                        string pkgContent = File.ReadAllText(pkgFile.FullName);
                        if (pkgContent.Contains("\"react\"", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"typescript\"", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"@types/", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"vue\"", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"svelte\"", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"@angular/", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"next\"", StringComparison.OrdinalIgnoreCase) ||
                            pkgContent.Contains("\"vite\"", StringComparison.OrdinalIgnoreCase)) {
                            return "Web (TypeScript / React)";
                        }
                    }
                    catch { }
                }

                return "Web (JavaScript / Classic)";
            }

            // 4. Fallback: Extension Frequency Analysis (if project lacks build configuration files)
            var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in scannedFiles) {
                string ext = file.Extension.TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                extCounts[ext] = extCounts.GetValueOrDefault(ext) + 1;
            }

            int csCount = extCounts.GetValueOrDefault("cs");
            int pyCount = extCounts.GetValueOrDefault("py");
            int goCount = extCounts.GetValueOrDefault("go");
            int rsCount = extCounts.GetValueOrDefault("rs");
            int javaCount = extCounts.GetValueOrDefault("java") + extCounts.GetValueOrDefault("kt");
            int gdCount = extCounts.GetValueOrDefault("gd") + extCounts.GetValueOrDefault("tscn") + extCounts.GetValueOrDefault("tres");
            int tsReactCount = extCounts.GetValueOrDefault("ts") + extCounts.GetValueOrDefault("tsx") + extCounts.GetValueOrDefault("jsx") + extCounts.GetValueOrDefault("vue") + extCounts.GetValueOrDefault("svelte");
            int jsClassicCount = extCounts.GetValueOrDefault("js") + extCounts.GetValueOrDefault("mjs") + extCounts.GetValueOrDefault("html") + extCounts.GetValueOrDefault("css");

            var scores = new Dictionary<string, int> {
                { "Godot Engine", gdCount },
                { "C# (.NET / Visual Studio)", csCount },
                { "Python", pyCount },
                { "Go (Golang)", goCount },
                { "Rust / Tauri", rsCount },
                { "Java (Maven/Gradle)", javaCount },
                { "Web (TypeScript / React)", tsReactCount },
                { "Web (JavaScript / Classic)", jsClassicCount }
            };

            var best = scores.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (best.Value > 0) {
                return best.Key;
            }

            return null;
        }
        catch {
            return null;
        }
    }

    private void CollectShallowEntries(DirectoryInfo dir, List<FileInfo> files, List<DirectoryInfo> dirs, int currentDepth, int maxDepth, int maxFiles) {
        if (currentDepth > maxDepth || files.Count >= maxFiles) return;

        try {
            foreach (var file in dir.EnumerateFiles()) {
                files.Add(file);
                if (files.Count >= maxFiles) return;
            }
        }
        catch { }

        try {
            foreach (var subDir in dir.EnumerateDirectories()) {
                if (DetectionIgnoredDirs.Contains(subDir.Name)) continue;
                dirs.Add(subDir);
                CollectShallowEntries(subDir, files, dirs, currentDepth + 1, maxDepth, maxFiles);
                if (files.Count >= maxFiles) return;
            }
        }
        catch { }
    }
}