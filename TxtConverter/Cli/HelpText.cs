using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TxtConverter.Cli;

/// <summary>Help and version output.</summary>
internal static class HelpText {
    public static string VersionString => Core.ProjectConstants.CurrentVersion;

    public static void PrintVersion(ConsoleLog log) {
        log.Result($"TxtConverter CLI {VersionString}");
        log.Result($"Runtime:    {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier ?? "portable"})");
        log.Result($"Repository: {Core.ProjectConstants.GitHubUrl}");
    }

    public static void PrintGeneral(ConsoleLog log) {
        log.Line("TxtConverter " + VersionString + " - command line interface");
        log.Line("");
        log.Line("Usage:");
        log.Line("  TxtConverter.exe <command> [options]");
        log.Line("  TxtConverter.exe            launches the graphical interface");
        log.Line("");
        log.Section("Commands:");

        var entries = CliLauncher.Commands
            .Select(c => (Usage: c.Usage, Summary: c.Summary))
            .ToList();
        entries.Add(("gui", "Start the graphical interface explicitly"));
        entries.Add(("help [command]", "Show help (also --help on every command)"));
        entries.Add(("version", "Show version information (also --version)"));

        int width = entries.Max(e => e.Usage.Length);
        foreach (var entry in entries)
            log.Line("  " + entry.Usage.PadRight(width) + "  " + entry.Summary);
        log.Line("");
        log.Section("Global options:");
        WriteOptions(GlobalOptions.All, log);
        log.Line("");
        log.Section("Examples:");
        log.Line("  TxtConverter.exe convert \"C:\\Projects\\MyGame\" --preset \"Godot Engine\"");
        log.Line("  TxtConverter.exe scan . --stats");
        log.Line("  TxtConverter.exe ai select . --task \"player movement\" --save selection.txt");
        log.Line("  TxtConverter.exe convert . --include \"src/**\" --compression maximum --dry-run");
        log.Line("  TxtConverter.exe test          " + "verify this installation");
        log.Line("");
        log.Line("Run 'TxtConverter.exe help <command>' for command specific options.");
    }

    private static void WriteOptions(IEnumerable<OptionDef> options, ConsoleLog log) {
        var list = options.ToList();
        if (list.Count == 0) return;
        int width = Math.Min(46, list.Max(o => o.HelpLeft.Length));
        foreach (OptionDef option in list)
            log.Line("  " + option.HelpLeft.PadRight(width) + "  " + option.HelpRight);
    }

    public static void PrintCommand(CliCommand command, OptionSet options, ConsoleLog log) {
        log.Line($"TxtConverter {VersionString} - {command.Name}");
        log.Line("");
        log.Line("Usage:");
        log.Line("  " + command.Usage);
        if (!string.IsNullOrWhiteSpace(command.Description)) {
            log.Line("");
            log.Line(command.Description);
        }

        var commandOptions = options.All.Where(o => o.Category != GlobalOptions.Category).ToList();
        if (commandOptions.Count > 0) {
            log.Line("");
            log.Section("Options:");
            WriteOptions(commandOptions, log);
        }

        log.Line("");
        log.Section("Global options:");
        WriteOptions(GlobalOptions.All, log);
    }
}