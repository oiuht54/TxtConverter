using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Cli.Commands;

/// <summary>Reads and writes the settings file that is shared with the GUI.</summary>
internal sealed class ConfigCommand : CliCommand {
    public override string Name => "config";
    public override string Summary => "list | get | set | path of the shared settings";
    public override string Usage => "config <list|get|set|path> [key] [value]";

    public override string Description =>
        "Actions:\n" +
        "  list               Print every setting (secrets are masked)\n" +
        "  get <key>          Print one setting\n" +
        "  set <key> <value>  Change one setting and save it to settings.json\n" +
        "  path               Print the settings file location\n\n" +
        "The CLI and the GUI share the same file, so a change here is visible in the interface too.";

    public override IReadOnlyList<OptionDef> Options => Array.Empty<OptionDef>();

    private sealed record ConfigKeyDef(
        string Key,
        string Description,
        Func<PreferenceManager, string> Get,
        Action<PreferenceManager, string> Set,
        bool Secret = false);

    private static readonly List<ConfigKeyDef> Keys = new() {
        new("language", "Report/UI language (en, ru)",
            p => p.GetLanguage(),
            (p, v) => p.SetLanguage(RequireEnum(v, "en", "ru"))),
        new("telemetry", "Anonymous usage statistics",
            p => Bool(p.GetTelemetryEnabled()),
            (p, v) => p.SetTelemetryEnabled(ParseBool(v))),
        new("global-ignored-folders", "Always ignored folders, comma separated",
            p => p.GetGlobalIgnoredFolders(),
            (p, v) => p.SetGlobalIgnoredFolders(v)),
        new("global-excluded-paths", "Always excluded files/folders (stubs), comma separated",
            p => p.GetGlobalExcludedPaths(),
            (p, v) => p.SetGlobalExcludedPaths(v)),
        new("last-source-dir", "Last converted folder",
            p => p.GetLastSourceDir(),
            (p, v) => p.SetLastSourceDir(v)),
        new("last-preset", "Last used preset",
            p => p.GetLastPreset(),
            (p, v) => {
                if (!PresetManager.Instance.HasPreset(v)) throw CliException.NotFound($"Unknown preset '{v}'.");
                p.SetLastPreset(v);
            }),
        new("ai.enabled", "Enable the AI Smart Select feature",
            p => Bool(p.GetAiEnabled()),
            (p, v) => p.SetAiEnabled(ParseBool(v))),
        new("ai.provider", "AI provider (GoogleGemini, NvidiaNim, OpenAiCompatible)",
            p => p.GetAiProvider().ToString(),
            (p, v) => p.SetAiProvider(ParseEnum<AiProvider>(v))),
        new("ai.api-key", "Gemini API key",
            p => p.GetGeminiApiKey(),
            (p, v) => p.SetGeminiApiKey(v),
            true),
        new("ai.model", "Gemini model",
            p => p.GetGeminiModel(),
            (p, v) => p.SetGeminiModel(v)),
        new("ai.system-prompt", "System prompt used for AI file selection",
            p => p.GetAiSystemPrompt(),
            (p, v) => p.SetAiSystemPrompt(v)),
        new("ai.thinking-enabled", "Enable Gemini thinking mode",
            p => Bool(p.GetAiThinkingEnabled()),
            (p, v) => p.SetAiThinkingEnabled(ParseBool(v))),
        new("ai.thinking-budget", "Gemini thinking token budget",
            p => p.GetAiThinkingBudget().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetAiThinkingBudget(ParseInt(v))),
        new("nvidia.api-key", "NVIDIA NIM API key",
            p => p.GetNvidiaApiKey(),
            (p, v) => p.SetNvidiaApiKey(v),
            true),
        new("nvidia.model", "NVIDIA NIM model",
            p => p.GetNvidiaModel(),
            (p, v) => p.SetNvidiaModel(v)),
        new("nvidia.max-tokens", "NVIDIA NIM max tokens",
            p => p.GetNvidiaMaxTokens().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetNvidiaMaxTokens(ParseInt(v))),
        new("nvidia.temperature", "NVIDIA NIM temperature",
            p => p.GetNvidiaTemperature().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetNvidiaTemperature(ParseDouble(v))),
        new("nvidia.top-p", "NVIDIA NIM top-p",
            p => p.GetNvidiaTopP().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetNvidiaTopP(ParseDouble(v))),
        new("nvidia.reasoning-enabled", "Enable NVIDIA reasoning mode",
            p => Bool(p.GetNvidiaReasoningEnabled()),
            (p, v) => p.SetNvidiaReasoningEnabled(ParseBool(v))),
        new("custom.endpoint", "OpenAI compatible endpoint",
            p => p.GetCustomOpenAiEndpoint(),
            (p, v) => p.SetCustomOpenAiEndpoint(v)),
        new("custom.api-key", "OpenAI compatible API key",
            p => p.GetCustomOpenAiApiKey(),
            (p, v) => p.SetCustomOpenAiApiKey(v),
            true),
        new("custom.model", "OpenAI compatible model",
            p => p.GetCustomOpenAiModel(),
            (p, v) => p.SetCustomOpenAiModel(v)),
        new("custom.max-tokens", "OpenAI compatible max tokens",
            p => p.GetCustomOpenAiMaxTokens().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetCustomOpenAiMaxTokens(ParseInt(v))),
        new("custom.temperature", "OpenAI compatible temperature",
            p => p.GetCustomOpenAiTemperature().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetCustomOpenAiTemperature(ParseDouble(v))),
        new("custom.top-p", "OpenAI compatible top-p",
            p => p.GetCustomOpenAiTopP().ToString(CultureInfo.InvariantCulture),
            (p, v) => p.SetCustomOpenAiTopP(ParseDouble(v)))
    };

