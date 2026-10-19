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
        new OptionDef { Name = "limit", TakesValue = true, ValueName = "n", Description = "Print at most n paths" }
    );

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        EnsureNoExtraArgs(args, 1);
        var project = await ProjectResolver.ResolveAsync(args, ctx);
        ProjectResolver.ApplySelectionFlags(args, project, ctx);
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