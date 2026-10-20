using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TxtConverter.Cli.Commands;

/// <summary>Lists the files a conversion would pick up.</summary>
internal sealed class ScanCommand : CliCommand {
    public override string Name => "scan";
    public override string Summary => "List the files that would be converted";
    public override string Usage => "scan [path] [options]";

    public override string Description =>
        "Applies the preset, extension, ignore and exclusion rules and prints the resulting file list.\n" +
        "[F] marks files merged in full, [E] marks files kept as stubs.";

    public override IReadOnlyList<OptionDef> Options => ProjectOptions.BuildWithSelection(
        new OptionDef { Name = "stats", TakesValue = false, Description = "Print a summary grouped by extension" },
        new OptionDef { Name = "limit", TakesValue = true, ValueName = "n", Description = "Print at most n paths" },
        new OptionDef { Name = "ai-task", TakesValue = true, ValueName = "text", Description = "Preview the file set AI Smart Select would choose" },
        new OptionDef { Name = "ai-model", TakesValue = true, ValueName = "name", Description = "Override the AI model for this preview" },
        new OptionDef { Name = "ai-budget", TakesValue = true, ValueName = "tokens", Description = "Override the AI thinking budget (Gemini only)" },
        new OptionDef { Name = "ai-system-prompt", TakesValue = true, ValueName = "file", Description = "Read the AI system prompt from a file" }
    );

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        EnsureNoExtraArgs(args, 1);

        string? aiTask = args.Value("ai-task");
        bool aiUsed = !string.IsNullOrWhiteSpace(aiTask);
        AiRequestOptions? aiOptions = null;
        if (aiUsed) {
            // Validate the AI options before scanning so bad input fails fast.
            aiOptions = AiRequestOptions.Resolve(args, ctx.Prefs);
        }
        else if (args.Has("ai-model") || args.Has("ai-budget") || args.Has("ai-system-prompt")) {
            ctx.Log.Warn("--ai-model, --ai-budget and --ai-system-prompt have no effect without --ai-task.");
        }

        var project = await ProjectResolver.ResolveAsync(args, ctx);
        ProjectResolver.ApplySelectionFlags(args, project, ctx);

        if (aiUsed) {
            if (project.FoundFiles.Count == 0)
                throw new CliException(
                    $"No files matched preset '{project.PresetName}'. Use --ext to override the extension list.",
                    ExitCodes.NothingToConvert);

            ctx.Log.Detail("Running AI Smart Select preview...");
            var outcome = await AiSelectionService.RunAsync(project, args, ctx, aiTask!, aiOptions);
            project.SelectedForMerge = new HashSet<string>(outcome.Files, StringComparer.OrdinalIgnoreCase);
        }

        int limit = args.IntValue("limit") ?? int.MaxValue;

        bool stats = args.Flag("stats") == true;
        if (!stats) {
            foreach (string file in project.FoundFiles.OrderBy(project.Relative, StringComparer.OrdinalIgnoreCase).Take(limit))
                ctx.Log.Line((project.SelectedForMerge.Contains(file) ? "[F] " : "[E] ") + project.Relative(file));
        }

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "scan" },
                { "root", project.RootPath },
                { "preset", project.PresetName },
                { "autoDetected", project.PresetAutoDetected },
                { "aiUsed", aiUsed },
                { "extensions", project.Extensions },
                { "ignoredFolders", project.IgnoredFolders },
                { "exclusions", project.ExclusionsCsv },
                { "filesFound", project.FoundFiles.Count },
                { "filesMerged", project.SelectedForMerge.Count },
                { "filesStubbed", project.FoundFiles.Count - project.SelectedForMerge.Count },
                { "byExtension", CountByExtension(project) },
                { "files", project.FoundFiles.OrderBy(project.Relative, StringComparer.OrdinalIgnoreCase).Take(limit).Select(project.Relative).ToList() }
            });
        }
        else {
            var byExtension = CountByExtension(project);
            if (stats) {
                ctx.Log.Section($"Found {project.FoundFiles.Count} files ({project.SelectedForMerge.Count} full, {project.FoundFiles.Count - project.SelectedForMerge.Count} stubs)");
                foreach (var pair in byExtension)
                    ctx.Log.Line($"  {pair.Key,-12} {pair.Value}");
            }
            else {
                ctx.Log.Detail($"Found {project.FoundFiles.Count} files ({project.SelectedForMerge.Count} full, {project.FoundFiles.Count - project.SelectedForMerge.Count} stubs)");
            }
        }

        if (project.FoundFiles.Count == 0) {
            ctx.Log.Error($"No files matched preset '{project.PresetName}'.");
            return ExitCodes.NothingToConvert;
        }

        return ExitCodes.Success;
    }

    private static Dictionary<string, int> CountByExtension(ResolvedProject project) {
        return project.FoundFiles
            .GroupBy(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => (g.Key.Length == 0 ? "(no extension)" : g.Key), g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }
}