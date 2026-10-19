using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>Preset management (mirrors the preset section of the main window).</summary>
internal sealed class PresetsCommand : CliCommand {
    public override string Name => "presets";
    public override string Summary => "list | show | save | delete preset definitions";
    public override string Usage => "presets <list|show|save|delete> [name] [options]";

    public override string Description =>
        "Actions:\n" +
        "  list             Print every built-in and custom preset\n" +
        "  show <name>      Print extensions, ignored folders and exclusions of one preset\n" +
        "  save <name>      Create or update a custom preset from the given options\n" +
        "  delete <name>    Delete a custom preset (built-in presets are protected)";

    public override IReadOnlyList<OptionDef> Options => new List<OptionDef> {
        new OptionDef { Name = "ext", TakesValue = true, ValueName = "list", Repeatable = true, Description = "Extensions for 'save'" },
        new OptionDef { Name = "ignore", TakesValue = true, ValueName = "list", Repeatable = true, Description = "Ignored folders for 'save'" },
        new OptionDef { Name = "exclude", TakesValue = true, ValueName = "list", Repeatable = true, Description = "Exclusions (stub rules) for 'save'" },
        new OptionDef { Name = "from", TakesValue = true, ValueName = "preset", Description = "Copy the values of an existing preset before applying the options above" }
    };

    public override Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        string action = args.Positional.Count > 0 ? args.Positional[0].ToLowerInvariant() : "list";
        if (args.Positional.Count > 0) args.Positional.RemoveAt(0);

        return Task.FromResult(action switch {
            "list" => List(args, ctx),
            "show" => Show(args, ctx),
            "save" => Save(args, ctx),
            "delete" => Delete(args, ctx),
            _ => throw CliException.Usage($"Unknown action '{action}'. Allowed: list, show, save, delete.")
        });
    }

    private static int List(ParsedArgs args, CliContext ctx) {
        var presets = PresetManager.Instance.GetPresetNames().ToList();
        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "presets list" },
                { "presets", presets.Select(Describe).ToList() }
            });
            return ExitCodes.Success;
        }

        ctx.Log.Section($"Presets ({presets.Count})");
        foreach (string preset in presets) {
            string type = PresetManager.Instance.IsPresetBuiltIn(preset) ? "built-in" : "custom";
            int extCount = SplitList(PresetManager.Instance.GetExtensionsFor(preset)).Count;
            ctx.Log.Line($"  {preset.PadRight(34)} {type.PadRight(9)} {extCount} extensions");
        }
        return ExitCodes.Success;
    }

    private static int Show(ParsedArgs args, CliContext ctx) {
        string preset = RequireName(args, "show");
        var data = Describe(preset);

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true }, { "command", "presets show" }, { "preset", data }
            });
            return ExitCodes.Success;
        }

        ctx.Log.Line("Name:        " + preset);
        ctx.Log.Line("Type:        " + data["type"]);
        ctx.Log.Line("Extensions:  " + data["extensions"]);
        ctx.Log.Line("Ignored:     " + data["ignoredFolders"]);
        ctx.Log.Line("Exclusions:  " + data["exclusions"]);
        return ExitCodes.Success;
    }

    private static int Save(ParsedArgs args, CliContext ctx) {
        string preset = RequireName(args, "save");

        if (PresetManager.Instance.IsPresetBuiltIn(preset))
            throw new CliException($"'{preset}' is a built-in preset and cannot be modified. Save it under a new name.", ExitCodes.NotFound);

        string extensions = PresetManager.Instance.GetExtensionsFor(preset);
        string ignored = PresetManager.Instance.GetIgnoredFoldersFor(preset);
        string exclusions = PresetManager.Instance.GetExclusionsFor(preset);

        string? copyFrom = args.Value("from");
        if (!string.IsNullOrWhiteSpace(copyFrom)) {
            if (!PresetManager.Instance.HasPreset(copyFrom))
                throw CliException.NotFound($"Unknown preset '{copyFrom}'.");
            extensions = PresetManager.Instance.GetExtensionsFor(copyFrom);
            ignored = PresetManager.Instance.GetIgnoredFoldersFor(copyFrom);
            exclusions = PresetManager.Instance.GetExclusionsFor(copyFrom);
        }

        if (args.Has("ext")) extensions = string.Join(",", args.CsvValues("ext"));
        if (args.Has("ignore")) ignored = string.Join(",", args.CsvValues("ignore"));
        if (args.Has("exclude")) exclusions = string.Join(",", args.CsvValues("exclude"));

        PresetManager.Instance.AddOrUpdatePreset(preset, extensions, ignored, exclusions);

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true }, { "command", "presets save" }, { "preset", Describe(preset) }
            });
        }
        else {
            ctx.Log.Success($"Preset '{preset}' saved.");
        }
        return ExitCodes.Success;
    }

    private static int Delete(ParsedArgs args, CliContext ctx) {
        string preset = RequireName(args, "delete");

        if (!PresetManager.Instance.HasPreset(preset))
            throw CliException.NotFound($"Unknown preset '{preset}'.");
        if (PresetManager.Instance.IsPresetBuiltIn(preset))
            throw new CliException($"'{preset}' is a built-in preset and cannot be deleted.", ExitCodes.NotFound);

        if (!ConsoleHost.Confirm($"Delete preset '{preset}'?", ctx.AssumeYes))
            throw CliException.Usage("Aborted by user.");

        PresetManager.Instance.DeletePreset(preset);

        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> { { "ok", true }, { "command", "presets delete" }, { "preset", preset } });
        else
            ctx.Log.Success($"Preset '{preset}' deleted.");
        return ExitCodes.Success;
    }

    private static Dictionary<string, object?> Describe(string preset) => new() {
        { "name", preset },
        { "type", PresetManager.Instance.IsPresetBuiltIn(preset) ? "built-in" : "custom" },
        { "extensions", PresetManager.Instance.GetExtensionsFor(preset) },
        { "ignoredFolders", PresetManager.Instance.GetIgnoredFoldersFor(preset) },
        { "exclusions", PresetManager.Instance.GetExclusionsFor(preset) }
    };

    private static string RequireName(ParsedArgs args, string action) {
        if (args.Positional.Count == 0 || string.IsNullOrWhiteSpace(args.Positional[0]))
            throw CliException.Usage($"Missing preset name. Usage: TxtConverter.exe presets {action} <name>");
        return args.Positional[0];
    }

    private static List<string> SplitList(string value) =>
        CliCommand.SplitCsv(value);
}