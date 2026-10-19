using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services;
using TxtConverter.Services.Ai;

namespace TxtConverter.Cli;

/// <summary>Result of an AI Smart Select run.</summary>
internal sealed class AiSelectionOutcome {
    public List<string> Files { get; init; } = new();
    public string ProviderName { get; init; } = "";
    public string Model { get; init; } = "";
    public string RawResponse { get; init; } = "";
    public string RequestJson { get; init; } = "";
    /// <summary>Compression mode that was used to pack the context.</summary>
    public CompressionLevel Compression { get; init; }
    /// <summary>Size of the packed context in characters.</summary>
    public int ContextLength { get; init; }
}

/// <summary>
/// Shared implementation of the "AI Smart Select" feature for the CLI
/// (identical pipeline to the GUI AiTaskWindow: context packing -> API call -> path matching).
/// </summary>
internal static class AiSelectionService {
    public static async Task<AiSelectionOutcome> RunAsync(ResolvedProject project, ParsedArgs args, CliContext ctx, string task) {
        var prefs = ctx.Prefs;

        if (!prefs.GetAiEnabled())
            ctx.Log.Warn("AI selection is disabled in the settings. Enable it with: config set ai.enabled true");

        string model = args.Value("ai-model") ?? prefs.GetAiModel();
        int budget = args.IntValue("ai-budget") ?? prefs.GetAiThinkingBudget();
        string systemPrompt = ResolveSystemPrompt(args, prefs);

        // Compression mode used to pack the context that is sent to the model.
        var compression = args.EnumValue<CompressionLevel>("compression") ?? prefs.GetCompressionLevel();

        string providerName = prefs.GetAiProvider().ToString();
        ctx.Log.Detail($"Building project context from {project.FoundFiles.Count} files ({compression} compression)...");

        var contextBuilder = new ContextBuilder(project.RootPath, project.FoundFiles, compression);
        string projectContext = await contextBuilder.BuildContextAsync(new DirectProgress<string>(s => ctx.Log.Trace(s)));

        ctx.Log.Detail($"Sending request to {providerName} (model: {model})...");

        AiAnalysisResult result;
        try {
            IAiClient client = AiClientFactory.CreateClient();
            result = await client.AnalyzeProjectAsync(task, projectContext, model, budget, systemPrompt);
        }
        catch (Exception ex) {
            throw CliException.Api($"AI request failed: {ex.Message}", ex);
        }

        ctx.Log.Trace($"Provider response ({result.ProviderName}):");
        ctx.Log.Trace(result.RawContentText);

        var matched = AiFileMatcher.Match(project.FoundFiles, project.RootPath, result.SelectedFiles);
        if (matched.Count == 0)
            throw CliException.Api(result.SelectedFiles.Count == 0
                ? "The AI returned an empty file selection."
                : "The AI returned paths that could not be matched to the scanned project files. Re-run with --verbose to see the raw response.");

        return new AiSelectionOutcome {
            Files = matched,
            ProviderName = string.IsNullOrWhiteSpace(result.ProviderName) ? providerName : result.ProviderName,
            Model = model,
            RawResponse = result.RawResponseJson,
            RequestJson = result.RequestJson,
            Compression = compression,
            ContextLength = projectContext.Length
        };
    }

    private static string ResolveSystemPrompt(ParsedArgs args, PreferenceManager prefs) {
        string? promptFile = args.Value("ai-system-prompt");
        if (!string.IsNullOrWhiteSpace(promptFile)) {
            if (!File.Exists(promptFile))
                throw CliException.NotFound($"System prompt file not found: {promptFile}");
            return File.ReadAllText(promptFile).Trim();
        }

        string prompt = prefs.GetAiSystemPrompt();
        return string.IsNullOrWhiteSpace(prompt) ? Core.ProjectConstants.DefaultAiSystemPrompt : prompt;
    }

    /// <summary>IProgress implementation that reports synchronously (no SynchronizationContext in the CLI).</summary>
    private sealed class DirectProgress<T> : IProgress<T> {
        private readonly Action<T> _handler;
        public DirectProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}