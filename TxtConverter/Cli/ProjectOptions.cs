using System;
using System.Collections.Generic;

namespace TxtConverter.Cli;

/// <summary>Options shared by every command that works on a project folder.</summary>
internal static class ProjectOptions {
    public static readonly OptionDef[] Common = {
        new OptionDef {
            Name = "preset", Short = 'p', TakesValue = true, ValueName = "name",
            Description = "Preset to use (default: auto-detected, otherwise last used)"
        },
        new OptionDef {
            Name = "ext", TakesValue = true, ValueName = "list", Repeatable = true,
            Description = "File extensions, comma separated (replaces the preset list)"
        },
        new OptionDef {
            Name = "ignore", TakesValue = true, ValueName = "list", Repeatable = true,
            Description = "Extra ignored folders, comma separated (added to the preset list)"
        },
        new OptionDef {
            Name = "exclude", TakesValue = true, ValueName = "list", Repeatable = true,
            Description = "Files/paths that are kept as stubs only (added to the preset list)"
        }
    };

    public static List<OptionDef> Build(params OptionDef[] extra) {
        var list = new List<OptionDef>(Common);
        list.AddRange(extra);
        return list;
    }

    /// <summary>Options that control which files end up in the report with full content.</summary>
    public static readonly OptionDef[] Selection = {
        new OptionDef {
            Name = "select", TakesValue = true, ValueName = "all|none",
            Description = "Start from every scanned file or from none"
        },
        new OptionDef {
            Name = "include", Short = 'i', TakesValue = true, ValueName = "glob", Repeatable = true,
            Description = "Merge files matching this pattern in full (e.g. \"src/**\")"
        },
        new OptionDef {
            Name = "include-file", TakesValue = true, ValueName = "list",
            Description = "Text file with paths/globs to merge in full"
        },
        new OptionDef {
            Name = "exclude-file", TakesValue = true, ValueName = "list",
            Description = "Text file with paths/globs to keep as stubs"
        }
    };

    public static List<OptionDef> BuildWithSelection(params OptionDef[] extra) {
        var list = new List<OptionDef>(Common);
        list.AddRange(Selection);
        list.AddRange(extra);
        return list;
    }
}

/// <summary>Options shared by the AI commands.</summary>
internal static class AiOptions {
    public static readonly OptionDef[] Common = {
        new OptionDef {
            Name = "provider", TakesValue = true, ValueName = "gemini|nvidia|openai",
            Description = "Override the configured AI provider"
        },
        new OptionDef {
            Name = "api-key", TakesValue = true, ValueName = "key",
            Description = "Override the stored API key for this call"
        },
        new OptionDef {
            Name = "endpoint", TakesValue = true, ValueName = "url",
            Description = "Override the API endpoint (OpenAI compatible only)"
        }
    };

    /// <summary>Maps a user friendly provider name onto the settings enum.</summary>
    public static Core.Enums.AiProvider ParseProvider(string raw) {
        return raw.ToLowerInvariant() switch {
            "gemini" or "google" or "googlegemini" => Core.Enums.AiProvider.GoogleGemini,
            "nvidia" or "nim" or "nvidianim" => Core.Enums.AiProvider.NvidiaNim,
            "openai" or "custom" or "openaicompatible" => Core.Enums.AiProvider.OpenAiCompatible,
            _ => throw CliException.Usage($"Invalid provider '{raw}'. Allowed: gemini, nvidia, openai.")
        };
    }

    /// <summary>Config key that stores a value of the given provider, e.g. nvidia.api-key.</summary>
    public static string KeyFor(Core.Enums.AiProvider provider, string suffix) => provider switch {
        Core.Enums.AiProvider.NvidiaNim => "nvidia." + suffix,
        Core.Enums.AiProvider.OpenAiCompatible => "custom." + suffix,
        _ => "ai." + suffix
    };
}