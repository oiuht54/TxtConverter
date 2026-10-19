using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Core.Logic.Processing;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>
/// Self test of the CLI and the conversion pipeline. Creates a throw away sample project in the
/// temp folder and runs the real production code against it: scanning, preset auto detection,
/// compression, structure map, merged file, PDF rendering, AI path matching and glob selection.
/// No network access and no telemetry unless --online is passed.
/// </summary>
internal sealed class TestCommand : CliCommand {
    public override string Name => "test";
    public override string Summary => "Verify the CLI and the conversion pipeline (self test)";
    public override string Usage => "test [options]";

    public override string Description =>
        "Builds a temporary sample project, runs the whole conversion chain on it and checks the results.\n" +
        "Nothing outside the temp folder is touched, no telemetry is sent and (unless --online is used)\n" +
        "no network request is made.\n\n" +
        "Exit code 0 means every check passed.";

    public override IReadOnlyList<OptionDef> Options => new List<OptionDef> {
        new OptionDef { Name = "keep", TakesValue = false, Description = "Keep the temporary sample project for inspection" },
        new OptionDef { Name = "online", TakesValue = false, Description = "Also run checks that need internet access (GitHub release check)" }
    };

    private sealed record CheckResult(string Name, string Status, string Detail);

    private const string StatusPass = "PASS";
    private const string StatusFail = "FAIL";
    private const string StatusSkip = "SKIP";

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        EnsureNoExtraArgs(args, 0);
        bool online = args.Flag("online") == true;
        bool keep = args.Flag("keep") == true;

        // A self test must stay silent.
        Services.TelemetryService.Instance.Suppress = true;

        string fixtureRoot = Path.Combine(Path.GetTempPath(), "TxtConverterCliTest_" + Guid.NewGuid().ToString("N")[..8]);
        var results = new List<CheckResult>();
        var stopwatch = Stopwatch.StartNew();

        ctx.Log.Line($"TxtConverter self test v{ProjectConstants.CurrentVersion}");
        ctx.Log.Detail($"Sample project: {fixtureRoot}");
        ctx.Log.Detail("Telemetry is disabled for this command.");
        ctx.Log.Line("");

        try {
            results.Add(CheckRuntime());
            results.Add(CheckSettings());
            results.Add(CheckLocalization());
            results.Add(CheckPresets());
            CreateFixture(fixtureRoot);
            results.Add(CheckAutoDetect(fixtureRoot));
            results.Add(CheckScan(fixtureRoot));
            results.Add(CheckCompression(fixtureRoot));
            results.Add(CheckGlobs());
            results.Add(CheckAiMatcher(fixtureRoot));

            var project = ResolveFixture(fixtureRoot, ctx);
            var convert = RunConversion(project, ctx, CompressionLevel.Smart);
            results.Add(CheckMerged(convert));
            results.Add(CheckPdf(convert));

            results.Add(CheckCompressionSelection(project, ctx));

            results.Add(online
                ? await CheckOnlineAsync(ctx)
                : new CheckResult("update-check", StatusSkip, "network checks disabled (use --online)"));
        }
        catch (Exception ex) {
            results.Add(new CheckResult("test-runner", StatusFail, ex.Message));
        }
        finally {
            stopwatch.Stop();
            if (keep) {
                ctx.Log.Detail($"Sample project kept at: {fixtureRoot}");
            }
            else {
                TryDeleteDirectory(fixtureRoot);
            }
        }

