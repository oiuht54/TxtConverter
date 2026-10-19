using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Core.Enums;
using TxtConverter.Services;
using TxtConverter.Services.Ai;

namespace TxtConverter.Cli.Commands;

/// <summary>AI Smart Select plus the provider helpers of the settings window.</summary>
internal sealed class AiCommand : CliCommand {
    public override string Name => "ai";
    public override string Summary => "select | models | test  (AI Smart Select and provider checks)";
    public override string Usage => "ai <select|models|test> [path] [options]";

    public override string Description =>
        "Actions:\n" +
        "  select [path]    Describe a task, let the AI pick the relevant files and print them\n" +
        "  models           List the models offered by the configured provider\n" +
        "  test             Send a test request to verify key, endpoint and model\n\n" +
        "Configure the provider first with 'config set ai.provider ...' / 'config set ai.api-key ...'.";

    public override IReadOnlyList<OptionDef> Options => AiOptions.Common.Concat(new List<OptionDef> {
        new OptionDef { Name = "task", TakesValue = true, ValueName = "text", Description = "What the code should do (required for 'select')" },
        new OptionDef { Name = "model", TakesValue = true, ValueName = "name", Description = "Model to use for this call" },
        new OptionDef { Name = "budget", TakesValue = true, ValueName = "tokens", Description = "Thinking token budget (Gemini)" },
        new OptionDef { Name = "compression", Short = 'c', TakesValue = true, ValueName = "none|smart|maximum", Description = "Compression mode used to pack the project context ('select')" },
        new OptionDef { Name = "system-prompt", TakesValue = true, ValueName = "file", Description = "Read the AI system prompt from a file" },
        new OptionDef { Name = "save", TakesValue = true, ValueName = "file", Description = "Write the selection as a reusable path list" }
    }).Concat(ProjectOptions.Common).ToList();

    public override async Task<int> ExecuteAsync(ParsedArgs args, CliContext ctx) {
        string action = args.Positional.Count > 0 ? args.Positional[0].ToLowerInvariant() : "select";
        if (args.Positional.Count > 0) args.Positional.RemoveAt(0);
        EnsureNoExtraArgs(args, action == "select" ? 1 : 0);

        return action switch {
            "select" => await SelectAsync(args, ctx),
            "models" => await ModelsAsync(args, ctx),
            "test" => await TestAsync(args, ctx),
            _ => throw CliException.Usage($"Unknown action '{action}'. Allowed: select, models, test.")
        };
    }

