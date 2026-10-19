using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>Shows which preset the project auto detection picks.</summary>
internal sealed class DetectCommand : CliCommand {
    public override string Name => "detect";
    public override string Summary => "Show the auto-detected preset for a project";
    public override string Usage => "detect [path]";

    public override string Description =>
        "Runs the same heuristic as the GUI (project.godot, Assets + ProjectSettings, *.sln, package.json, ...)\n" +
        "and prints the preset name that would be selected.";

    public override IReadOnlyList<OptionDef> Options => Array.Empty<OptionDef>();

    public override Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        EnsureNoExtraArgs(args, 1);
        string root = CliCommand.ResolveRootPath(args, ctx);
        string? detected = PresetManager.Instance.AutoDetectPreset(root);

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "detect" },
                { "root", root },
                { "preset", detected },
                { "detected", !string.IsNullOrEmpty(detected) }
            });
        }
        else if (!string.IsNullOrEmpty(detected)) {
            ctx.Log.Result(detected);
        }
        else {
            ctx.Log.Detail("No preset detected (the GUI would keep the current preset).");
        }

        return Task.FromResult(ExitCodes.Success);
    }
}