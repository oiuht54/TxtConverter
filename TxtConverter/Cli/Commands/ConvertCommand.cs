using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>Full conversion pipeline: scan -> file selection -> txt / merged / pdf reports.</summary>
internal sealed class ConvertCommand : CliCommand {
    public override string Name => "convert";
    public override string Summary => "Convert a project folder into AI ready text reports";
    public override string Usage => "convert [path] [options]";

    public override string Description =>
        "Scans the project, compresses every matching file and writes the results into the output folder.\n" +
        "Files that are not selected are still listed in the report, but only as stubs.\n\n" +
        "The output folder is <path>/" + ProjectConstants.OutputDirName + " unless --output is given.";

    public override IReadOnlyList<OptionDef> Options => ProjectOptions.BuildWithSelection(
        new OptionDef { Name = "compression", Short = 'c', TakesValue = true, ValueName = "none|smart|maximum", Description = "Token compression level (default: from settings)" },
        new OptionDef { Name = "structure", TakesValue = false, Negatable = true, Description = "Embed the file structure map in the report" },
        new OptionDef { Name = "compact-structure", TakesValue = false, Negatable = true, Description = "Collapse stub entries in the structure map" },
        new OptionDef { Name = "merged", TakesValue = false, Negatable = true, Description = "Generate the single merged source file" },
        new OptionDef { Name = "pdf", TakesValue = false, Negatable = true, Description = "Generate the PDF report" },
        new OptionDef { Name = "pdf-mode", TakesValue = true, ValueName = "standard|compact|extreme", Description = "PDF layout mode" },
        new OptionDef { Name = "output", Short = 'o', TakesValue = true, ValueName = "dir", Description = "Output directory (default: " + ProjectConstants.OutputDirName + ")" },
        new OptionDef { Name = "dry-run", TakesValue = false, Description = "Show what would be converted and where, write nothing" },
        new OptionDef { Name = "ai-task", TakesValue = true, ValueName = "text", Description = "Let the AI pick the relevant files before converting" },
        new OptionDef { Name = "ai-model", TakesValue = true, ValueName = "name", Description = "Override the AI model for this run" },
        new OptionDef { Name = "ai-budget", TakesValue = true, ValueName = "tokens", Description = "Override the AI thinking budget (Gemini only)" },
        new OptionDef { Name = "ai-system-prompt", TakesValue = true, ValueName = "file", Description = "Read the AI system prompt from a file" }
    );

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        var prefs = ctx.Prefs;
        EnsureNoExtraArgs(args, 1);

        // Validate the value options before touching the file system so bad input fails fast.
        CompressionLevel compression = args.EnumValue<CompressionLevel>("compression") ?? prefs.GetCompressionLevel();
        PdfMode pdfMode = args.EnumValue<PdfMode>("pdf-mode") ?? prefs.GetPdfMode();

        string? aiTask = args.Value("ai-task");
        AiRequestOptions? aiOptions = null;
        if (!string.IsNullOrWhiteSpace(aiTask)) {
            // Validate the AI options before scanning so bad input fails fast.
            aiOptions = AiRequestOptions.Resolve(args, prefs);
        }
        else if (args.Has("ai-model") || args.Has("ai-budget") || args.Has("ai-system-prompt")) {
            ctx.Log.Warn("--ai-model, --ai-budget and --ai-system-prompt have no effect without --ai-task.");
        }

        var project = await ProjectResolver.ResolveAsync(args, ctx);
        ProjectResolver.ApplySelectionFlags(args, project, ctx);

        if (!string.IsNullOrWhiteSpace(aiTask)) {
            ctx.Log.Detail("Running AI Smart Select...");
            var outcome = await AiSelectionService.RunAsync(project, args, ctx, aiTask, aiOptions);
            project.SelectedForMerge = new HashSet<string>(outcome.Files, StringComparer.OrdinalIgnoreCase);
            ctx.Log.Detail($"AI selected {project.SelectedForMerge.Count} files.");
        }

        if (project.FoundFiles.Count == 0)
            throw new CliException(
                $"No files matched preset '{project.PresetName}' (extensions: {string.Join(",", project.Extensions)}). " +
                "Use --ext to override the extension list.", ExitCodes.NothingToConvert);

        bool genStructure = args.Flag("structure") ?? prefs.GetGenerateStructure();
        bool compactStructure = args.Flag("compact-structure") ?? prefs.GetCompactMode();
        bool genMerged = args.Flag("merged") ?? prefs.GetGenerateMerged();
        bool genPdf = args.Flag("pdf") ?? prefs.GetGeneratePdf();

        if (compactStructure && !genStructure)
            ctx.Log.Warn("--compact-structure has no effect without --structure.");

        string? outputOverride = args.Value("output");
        string outputDir = string.IsNullOrWhiteSpace(outputOverride)
            ? Path.Combine(project.RootPath, ProjectConstants.OutputDirName)
            : (Path.IsPathRooted(outputOverride) ? outputOverride : Path.Combine(project.RootPath, outputOverride));