    private static async Task<int> SelectAsync(ParsedArgs args, CliContext ctx) {
        string? task = args.Value("task");
        if (string.IsNullOrWhiteSpace(task))
            throw CliException.Usage("Missing task description. Usage: TxtConverter.exe ai select [path] --task \"what the code should do\"");

        var project = await ProjectResolver.ResolveAsync(args, ctx);
        if (project.FoundFiles.Count == 0)
            throw new CliException(
                $"No files matched preset '{project.PresetName}'. Use --ext to override the extension list.",
                ExitCodes.NothingToConvert);

        var outcome = await AiSelectionService.RunAsync(project, args, ctx, task);

        string? saveTo = args.Value("save");
        if (!string.IsNullOrWhiteSpace(saveTo)) {
            var lines = outcome.Files.Select(project.Relative).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllLines(saveTo, lines);
            ctx.Log.Detail($"Selection saved to {saveTo} (usable with: convert --include-file {saveTo})");
        }

        if (ctx.Json) {
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true },
                { "command", "ai select" },
                { "root", project.RootPath },
                { "provider", outcome.ProviderName },
                { "model", outcome.Model },
                { "compression", outcome.Compression },
                { "contextLength", outcome.ContextLength },
                { "filesScanned", project.FoundFiles.Count },
                { "filesSelected", outcome.Files.Count },
                { "savedTo", saveTo ?? "" },
                { "files", outcome.Files.Select(project.Relative).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList() }
            });
        }
        else {
            ctx.Log.Section($"AI selected {outcome.Files.Count} of {project.FoundFiles.Count} files");
            ctx.Log.Detail($"Context packed with '{outcome.Compression}' compression: {outcome.ContextLength} characters.");
            foreach (string file in outcome.Files.OrderBy(project.Relative, StringComparer.OrdinalIgnoreCase))
                ctx.Log.Line("  " + project.Relative(file));
        }

        TelemetryService.Instance.TrackEvent("ai_used", new Dictionary<string, object> {
            { "surface", "cli" },
            { "files_selected", outcome.Files.Count },
            { "total_files_in_project", project.FoundFiles.Count },
            { "provider", outcome.ProviderName }
        });

        return ExitCodes.Success;
    }

    private static async Task<int> ModelsAsync(ParsedArgs args, CliContext ctx) {
        var (client, providerName, _requestedModel) = CreateClient(args, ctx);

        List<string> models;
        try {
            models = await client.GetAvailableModelsAsync();
        }
        catch (Exception ex) {
            throw CliException.Api($"Could not fetch models: {ex.Message}", ex);
        }

        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true }, { "command", "ai models" }, { "provider", providerName }, { "count", models.Count }, { "models", models }
            });
        else {
            ctx.Log.Section($"{models.Count} models from {providerName}");
            foreach (string modelName in models) ctx.Log.Line("  " + modelName);
        }

        if (models.Count == 0) {
            ctx.Log.Error("The provider returned no models. Check the API key, endpoint and model configuration.");
            return ExitCodes.ApiError;
        }
        return ExitCodes.Success;
    }

    private static async Task<int> TestAsync(ParsedArgs args, CliContext ctx) {
        var (client, providerName, model) = CreateClient(args, ctx);

        string response;
        try {
            response = await client.TestConnectionAsync(model);
        }
        catch (Exception ex) {
            throw CliException.Api($"Connection test failed: {ex.Message}", ex);
        }

        if (ctx.Json)
            ctx.Log.EmitJson(new Dictionary<string, object?> {
                { "ok", true }, { "command", "ai test" }, { "provider", providerName }, { "model", model }, { "response", response }
            });
        else
            ctx.Log.Section($"Connection to {providerName} succeeded");
        ctx.Log.Line(response);
        return ExitCodes.Success;
    }

    private static (IAiClient client, string providerName, string model) CreateClient(ParsedArgs args, CliContext ctx) {
        var prefs = ctx.Prefs;
        AiProvider provider = prefs.GetAiProvider();
        string? overrideProvider = args.Value("provider");
        if (!string.IsNullOrWhiteSpace(overrideProvider)) provider = AiOptions.ParseProvider(overrideProvider);

        string apiKey = args.Value("api-key") ?? provider switch {
            AiProvider.NvidiaNim => prefs.GetNvidiaApiKey(),
            AiProvider.OpenAiCompatible => prefs.GetCustomOpenAiApiKey(),
            _ => prefs.GetGeminiApiKey()
        };

        if (string.IsNullOrWhiteSpace(apiKey) && provider != AiProvider.OpenAiCompatible)
            throw CliException.NotFound($"No API key configured for {provider}. Set it with: config set {KeyFor(provider, "api-key")} <key>");

        string model = args.Value("model") ?? provider switch {
            AiProvider.NvidiaNim => prefs.GetNvidiaModel(),
            AiProvider.OpenAiCompatible => prefs.GetCustomOpenAiModel(),
            _ => prefs.GetGeminiModel()
        };
        if (string.IsNullOrWhiteSpace(model))
            throw CliException.NotFound("No model configured. Pass --model or store one in the settings.");

        string? endpoint = args.Value("endpoint") ?? (provider == AiProvider.OpenAiCompatible ? prefs.GetCustomOpenAiEndpoint() : null);

        return (AiClientFactory.CreateSpecific(provider, apiKey, model, endpoint), provider.ToString(), model);
    }

    private static string KeyFor(AiProvider provider, string suffix) => provider switch {
        AiProvider.NvidiaNim => "nvidia." + suffix,
        AiProvider.OpenAiCompatible => "custom." + suffix,
        _ => "ai." + suffix
    };
}