        PrintResults(results, ctx);
        return results.Any(r => r.Status == StatusFail) ? ExitCodes.Failure : ExitCodes.Success;
    }

    // ---------------------------------------------------------------- checks

    private static CheckResult CheckRuntime() {
        string detail = $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier ?? "portable"})";
        return new CheckResult("runtime", StatusPass, detail);
    }

    private static CheckResult CheckSettings() {
        var prefs = PreferenceManager.Instance;
        string language = prefs.GetLanguage();
        if (language is not (ProjectConstants.LangEn or ProjectConstants.LangRu))
            return new CheckResult("settings", StatusFail, $"unexpected saved language '{language}'");
        return new CheckResult("settings", StatusPass, prefs.SettingsPath);
    }

    private static CheckResult CheckLocalization() {
        var manager = LanguageManager.Instance;
        string original = PreferenceManager.Instance.GetLanguage();

        try {
            manager.SetLanguage(ProjectConstants.LangEn, persist: false);
            string english = manager.GetString("report_structure_header");
            if (english.StartsWith("!") || string.IsNullOrWhiteSpace(english))
                return new CheckResult("localization", StatusFail, "English resource dictionary did not load");

            manager.SetLanguage(ProjectConstants.LangRu, persist: false);
            string russian = manager.GetString("report_structure_header");
            if (russian.StartsWith("!") || string.IsNullOrWhiteSpace(russian))
                return new CheckResult("localization", StatusFail, "Russian resource dictionary did not load");

            if (english == russian)
                return new CheckResult("localization", StatusFail, "en and ru dictionaries are identical");

            return new CheckResult("localization", StatusPass, "en + ru resource dictionaries loaded");
        }
        finally {
            manager.SetLanguage(original, persist: false);
        }
    }

    private static CheckResult CheckPresets() {
        var presets = PresetManager.Instance;
        int count = presets.GetPresetNames().Count();

        if (count == 0) return new CheckResult("presets", StatusFail, "no presets loaded");
        if (!presets.IsPresetBuiltIn("Godot Engine"))
            return new CheckResult("presets", StatusFail, "built-in preset 'Godot Engine' is missing");
        if (!presets.GetExtensionsFor("Godot Engine").Contains("gd"))
            return new CheckResult("presets", StatusFail, "preset extensions were not returned");

        int custom = presets.GetPresetNames().Count(name => !presets.IsPresetBuiltIn(name));
        return new CheckResult("presets", StatusPass, $"{count} presets ({custom} custom)");
    }

    private static CheckResult CheckAutoDetect(string root) {
        string? detected = PresetManager.Instance.AutoDetectPreset(root);
        return detected == "Godot Engine"
            ? new CheckResult("auto-detect", StatusPass, "project.godot detected as 'Godot Engine'")
            : new CheckResult("auto-detect", StatusFail, $"expected 'Godot Engine', got '{detected ?? "nothing"}'");
    }

    private static CheckResult CheckScan(string root) {
        var scanner = new FileScanner(
            SplitPresetList(PresetManager.Instance.GetExtensionsFor("Godot Engine")),
            SplitPresetList(PresetManager.Instance.GetIgnoredFoldersFor("Godot Engine")));

        List<string> files = scanner.ScanAsync(root).GetAwaiter().GetResult();
        var names = files.Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] expected = { "project.godot", "player.gd", "enemy.gd", "main.tscn", "README.md" };
        var missing = expected.Where(e => !names.Contains(e)).ToList();
        if (missing.Count > 0)
            return new CheckResult("scan", StatusFail, "missing: " + string.Join(", ", missing));

        if (names.Contains("ThirdParty.cs"))
            return new CheckResult("scan", StatusFail, "extension filter leaked a .cs file into a Godot preset");

        return new CheckResult("scan", StatusPass, $"{files.Count} files matched the preset");
    }

    private static CheckResult CheckCompression(string root) {
        string sample = Path.Combine(root, "scripts", "sample.gd");
        string processedNone = new FileContentProcessor(CompressionLevel.None).ReadAndProcess(sample);
        string processedSmart = new FileContentProcessor(CompressionLevel.Smart).ReadAndProcess(sample);
        string processedMax = new FileContentProcessor(CompressionLevel.Maximum).ReadAndProcess(sample);

        if (processedNone.Length == 0 || processedSmart.Length == 0 || processedMax.Length == 0)
            return new CheckResult("compression", StatusFail, "a compression level produced empty output");

        if (!processedNone.Contains("SELF_TEST_COMMENT", StringComparison.Ordinal))
            return new CheckResult("compression", StatusFail, "'none' level unexpectedly dropped content");

        if (!processedNone.Contains("\n\n\n", StringComparison.Ordinal))
            return new CheckResult("compression", StatusFail, "fixture no longer contains repeated empty lines");

        // Smart is documented to collapse 3+ newlines into 2.
        if (processedSmart.Contains("\n\n\n", StringComparison.Ordinal))
            return new CheckResult("compression", StatusFail, "'smart' level kept repeated empty lines");

        // Maximum must apply the Godot scene optimizer (repetition collapse).
        string scene = Path.Combine(root, "scenes", "repeated.tscn");
        string sceneNone = new FileContentProcessor(CompressionLevel.None).ReadAndProcess(scene);
        string sceneMax = new FileContentProcessor(CompressionLevel.Maximum).ReadAndProcess(scene);

        if (!sceneMax.Contains("@Repeated(3)", StringComparison.Ordinal))
            return new CheckResult("compression", StatusFail, "maximum level did not collapse repeated nodes");
        if (sceneMax.Length > sceneNone.Length)
            return new CheckResult("compression", StatusFail, "maximum level did not shrink the scene file");

        return new CheckResult("compression", StatusPass,
            $"gd none {processedNone.Length}B / smart {processedSmart.Length}B / max {processedMax.Length}B, tscn {sceneNone.Length}B -> {sceneMax.Length}B");
    }

    private static CheckResult CheckGlobs() {
        var cases = new (string Pattern, string Path, bool Expected)[] {
            ("*.gd", "scripts/player.gd", true),
            ("*.gd", "scenes/main.tscn", false),
            ("scripts/**", "scripts/enemy.gd", true),
            ("scripts/**", "vendor/lib.gd", false),
            ("**/*.tscn", "scenes/main.tscn", true),
            ("vendor/", "vendor/ThirdParty.cs", true),
            ("*.gd", "README.md", false)
        };

        var wrong = cases
            .Where(c => GlobMatcher.IsMatch(c.Path, c.Pattern) != c.Expected)
            .Select(c => c.Pattern + " vs " + c.Path)
            .ToList();

        return wrong.Count == 0
            ? new CheckResult("glob-matching", StatusPass, $"{cases.Length} patterns verified")
            : new CheckResult("glob-matching", StatusFail, "mismatch: " + string.Join("; ", wrong));
    }

    private static CheckResult CheckAiMatcher(string root) {
        var files = new FileScanner(
            SplitPresetList(PresetManager.Instance.GetExtensionsFor("Godot Engine")), new List<string>())
            .ScanAsync(root).GetAwaiter().GetResult();

        var suggestions = new List<string> { "scripts/player.gd", "main.tscn", "does/not/exist.gd" };
        List<string> matched = AiFileMatcher.Match(files, root, suggestions);

        bool hasScript = matched.Any(m => m.EndsWith("player.gd", StringComparison.OrdinalIgnoreCase));
        bool hasScene = matched.Any(m => m.EndsWith("main.tscn", StringComparison.OrdinalIgnoreCase));

        if (!hasScript || !hasScene)
            return new CheckResult("ai-path-matching", StatusFail, $"only matched {matched.Count} of the suggested files");

        return new CheckResult("ai-path-matching", StatusPass, $"{matched.Count} of {suggestions.Count} suggestions resolved");
    }

    private static ResolvedProject ResolveFixture(string root, CliContext ctx) {
        var args = new ParsedArgs();
        args.Positional.Add(root);
        args.AddValue("preset", "Godot Engine");
        // Folder rules need a trailing slash, exactly like in the GUI exclusion box.
        args.AddValue("exclude", "vendor/");

        bool previousQuiet = ctx.Log.Quiet;
        ctx.Log.Quiet = true;
        try {
            return ProjectResolver.ResolveAsync(args, ctx).GetAwaiter().GetResult();
        }
        finally {
            ctx.Log.Quiet = previousQuiet;
        }
    }

    private static ConversionResult RunConversion(ResolvedProject project, CliContext ctx, CompressionLevel compression) {
        var orchestrator = new ConversionOrchestrator(
            project.RootPath,
            project.FoundFiles,
            project.SelectedForMerge,
            project.IgnoredFolders,
            genStructure: true,
            compactMode: true,
            compression,
            genMerged: true,
            genPdf: true,
            PdfMode.Standard);

        // The self test prints its own report, so mute the pipeline progress output.
        bool previousQuiet = ctx.Log.Quiet;
        ctx.Log.Quiet = true;
        try {
            orchestrator.RunAsync(new Progress<double>(), new Progress<string>()).GetAwaiter().GetResult();
        }
        finally {
            ctx.Log.Quiet = previousQuiet;
        }

        string outputDir = Path.Combine(project.RootPath, ProjectConstants.OutputDirName);
        string projectName = Path.GetFileName(project.RootPath);
        return new ConversionResult(
            outputDir,
            Path.Combine(outputDir, "_" + projectName + ProjectConstants.MergedFileSuffix),
            Path.Combine(outputDir, "_" + projectName + "_Report.pdf"),
            project);
    }

    /// <summary>
    /// Selecting a compression mode must really change the produced report:
    /// the same project is converted with 'smart' and with 'maximum' and the results are compared.
    /// </summary>
    private static CheckResult CheckCompressionSelection(ResolvedProject project, CliContext ctx) {
        var none = RunConversion(project, ctx, CompressionLevel.None);
        long noneSize = new FileInfo(none.MergedFile).Length;
        string noneContent = File.ReadAllText(none.MergedFile);

        var max = RunConversion(project, ctx, CompressionLevel.Maximum);
        long maxSize = new FileInfo(max.MergedFile).Length;
        string maxContent = File.ReadAllText(max.MergedFile);

        // Report format differs per mode: 'none' uses the verbose header, every other mode the compact one.
        string verboseHeader = string.Format(
            LanguageManager.Instance.GetString("report_merged_header"),
            Path.GetFileName(project.RootPath));
        if (!noneContent.Contains(verboseHeader, StringComparison.Ordinal))
            return new CheckResult("compression-mode", StatusFail, "'none' did not produce the verbose report format");
        if (!maxContent.Contains("# Project:", StringComparison.Ordinal))
            return new CheckResult("compression-mode", StatusFail, "'maximum' did not produce the compact report format");

        if (!maxContent.Contains("@Repeated(3)", StringComparison.Ordinal))
            return new CheckResult("compression-mode", StatusFail, "'maximum' mode did not apply the Godot scene optimizer");
        if (noneContent.Contains("@Repeated(3)", StringComparison.Ordinal))
            return new CheckResult("compression-mode", StatusFail, "'none' mode unexpectedly compacted the scene");
        if (maxSize >= noneSize)
            return new CheckResult("compression-mode", StatusFail, $"maximum ({maxSize} B) is not smaller than none ({noneSize} B)");

        return new CheckResult("compression-mode", StatusPass,
            $"none {noneSize / 1024} KB vs maximum {maxSize / 1024} KB (Godot optimizer applied)");
    }

    private static CheckResult CheckMerged(ConversionResult conversion) {
        if (!File.Exists(conversion.MergedFile))
            return new CheckResult("convert-merged", StatusFail, "merged file was not created");

        string content = File.ReadAllText(conversion.MergedFile);
        string structureHeader = LanguageManager.Instance.GetString("report_structure_header");
        string stubLabel = LanguageManager.Instance.GetString("report_stub_label");

        if (!content.Contains(structureHeader))
            return new CheckResult("convert-merged", StatusFail, "structure map is missing from the merged file");

        if (!content.Contains("SELF_TEST_MARKER"))
            return new CheckResult("convert-merged", StatusFail, "file contents are missing from the merged file");

        if (content.Contains("ThirdParty.cs", StringComparison.Ordinal))
            return new CheckResult("convert-merged", StatusFail, "extension filter leaked vendor/ThirdParty.cs into the report");

        // vendor_lib.gd is scanned by the preset but excluded by the stub rules.
        if (!content.Contains("vendor_lib.gd", StringComparison.Ordinal))
            return new CheckResult("convert-merged", StatusFail, "excluded file is not listed at all");

        string omitted = LanguageManager.Instance.GetString("report_omitted");
        if (!content.Contains(omitted, StringComparison.Ordinal))
            return new CheckResult("convert-merged", StatusFail, "excluded file is not written as a stub");

        if (stubLabel.Length > 0 && !content.Contains("[E]"))
            return new CheckResult("convert-merged", StatusFail, "structure map does not mark excluded files with [E]");

        int perFile = Directory.GetFiles(conversion.OutputDir, "*.txt").Length;
        if (perFile < conversion.Project.FoundFiles.Count)
            return new CheckResult("convert-merged", StatusFail, $"only {perFile} per-file outputs were written");

        return new CheckResult("convert-merged", StatusPass,
            $"{Path.GetFileName(conversion.MergedFile)} ({new FileInfo(conversion.MergedFile).Length} B) + {perFile} txt files");
    }

    private static CheckResult CheckPdf(ConversionResult conversion) {
        if (!File.Exists(conversion.PdfFile))
            return new CheckResult("convert-pdf", StatusFail, "PDF report was not created");

        var info = new FileInfo(conversion.PdfFile);
        if (info.Length < 1024)
            return new CheckResult("convert-pdf", StatusFail, $"PDF is suspiciously small ({info.Length} B)");

        using var stream = File.OpenRead(conversion.PdfFile);
        byte[] header = new byte[4];
        int read = stream.Read(header, 0, header.Length);
        string magic = Encoding.ASCII.GetString(header, 0, Math.Max(0, read));

        if (magic != "%PDF")
            return new CheckResult("convert-pdf", StatusFail, $"unexpected file header '{magic}'");

        return new CheckResult("convert-pdf", StatusPass, $"{Path.GetFileName(conversion.PdfFile)} ({info.Length / 1024} KB)");
    }

    private static async Task<CheckResult> CheckOnlineAsync(CliContext ctx) {
        try {
            UpdateCheckResult result = await UpdateCheckerService.Instance.CheckForUpdatesDetailedAsync();
            if (!result.Success)
                return new CheckResult("update-check", StatusFail, result.ErrorMessage);

            string latest = result.Release?.TagName ?? ProjectConstants.CurrentVersion;
            return new CheckResult("update-check", StatusPass, $"current {ProjectConstants.CurrentVersion}, latest {latest}");
        }
        catch (Exception ex) {
            return new CheckResult("update-check", StatusFail, ex.Message);
        }
    }

    // ---------------------------------------------------------------- fixture

    private sealed record ConversionResult(
        string OutputDir,
        string MergedFile,
        string PdfFile,
        ResolvedProject Project);

    /// <summary>Creates a tiny Godot project that exercises every stage of the pipeline.</summary>
    private static void CreateFixture(string root) {
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        Directory.CreateDirectory(Path.Combine(root, "scenes"));
        Directory.CreateDirectory(Path.Combine(root, "vendor"));
        Directory.CreateDirectory(Path.Combine(root, ".godot", "cache"));

        File.WriteAllText(Path.Combine(root, "project.godot"),
            "; Engine configuration file.\nconfig_version=5\n\n[application]\nconfig/name=\"Self Test\"\n");

        File.WriteAllText(Path.Combine(root, "scripts", "player.gd"),
            "extends CharacterBody2D\n\n# SELF_TEST_COMMENT: this comment must disappear in maximum mode\nconst SPEED = 300.0 # SELF_TEST_MARKER\n\n\n\nfunc _physics_process(delta: float) -> void:\n\tvelocity = SPEED\n");

        File.WriteAllText(Path.Combine(root, "scripts", "enemy.gd"),
            "extends Node2D\n\n@export var health: int = 10\n");

        File.WriteAllText(Path.Combine(root, "scenes", "main.tscn"),
            "[gd_scene load_steps=2 format=3]\n\n[node name=\"Main\" type=\"Node2D\"]\n");

        File.WriteAllText(Path.Combine(root, "README.md"), "# Self test fixture\n");

        File.WriteAllText(Path.Combine(root, "scripts", "sample.gd"),
            "# SELF_TEST_COMMENT\nvar a = 1\n\n\n\nvar b = 2\n");

        File.WriteAllText(Path.Combine(root, "scenes", "repeated.tscn"),
            "[gd_scene format=3]\n\n" +
            "[node name=\"Tile1\" type=\"Sprite2D\"]\nposition = Vector2(0, 0)\n\n" +
            "[node name=\"Tile2\" type=\"Sprite2D\"]\nposition = Vector2(16, 0)\n\n" +
            "[node name=\"Tile3\" type=\"Sprite2D\"]\nposition = Vector2(32, 0)\n");

        File.WriteAllText(Path.Combine(root, "README.md"), "# Self test fixture\n");

        File.WriteAllText(Path.Combine(root, "vendor", "vendor_lib.gd"), "var vendor = true\n");
        File.WriteAllText(Path.Combine(root, "vendor", "ThirdParty.cs"), "// vendor code\npublic class ThirdParty {}\n");
        File.WriteAllText(Path.Combine(root, ".godot", "cache", "ignored.gd"), "var ignored = true\n");
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch {
            // Temp files left behind are not worth failing a test run over.
        }
    }

    private static List<string> SplitPresetList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    // ---------------------------------------------------------------- output

    private void PrintResults(List<CheckResult> results, CliContext ctx) {
        int passed = results.Count(r => r.Status == StatusPass);
        int failed = results.Count(r => r.Status == StatusFail);
        int skipped = results.Count(r => r.Status == StatusSkip);

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", failed == 0 },
                { "command", "test" },
                { "version", ProjectConstants.CurrentVersion },
                { "passed", passed },
                { "failed", failed },
                { "skipped", skipped },
                { "checks", results.Select(r => (object)new Dictionary<string, object?> {
                    { "name", r.Name }, { "status", r.Status }, { "detail", r.Detail }
                }).ToList() }
            });
            return;
        }

        int width = results.Count == 0 ? 8 : results.Max(r => r.Name.Length);
        foreach (CheckResult result in results) {
            string tag = "[" + result.Status + "]";
            if (result.Status == StatusPass) ctx.Log.Success($"  {tag,-6} {result.Name.PadRight(width)}  {result.Detail}");
            else if (result.Status == StatusSkip) ctx.Log.Detail($"  {tag,-6} {result.Name.PadRight(width)}  {result.Detail}");
            else ctx.Log.Error($"  {tag,-6} {result.Name.PadRight(width)}  {result.Detail}");
        }

        ctx.Log.Line("");
        if (failed == 0)
            ctx.Log.Section($"All {passed} checks passed{(skipped > 0 ? $" ({skipped} skipped)" : "")}.");
        else
            ctx.Log.Section($"{failed} of {results.Count} checks FAILED.");
    }
}