        ctx.Log.Detail($"Preset: {project.PresetName}{(project.PresetAutoDetected ? " (auto-detected)" : "")}");
        ctx.Log.Detail($"Files found: {project.FoundFiles.Count}, merged in full: {project.SelectedForMerge.Count}, stubs: {project.FoundFiles.Count - project.SelectedForMerge.Count}");
        ctx.Log.Detail($"Compression: {compression}, PDF: {(genPdf ? pdfMode.ToString() : "off")}, merged file: {(genMerged ? "on" : "off")}");

        if (args.Flag("dry-run") == true) {
            if (ctx.Json) {
                ctx.Log.EmitJson(new Dictionary<string, object?> {
                    { "ok", true },
                    { "command", "convert" },
                    { "dryRun", true },
                    { "root", project.RootPath },
                    { "preset", project.PresetName },
                    { "outputDirectory", outputDir },
                    { "filesFound", project.FoundFiles.Count },
                    { "filesMerged", project.SelectedForMerge.Count },
                    { "filesStubbed", project.FoundFiles.Count - project.SelectedForMerge.Count },
                    { "compression", compression },
                    { "generateStructure", genStructure },
                    { "generateMerged", genMerged },
                    { "generatePdf", genPdf },
                    { "pdfMode", pdfMode },
                    { "files", project.FoundFiles.Select(project.Relative).ToList() }
                });
            }
            else {
                ctx.Log.Section("Dry run - nothing was written");
                ctx.Log.Line($"Output directory: {outputDir}");
                ctx.Log.Line($"Merged in full ({project.SelectedForMerge.Count}):");
                foreach (string file in project.SelectedForMerge.OrderBy(project.Relative, StringComparer.OrdinalIgnoreCase).Take(200))
                    ctx.Log.Line("  [F] " + project.Relative(file));
                if (project.SelectedForMerge.Count > 200)
                    ctx.Log.Line($"  ... and {project.SelectedForMerge.Count - 200} more");
            }
            return ExitCodes.Success;
        }

        var orchestrator = new ConversionOrchestrator(
            project.RootPath,
            project.FoundFiles,
            project.SelectedForMerge,
            project.IgnoredFolders,
            genStructure,
            compactStructure,
            compression,
            genMerged,
            genPdf,
            pdfMode,
            outputOverride);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastPercent = -1;
        var progress = new DirectProgress<double>(value => {
            int percent = (int)(value * 100);
            if (percent % 5 == 0 && percent != lastPercent) {
                lastPercent = percent;
                ctx.Log.Detail($"Processing files: {percent}%");
            }
        });
        var status = new DirectProgress<string>(message => ctx.Log.Detail(message));

        await orchestrator.RunAsync(progress, status);
        stopwatch.Stop();

        TelemetryService.Instance.TrackEvent("conversion_completed", new Dictionary<string, object> {
            { "surface", "cli" },
            { "files_processed", project.FoundFiles.Count },
            { "files_merged", project.SelectedForMerge.Count },
            { "duration_ms", (long)stopwatch.Elapsed.TotalMilliseconds },
            { "preset", project.PresetName },
            { "compression", compression.ToString() },
            { "pdf_generated", genPdf },
            { "pdf_mode", pdfMode.ToString() }
        });

        var artifacts = DescribeOutput(outputDir, project);

        // Remember the folder and preset like the GUI does, so a later run without arguments works.
        prefs.SetLastSourceDir(project.RootPath);
        prefs.SetLastPreset(project.PresetName);

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "convert" },
                { "root", project.RootPath },
                { "preset", project.PresetName },
                { "outputDirectory", outputDir },
                { "filesFound", project.FoundFiles.Count },
                { "filesMerged", project.SelectedForMerge.Count },
                { "filesStubbed", project.FoundFiles.Count - project.SelectedForMerge.Count },
                { "compression", compression },
                { "durationMs", (long)stopwatch.Elapsed.TotalMilliseconds },
                { "artifacts", artifacts }
            });
        }
        else {
            ctx.Log.Section($"Done in {stopwatch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s");
            ctx.Log.Line("Results: " + outputDir);
            foreach (string artifact in artifacts)
                ctx.Log.Line("  " + artifact);
        }

        return ExitCodes.Success;
    }

    private static List<string> DescribeOutput(string outputDir, ResolvedProject project) {
        var artifacts = new List<string>();
        if (!Directory.Exists(outputDir)) return artifacts;

        string projectName = Path.GetFileName(project.RootPath.TrimEnd(Path.DirectorySeparatorChar));
        string merged = Path.Combine(outputDir, "_" + projectName + ProjectConstants.MergedFileSuffix);
        string pdf = Path.Combine(outputDir, "_" + projectName + "_Report.pdf");

        if (File.Exists(merged))
            artifacts.Add($"{Path.GetFileName(merged)} ({new FileInfo(merged).Length / 1024} KB, full source)");
        if (File.Exists(pdf))
            artifacts.Add($"{Path.GetFileName(pdf)} ({new FileInfo(pdf).Length / 1024} KB, PDF report)");

        int perFile = Directory.GetFiles(outputDir, "*.txt").Length - (File.Exists(merged) ? 1 : 0);
        if (perFile > 0)
            artifacts.Add($"{perFile} compressed .txt files");

        return artifacts;
    }

    private sealed class DirectProgress<T> : IProgress<T> {
        private readonly Action<T> _handler;
        public DirectProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}