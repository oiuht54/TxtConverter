using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TxtConverter.Cli;

/// <summary>Runtime context handed to every command.</summary>
internal sealed class CliContext {
    public ConsoleLog Log { get; init; } = null!;
    public bool Json { get; init; }
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }
    public bool AssumeYes { get; init; }

    public Services.PreferenceManager Prefs => Services.PreferenceManager.Instance;
}

/// <summary>Options accepted by every command.</summary>
internal static class GlobalOptions {
    public const string Category = "Global options";

    public static readonly OptionDef[] All = {
        new() {
            Name = "language", Short = 'l', TakesValue = true, ValueName = "en|ru",
            Description = "Language of the generated report for this run (saved settings stay untouched)",
            Category = Category
        },
        new() {
            Name = "telemetry", TakesValue = true, ValueName = "on|off",
            Description = "Enable or disable anonymous usage statistics for this run",
            Category = Category
        },
        new() {
            Name = "yes", Short = 'y',
            Description = "Answer yes to confirmation prompts",
            Category = Category
        },
        new() {
            Name = "json",
            Description = "Print a single JSON document on stdout instead of text",
            Category = Category
        },
        new() {
            Name = "quiet", Short = 'q',
            Description = "Print results and errors only (no progress output)",
            Category = Category
        },
        new() {
            Name = "verbose", Short = 'v',
            Description = "Print detailed progress and diagnostics on stderr",
            Category = Category
        },
        new() {
            Name = "no-color",
            Description = "Disable colored output",
            Category = Category
        },
        new() {
            Name = "pause", Negatable = true,
            Description = "Wait for a keypress when the command finished, so the console stays open (default: on)",
            Category = Category
        },
        new() {
            Name = "help", Short = 'h',
            Description = "Show help for the command",
            Category = Category
        }
    };
}

/// <summary>Base class for every CLI command.</summary>
internal abstract class CliCommand {
    public abstract string Name { get; }
    public abstract string Summary { get; }
    public abstract string Usage { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyList<OptionDef> Options { get; }

    public OptionSet BuildOptionSet() {
        var set = new OptionSet();
        foreach (OptionDef option in Options) set.Add(option);
        foreach (OptionDef option in GlobalOptions.All) set.Add(option);
        return set;
    }

    public abstract Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx);

    /// <summary>Rejects stray positional arguments with a helpful usage error.</summary>
    protected static void EnsureNoExtraArgs(ParsedArgs args, int allowed) {
        if (args.Positional.Count > allowed)
            throw CliException.Usage($"Unexpected argument '{args.Positional[allowed]}'.");
    }

    /// <summary>
    /// Resolves the project directory: explicit argument, last used folder from the GUI, or the current directory.
    /// </summary>
    public static string ResolveRootPath(ParsedArgs args, CliContext ctx) {
        string? candidate = args.Positional.Count > 0 && !string.IsNullOrWhiteSpace(args.Positional[0])
            ? args.Positional[0]
            : null;

        if (string.IsNullOrWhiteSpace(candidate)) {
            string? lastDir = ctx.Prefs.GetLastSourceDir();
            if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir)) {
                ctx.Log.Trace($"Using last used folder from settings: {lastDir}");
                candidate = lastDir;
            }
            else {
                candidate = Directory.GetCurrentDirectory();
            }
        }

        string full;
        try {
            full = Path.GetFullPath(candidate!);
        }
        catch (Exception ex) {
            throw CliException.Usage($"Invalid path '{candidate}': {ex.Message}");
        }

        if (!Directory.Exists(full))
            throw CliException.NotFound($"Directory not found: {full}");

        return full;
    }

    /// <summary>Splits a comma separated settings string into a trimmed list.</summary>
    public static List<string> SplitCsv(string? csv) {
        if (string.IsNullOrWhiteSpace(csv)) return new List<string>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Select(p => p.Trim())
                   .Where(p => p.Length > 0)
                   .ToList();
    }
}