    public override Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        string action = args.Positional.Count > 0 ? args.Positional[0].ToLowerInvariant() : "list";
        if (args.Positional.Count > 0) args.Positional.RemoveAt(0);

        return Task.FromResult(action switch {
            "list" => List(ctx),
            "get" => Get(args, ctx),
            "set" => Set(args, ctx),
            "path" => Path(ctx),
            _ => throw CliException.Usage($"Unknown action '{action}'. Allowed: list, get, set, path.")
        });
    }

    private static int List(CliContext ctx) {
        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "config list" },
                { "settingsPath", PreferenceManager.Instance.SettingsPath },
                { "settings", Keys.ToDictionary(k => k.Key, k => Mask(k, k.Get(ctx.Prefs))) }
            });
            return ExitCodes.Success;
        }

        ctx.Log.Line("Settings file: " + PreferenceManager.Instance.SettingsPath);
        ctx.Log.Section($"Settings ({Keys.Count})");
        foreach (ConfigKeyDef key in Keys)
            ctx.Log.Line($"  {key.Key.PadRight(26)} {Compact(Mask(key, key.Get(ctx.Prefs))).PadRight(30)} {key.Description}");
        return ExitCodes.Success;
    }

    /// <summary>Single line, length limited version of a setting value for list output.</summary>
    private static string Compact(string value) {
        string single = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (single.Length <= 30) return single.Length == 0 ? "<empty>" : single;
        return single[..29] + "…";
    }

    private static int Get(ParsedArgs args, CliContext ctx) {
        ConfigKeyDef key = RequireKey(args);
        string value = Mask(key, key.Get(ctx.Prefs));

        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> { { "ok", true }, { "command", "config get" }, { "key", key.Key }, { "value", value } });
        else
            ctx.Log.Result(value);
        return ExitCodes.Success;
    }

    private static int Set(ParsedArgs args, CliContext ctx) {
        ConfigKeyDef key = RequireKey(args);
        if (args.Positional.Count < 2)
            throw CliException.Usage($"Missing value. Usage: TxtConverter.exe config set {key.Key} <value>");
        string value = args.Positional[1];

        key.Set(ctx.Prefs, value);
        ctx.Prefs.Save();

        string stored = Mask(key, key.Get(ctx.Prefs));
        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> { { "ok", true }, { "command", "config set" }, { "key", key.Key }, { "value", stored } });
        else
            ctx.Log.Success($"{key.Key} = {stored}");
        return ExitCodes.Success;
    }

    private static int Path(CliContext ctx) {
        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> { { "ok", true }, { "command", "config path" }, { "settingsPath", PreferenceManager.Instance.SettingsPath } });
        else
            ctx.Log.Result(PreferenceManager.Instance.SettingsPath);
        return ExitCodes.Success;
    }

    private static ConfigKeyDef RequireKey(ParsedArgs args) {
        if (args.Positional.Count == 0 || string.IsNullOrWhiteSpace(args.Positional[0]))
            throw CliException.Usage("Missing settings key. Run 'TxtConverter.exe config list' to see all keys.");
        string name = args.Positional[0];
        return Keys.FirstOrDefault(k => k.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? throw CliException.NotFound($"Unknown settings key '{name}'. Run 'TxtConverter.exe config list' to see all keys.");
    }

    private static string Mask(ConfigKeyDef key, string value) {
        if (!key.Secret) return value;
        if (string.IsNullOrEmpty(value)) return "<not set>";
        return $"*** ({value.Length} chars)";
    }

    private static string Bool(bool value) => value ? "on" : "off";

    private static bool ParseBool(string value) {
        switch (value.Trim().ToLowerInvariant()) {
            case "on" or "true" or "1" or "yes" or "y": return true;
            case "off" or "false" or "0" or "no" or "n" or "": return false;
            default: throw CliException.Usage($"Invalid boolean '{value}'. Allowed: on, off, true, false.");
        }
    }

    private static int ParseInt(string value) {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw CliException.Usage($"Invalid number '{value}'.");
        return parsed;
    }

    private static double ParseDouble(string value) {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            throw CliException.Usage($"Invalid number '{value}'.");
        return parsed;
    }

    private static string RequireEnum(string value, params string[] allowed) {
        if (!allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            throw CliException.Usage($"Invalid value '{value}'. Allowed: {string.Join(", ", allowed)}.");
        return value.Trim().ToLowerInvariant();
    }

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum {
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed)) return parsed;
        throw CliException.Usage($"Invalid value '{value}'. Allowed: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
    }
}