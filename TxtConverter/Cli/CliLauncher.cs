using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuestPDF.Infrastructure;
using TxtConverter.Cli.Commands;
using TxtConverter.Core;
using TxtConverter.Services;

namespace TxtConverter.Cli;

/// <summary>
/// Entry point of the command line interface. The same executable is used for the GUI,
/// so any argument switches the process into CLI mode.
/// </summary>
public static class CliLauncher {
    internal static readonly List<CliCommand> Commands = new() {
        new ConvertCommand(),
        new ScanCommand(),
        new DetectCommand(),
        new PresetsCommand(),
        new ConfigCommand(),
        new AiCommand(),
        new UpdateCommand()
    };

    /// <summary>No arguments (or an explicit "gui") keeps the classic windowed behaviour.</summary>
    public static bool IsCliInvocation(string[] args) {
        if (args.Length == 0) return false;
        if (args.Length == 1 && (args[0] == "gui" || args[0] == "--gui")) return false;
        return true;
    }

    public static async Task<int> RunAsync(string[] args) {
        ConsoleHost.Initialize();

        bool colorAllowed = Environment.GetEnvironmentVariable("NO_COLOR") == null
                            && !args.Any(a => a.Equals("--no-color", StringComparison.OrdinalIgnoreCase));
        var log = new ConsoleLog { UseColor = colorAllowed };

        try {
            if (args.Length == 0) {
                HelpText.PrintGeneral(log);
                return ExitCodes.UsageError;
            }

            string verb = args[0];
            List<string> rest = args.Skip(1).ToList();

            if (verb is "--version" or "-V" or "version") {
                HelpText.PrintVersion(log);
                return ExitCodes.Success;
            }

            if (verb is "--help" or "-h" or "help") {
                PrintHelp(rest);
                return ExitCodes.Success;
            }

            CliCommand? command = Commands.FirstOrDefault(c => c.Name.Equals(verb, StringComparison.OrdinalIgnoreCase));
            if (command == null) {
                log.Error($"Unknown command '{verb}'.");
                HelpText.PrintGeneral(log);
                return ExitCodes.UsageError;
            }

            OptionSet options = command.BuildOptionSet();
            ParsedArgs parsed = CommandLineParser.Parse(rest, options);

            if (parsed.Flag("help") == true) {
                HelpText.PrintCommand(command, options, log);
                return ExitCodes.Success;
            }

            ConfigureLog(parsed, log);
            var ctx = new CliContext {
                Log = log,
                Json = parsed.Flag("json") == true,
                Quiet = parsed.Flag("quiet") == true,
                Verbose = parsed.Flag("verbose") == true,
                AssumeYes = parsed.Flag("yes") == true
            };

            InitializeHeadless(parsed, ctx);

            return await command.ExecuteAsync(parsed, ctx);
        }
        catch (CliException ex) {
            if (log.JsonMode) log.EmitJson(new Dictionary<string, object?> { { "ok", false }, { "error", ex.Message }, { "exitCode", ex.ExitCode } });
            log.Error(ex.Message);
            if (log.Verbose) log.Trace(ex.ToString());
            return ex.ExitCode;
        }
        catch (Exception ex) {
            if (log.JsonMode) log.EmitJson(new Dictionary<string, object?> { { "ok", false }, { "error", ex.Message }, { "exitCode", ExitCodes.Failure } });
            log.Error(ex.Message);
            if (log.Verbose) log.Trace(ex.ToString());
            return ExitCodes.Failure;
        }
    }

    private static void ConfigureLog(ParsedArgs parsed, ConsoleLog log) {
        log.JsonMode = parsed.Flag("json") == true;
        log.Quiet = parsed.Flag("quiet") == true;
        log.Verbose = parsed.Flag("verbose") == true;
    }

    private static void InitializeHeadless(ParsedArgs parsed, CliContext ctx) {
        QuestPDF.Settings.License = LicenseType.Community;

        string? telemetry = parsed.Value("telemetry");
        if (!string.IsNullOrWhiteSpace(telemetry)) {
            switch (telemetry.ToLowerInvariant()) {
                case "off":
                case "false":
                case "0":
                    TelemetryService.Instance.Suppress = true;
                    break;
                case "on":
                case "true":
                case "1":
                    TelemetryService.Instance.Suppress = false;
                    break;
                default:
                    throw CliException.Usage($"Invalid value '{telemetry}' for --telemetry. Allowed: on, off.");
            }
        }

        PreferenceManager.Instance.Load();

        string language = PreferenceManager.Instance.GetLanguage();
        string? overrideLanguage = parsed.Value("language");
        if (!string.IsNullOrWhiteSpace(overrideLanguage)) {
            if (overrideLanguage is not (ProjectConstants.LangEn or ProjectConstants.LangRu))
                throw CliException.Usage($"Invalid language '{overrideLanguage}'. Allowed: en, ru.");
            language = overrideLanguage;
        }

        // persist: false - the CLI never rewrites the language saved by the GUI.
        LanguageManager.Instance.SetLanguage(language, persist: false);
        ctx.Log.Trace($"Report language: {language}");
    }

    private static void PrintHelp(List<string> rest) {
        if (rest.Count == 0) {
            HelpText.PrintGeneral(new ConsoleLog());
            return;
        }

        CliCommand? command = Commands.FirstOrDefault(c => c.Name.Equals(rest[0], StringComparison.OrdinalIgnoreCase));
        if (command == null) {
            HelpText.PrintGeneral(new ConsoleLog());
            return;
        }

        HelpText.PrintCommand(command, command.BuildOptionSet(), new ConsoleLog());
    }
}