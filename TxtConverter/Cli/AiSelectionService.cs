using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic;
using TxtConverter.Services.Ai;

namespace TxtConverter.Cli;

/// <summary>Result of an AI Smart Select run.</summary>
internal sealed class AiSelectionOutcome {
    public List<string> Files { get; init; } = new();
    public string ProviderName { get; init; } = "";
    public string Model { get; init; } = "";
    public string RawResponse { get; init; } = "";
    public string RequestJson { get; init; } = "";
    public string ResponseText { get; init; } = "";
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
    public static async Task<AiSelectionOutcome> RunAsync(
        ResolvedProject project,
        ParsedArgs args,
        CliContext ctx,
        string task,
        AiRequestOptions? options = null) {
        var prefs = ctx.Prefs;

        if (!prefs.GetAiEnabled())
            ctx.Log.Warn("AI selection is disabled in the settings. Enable it with: config set ai.enabled true");

        options ??= AiRequestOptions.Resolve(args, prefs);
        if (string.IsNullOrWhiteSpace(options.ApiKey) && options.Provider != AiProvider.OpenAiCompatible) {
            throw CliException.NotFound(
                $"No API key configured for {options.Provider}. " +
                $"Set it with: config set {AiOptions.KeyFor(options.Provider, "api-key")} <key> " +
                "(or pass --api-key to 'ai select').");
        }

        // Compression mode used to pack the context that is sent to the model.
        var compression = options.Compression;

        ctx.Log.Detail($"Building project context from {project.FoundFiles.Count} files ({compression} compression)...");
        var contextBuilder = new ContextBuilder(project.RootPath, project.FoundFiles, compression);
        string projectContext = await contextBuilder.BuildContextAsync(new DirectProgress<string>(s => ctx.Log.Trace(s)));

        ctx.Log.Detail($"Sending request to {options.ProviderName} (model: {options.Model})...");

        AiAnalysisResult result;
        try {
            IAiClient client = AiClientFactory.CreateForSelection(
                options.Provider,
                options.ApiKey,
                options.Model,
                options.Endpoint,
                options.ThinkingEnabled,
                options.ThinkingBudget);
            result = await client.AnalyzeProjectAsync(task, projectContext, options.Model, options.ThinkingBudget, options.SystemPrompt);
        }
        catch (Exception ex) {
            throw CliException.Api($"AI request failed: {ex.Message}", ex);
        }

        // The GUI shows the request and the raw API response in its debug tabs. --verbose
        // and --debug print the same information (with the API key masked by the clients).
        if (ctx.Verbose || ctx.Debug) {
            ctx.Log.Debug("=== AI REQUEST ===");
            ctx.Log.Debug(result.RequestJson);
            ctx.Log.Debug("=== AI RESPONSE (text) ===");
            ctx.Log.Debug(result.RawContentText);
            ctx.Log.Debug("=== RAW API JSON RESPONSE ===");
            ctx.Log.Debug(result.RawResponseJson);
        }

        var matched = AiFileMatcher.Match(project.FoundFiles, project.RootPath, result.SelectedFiles);
        if (matched.Count == 0)
            throw CliException.Api(result.SelectedFiles.Count == 0
                ? "The AI returned an empty file selection."
                : "The AI returned paths that could not be matched to the scanned project files. Re-run with --verbose or --debug to see the raw response.");

        return new AiSelectionOutcome {
            Files = matched,
            ProviderName = string.IsNullOrWhiteSpace(result.ProviderName) ? options.ProviderName : result.ProviderName,
            Model = options.Model,
            RawResponse = result.RawResponseJson,
            RequestJson = result.RequestJson,
            ResponseText = result.RawContentText,
            Compression = compression,
            ContextLength = projectContext.Length
        };
    }

    /// <summary>IProgress implementation that reports synchronously (no SynchronizationContext in the CLI).</summary>
    private sealed class DirectProgress<T> : IProgress<T> {
        private readonly Action<T> _handler;
        public DirectProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}