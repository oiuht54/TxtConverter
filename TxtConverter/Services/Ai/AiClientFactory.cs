using TxtConverter.Core.Enums;

namespace TxtConverter.Services.Ai;

public static class AiClientFactory {
    public static IAiClient CreateClient() {
        var prefs = PreferenceManager.Instance;
        var provider = prefs.GetAiProvider();
        switch (provider) {
            case AiProvider.OpenAiCompatible:
                return new OpenAiCompatibleClient(
                    prefs.GetCustomOpenAiEndpoint(),
                    prefs.GetCustomOpenAiApiKey(),
                    prefs.GetCustomOpenAiModel(),
                    prefs.GetCustomOpenAiMaxTokens(),
                    prefs.GetCustomOpenAiTemperature(),
                    prefs.GetCustomOpenAiTopP()
                );
            case AiProvider.NvidiaNim:
                return new NvidiaClient(
                    prefs.GetNvidiaApiKey(),
                    prefs.GetNvidiaModel(),
                    prefs.GetNvidiaMaxTokens(),
                    prefs.GetNvidiaTemperature(),
                    prefs.GetNvidiaTopP(),
                    prefs.GetNvidiaReasoningEnabled()
                );
            case AiProvider.GoogleGemini:
            default:
                return new GeminiClient(
                    prefs.GetGeminiApiKey(),
                    prefs.GetGeminiModel(),
                    prefs.GetAiThinkingEnabled(),
                    prefs.GetAiThinkingBudget()
                );
        }
    }

    public static IAiClient CreateSpecific(AiProvider provider, string apiKey, string model, string? endpoint = null) {
        var prefs = PreferenceManager.Instance;
        switch (provider) {
            case AiProvider.OpenAiCompatible:
                string resolvedEndpoint = !string.IsNullOrWhiteSpace(endpoint)
                    ? endpoint
                    : prefs.GetCustomOpenAiEndpoint();
                return new OpenAiCompatibleClient(
                    resolvedEndpoint,
                    apiKey,
                    model,
                    prefs.GetCustomOpenAiMaxTokens(),
                    prefs.GetCustomOpenAiTemperature(),
                    prefs.GetCustomOpenAiTopP()
                );
            case AiProvider.NvidiaNim:
                return new NvidiaClient(
                    apiKey,
                    model,
                    prefs.GetNvidiaMaxTokens(),
                    0.5,
                    0.7,
                    false
                );
            case AiProvider.GoogleGemini:
            default:
                return new GeminiClient(apiKey, model, false, 0);
        }
    }
}