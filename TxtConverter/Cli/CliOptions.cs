using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TxtConverter.Cli;

/// <summary>Declaration of a single command line option. Also drives the generated help text.</summary>
internal sealed class OptionDef {
    public string Name { get; init; } = "";
    public char? Short { get; init; }
    public bool TakesValue { get; init; }
    public bool Repeatable { get; init; }
    public string ValueName { get; init; } = "value";
    public string Description { get; init; } = "";
    public string DefaultText { get; init; } = "";
    public string Category { get; init; } = "Options";
    public bool Required { get; init; }
    public bool Negatable { get; init; }

    /// <summary>Flags can be disabled with the --no- prefix.</summary>
    public string NegatedName => "no-" + Name;

    public string Display => Short.HasValue ? $"-{Short.Value}, --{Name}" : $"--{Name}";

    /// <summary>Left hand side of the help line (without padding).</summary>
    public string HelpLeft {
        get {
            string left = TakesValue ? $"{Display} <{ValueName}>" : Display;
            if (Repeatable) left += " (repeatable)";
            if (Negatable) left += $" / --no-{Name}";
            if (Required) left += " (required)";
            return left;
        }
    }

    public string HelpRight {
        get {
            string right = Description;
            if (!string.IsNullOrEmpty(DefaultText)) right += $" [default: {DefaultText}]";
            return right;
        }
    }
}

/// <summary>Registry of the options accepted by one command.</summary>
internal sealed class OptionSet {
    private readonly Dictionary<string, OptionDef> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OptionDef> _ordered = new();

    public IReadOnlyList<OptionDef> All => _ordered;

    public OptionSet Add(OptionDef def) {
        _ordered.Add(def);
        _byName[def.Name] = def;
        if (def.Short.HasValue) _byName["-" + def.Short.Value] = def;
        return this;
    }

    public OptionSet AddValue(string name, string valueName, string description, char? shortName = null, bool repeatable = false, string? defaultText = null, bool required = false) =>
        Add(new OptionDef {
            Name = name, Short = shortName, TakesValue = true, ValueName = valueName,
            Description = description, Repeatable = repeatable, DefaultText = defaultText ?? "", Required = required
        });

    public OptionSet AddFlag(string name, string description, char? shortName = null, string? defaultText = null) =>
        Add(new OptionDef {
            Name = name, Short = shortName, TakesValue = false,
            Description = description, DefaultText = defaultText ?? ""
        });

    public bool TryGet(string token, out OptionDef def) => _byName.TryGetValue(token, out def!);

    public IEnumerable<OptionDef> ByCategory(string category) => _ordered.Where(o => o.Category == category);
}

/// <summary>Result of parsing the raw argument vector against an <see cref="OptionSet"/>.</summary>
internal sealed class ParsedArgs {
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = new();

    internal void AddValue(string name, string value) {
        if (!_values.TryGetValue(name, out var list)) {
            list = new List<string>();
            _values[name] = list;
        }
        list.Add(value);
    }

    internal void ClearValue(string name) => _values.Remove(name);

    internal void SetFlag(string name, bool value) => _flags[name] = value;

    public bool Has(string name) => _values.ContainsKey(name) || _flags.ContainsKey(name);

    public string? Value(string name) {
        if (_values.TryGetValue(name, out var list) && list.Count > 0) return list[list.Count - 1];
        return null;
    }

    public IReadOnlyList<string> Values(string name) =>
        _values.TryGetValue(name, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>All values of a repeatable option, additionally split on commas.</summary>
    public List<string> CsvValues(string name) {
        var result = new List<string>();
        foreach (var raw in Values(name)) {
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
        }
        return result;
    }

    /// <summary>Boolean switch: true when --flag, false when --no-flag, null when not provided.</summary>
    public bool? Flag(string name) => _flags.TryGetValue(name, out bool value) ? value : null;

    public string RequireValue(string name) {
        string? value = Value(name);
        if (string.IsNullOrWhiteSpace(value))
            throw CliException.Usage($"Option --{name} requires a value.");
        return value;
    }

    public TEnum? EnumValue<TEnum>(string name) where TEnum : struct, Enum {
        string? raw = Value(name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Enum.TryParse<TEnum>(raw, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        throw CliException.Usage($"Invalid value '{raw}' for --{name}. Allowed: {string.Join(", ", Enum.GetNames(typeof(TEnum)).Select(n => n.ToLowerInvariant()))}.");
    }

    public int? IntValue(string name) {
        string? raw = Value(name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw CliException.Usage($"Invalid number '{raw}' for --{name}.");
        return value;
    }
}

/// <summary>Minimal dependency free command line parser.</summary>
internal static class CommandLineParser {
    public static ParsedArgs Parse(IReadOnlyList<string> args, OptionSet options) {
        var parsed = new ParsedArgs();

        for (int i = 0; i < args.Count; i++) {
            string token = args[i];

            if (token == "--") {
                for (int j = i + 1; j < args.Count; j++) parsed.Positional.Add(args[j]);
                break;
            }

            if (token.StartsWith("--", StringComparison.Ordinal)) {
                string body = token.Substring(2);
                string? inlineValue = null;
                int eq = body.IndexOf('=');
                if (eq >= 0) {
                    inlineValue = body.Substring(eq + 1);
                    body = body.Substring(0, eq);
                }

                if (!options.TryGet(body, out OptionDef def)) {
                    if (TryGetNegatedFlag(body, options, out OptionDef negated)) {
                        if (inlineValue != null)
                            throw CliException.Usage($"Option --{body} does not take a value.");
                        parsed.SetFlag(negated.Name, false);
                        continue;
                    }
                    throw CliException.Usage($"Unknown option '--{body}'. Run with --help to see available options.");
                }

                if (def.TakesValue) {
                    string value = inlineValue ?? NextValue(args, ref i, def);
                    if (!def.Repeatable) parsed.ClearValue(def.Name);
                    parsed.AddValue(def.Name, value);
                }
                else {
                    if (inlineValue != null)
                        throw CliException.Usage($"Option --{body} does not take a value.");
                    parsed.SetFlag(def.Name, true);
                }
                continue;
            }

            if (token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1])) {
                string body = token.Substring(1);
                string? inlineValue = null;
                int eq = body.IndexOf('=');
                if (eq >= 0) {
                    inlineValue = body.Substring(eq + 1);
                    body = body.Substring(0, eq);
                }

                if (!options.TryGet("-" + body, out OptionDef def))
                    throw CliException.Usage($"Unknown option '-{body}'. Run with --help to see available options.");

                if (def.TakesValue) {
                    string value = inlineValue ?? NextValue(args, ref i, def);
                    parsed.AddValue(def.Name, value);
                }
                else {
                    if (inlineValue != null)
                        throw CliException.Usage($"Option -{body} does not take a value.");
                    parsed.SetFlag(def.Name, true);
                }
                continue;
            }

            parsed.Positional.Add(token);
        }

        return parsed;
    }

    private static bool TryGetNegatedFlag(string body, OptionSet options, out OptionDef def) {
        if (body.StartsWith("no-", StringComparison.OrdinalIgnoreCase)
            && options.TryGet(body.Substring(3), out def) && !def.TakesValue) {
            return true;
        }
        def = null!;
        return false;
    }

    private static string NextValue(IReadOnlyList<string> args, ref int i, OptionDef def) {
        if (i + 1 >= args.Count)
            throw CliException.Usage($"Option --{def.Name} requires a value.");
        return args[++i];
    }
}