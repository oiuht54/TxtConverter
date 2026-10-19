using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Core;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>GitHub release check (the CLI twin of the "Check for updates" button).</summary>
internal sealed class UpdateCommand : CliCommand {
    public override string Name => "update";
    public override string Summary => "Check GitHub for a newer release";
    public override string Usage => "update check";

    public override string Description =>
        "Queries the GitHub releases API for oiuht54/TxtConverter and prints the latest version.\n" +
        "This is the non-interactive version of the update check in the settings window.";

    public override IReadOnlyList<OptionDef> Options => Array.Empty<OptionDef>();

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        string action = args.Positional.Count > 0 ? args.Positional[0].ToLowerInvariant() : "check";
        if (args.Positional.Count > 0) args.Positional.RemoveAt(0);

        if (action != "check")
            throw CliException.Usage($"Unknown action '{action}'. Allowed: check.");

        ctx.Log.Detail($"Checking {ProjectConstants.GitHubRepo}...");
        UpdateCheckResult result = await UpdateCheckerService.Instance.CheckForUpdatesDetailedAsync();

        if (!result.Success) {
            throw new CliException($"Update check failed: {result.ErrorMessage}", ExitCodes.Failure);
        }

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "update check" },
                { "currentVersion", ProjectConstants.CurrentVersion },
                { "updateAvailable", result.IsUpdateAvailable },
                { "latestVersion", result.Release?.TagName ?? ProjectConstants.CurrentVersion },
                { "url", result.Release?.HtmlUrl ?? ProjectConstants.GitHubUrl + "/releases" },
                { "notes", result.Release?.Body ?? "" }
            });
        }
        else if (result.IsUpdateAvailable && result.Release != null) {
            ctx.Log.Section($"Update available: {result.Release.TagName} (current: {ProjectConstants.CurrentVersion})");
            ctx.Log.Line(result.Release.HtmlUrl);
            if (!string.IsNullOrWhiteSpace(result.Release.Body))
                ctx.Log.Line("");
            if (!string.IsNullOrWhiteSpace(result.Release.Body))
                foreach (string line in result.Release.Body.Split('\n').Take(20))
                    ctx.Log.Line("  " + line.TrimEnd('\r'));
        }
        else {
            ctx.Log.Result($"You have the latest version ({ProjectConstants.CurrentVersion}).");
        }

        return ExitCodes.Success;
    }
}