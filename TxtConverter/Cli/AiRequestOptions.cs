using System;
using System.IO;
using TxtConverter.Core;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Cli;

/// <summary>
/// Turns the AI options of a command into one concrete request configuration.
/// Both 'ai select' and 'convert --ai-task' use this class, so the documented flags
/// can never be accepted and then silently ignored again.
/// </summary>
internal sealed class AiRequestOptions {
    public AiProvider Provider { get; init; } = AiProvider.GoogleGemini;
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public string? Endpoint { get; init; }
    public bool ThinkingEnabled { get; init; }
    public int ThinkingBudget { get; init; }
    public string SystemPrompt { get; init; } = "";
    public CompressionLevel Compression { get; init; }

    public string ProviderName => Provider switch {
        AiProvider.NvidiaNim => "NVIDIA NIM",
        AiProvider.OpenAiCompatible => "OpenAI Compatible",
        _ => "Google Gemini"
    };

    /// <summary>
    /// Resolves the per-call overrides over the saved settings. The option names of both
    /// commands are accepted ('--model' / '--ai-model', '--budget' / '--ai-budget', ...).
    /// Credentials are not validated here; the caller reports a missing key with the
    /// provider specific config key.
    /// </summary>
    public static AiRequestOptions Resolve(ParsedArgs args, PreferenceManager prefs) {
        AiProvider provider = prefs.GetAiProvider();
        string? providerOverride = args.Value("provider");
        if (!string.IsNullOrWhiteSpace(providerOverride))
            provider = AiOptions.ParseProvider(providerOverride);

        string apiKey = args.Value("api-key") ?? provider switch {
            AiProvider.NvidiaNim => prefs.GetNvidiaApiKey(),
            AiProvider.OpenAiCompatible => prefs.GetCustomOpenAiApiKey(),
            _ => prefs.GetGeminiApiKey()
        };

        string model = FirstNonEmpty(
            args.Value("model"),
            args.Value("ai-model"),
            provider switch {
                AiProvider.NvidiaNim => prefs.GetNvidiaModel(),
                AiProvider.OpenAiCompatible => prefs.GetCustomOpenAiModel(),
                _ => prefs.GetGeminiModel()
            },
            provider switch {
                AiProvider.NvidiaNim => ProjectConstants.DefaultNvidiaModel,
                AiProvider.OpenAiCompatible => ProjectConstants.DefaultCustomOpenAiModel,
                _ => ProjectConstants.DefaultGeminiModel
            });

        string? endpointOverride = args.Value("endpoint");
        if (!string.IsNullOrWhiteSpace(endpointOverride) && provider != AiProvider.OpenAiCompatible)
            throw CliException.Usage(
                $"--endpoint is only supported for the OpenAI compatible provider (selected: {provider}).");

        string? endpoint = provider == AiProvider.OpenAiCompatible
            ? FirstNonEmpty(endpointOverride, prefs.GetCustomOpenAiEndpoint())
            : null;

        bool budgetProvided = args.Has("budget") || args.Has("ai-budget");
        if (budgetProvided && provider != AiProvider.GoogleGemini)
            throw CliException.Usage(
                $"--budget/--ai-budget is only supported for the Google Gemini provider (selected: {provider}).");

        int budget = args.IntValue("budget") ?? args.IntValue("ai-budget") ?? prefs.GetAiThinkingBudget();
        if (budget < 0)
            throw CliException.Usage($"The AI thinking budget cannot be negative ({budget}).");

        return new AiRequestOptions {
            Provider = provider,
            ApiKey = (apiKey ?? "").Trim(),
            Model = model,
            Endpoint = endpoint,
            ThinkingEnabled = provider == AiProvider.GoogleGemini && prefs.GetAiThinkingEnabled(),
            ThinkingBudget = budget,
            SystemPrompt = ResolveSystemPrompt(args, prefs),
            Compression = args.EnumValue<CompressionLevel>("compression") ?? prefs.GetCompressionLevel()
        };
    }

    /// <summary>Reads '--system-prompt' (ai select) or '--ai-system-prompt' (convert), otherwise the saved prompt.</summary>
    private static string ResolveSystemPrompt(ParsedArgs args, PreferenceManager prefs) {
        string? promptFile = FirstNonEmpty(args.Value("system-prompt"), args.Value("ai-system-prompt"));
        if (!string.IsNullOrWhiteSpace(promptFile)) {
            if (!File.Exists(promptFile))
                throw CliException.NotFound($"System prompt file not found: {promptFile}");

            string contents = File.ReadAllText(promptFile).Trim();
            if (contents.Length == 0)
                throw CliException.Usage($"System prompt file is empty: {promptFile}");
            return contents;
        }

        string prompt = prefs.GetAiSystemPrompt();
        return string.IsNullOrWhiteSpace(prompt) ? ProjectConstants.DefaultAiSystemPrompt : prompt;
    }

    private static string FirstNonEmpty(params string?[] values) {
        foreach (string? value in values) {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }
}
