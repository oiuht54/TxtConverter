using TxtConverter.Core.Enums;

namespace TxtConverter.Services.Ai;

public static class AiClientFactory {
    public static IAiClient CreateClient() {
        var prefs = PreferenceManager.Instance;
        var provider = prefs.GetAiProvider();

        switch (provider) {
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

    public static IAiClient CreateSpecific(AiProvider provider, string apiKey, string model) {
        var prefs = PreferenceManager.Instance;
        switch (provider) {
            case AiProvider.NvidiaNim:
                // Для теста настроек используем текущие сохраненные параметры
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