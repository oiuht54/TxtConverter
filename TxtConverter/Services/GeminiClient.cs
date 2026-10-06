using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TxtConverter.Core;
using TxtConverter.Services.Ai;

namespace TxtConverter.Services;

public class GeminiClient : IAiClient {
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly bool _thinkingEnabled;
    private readonly int _defaultTokenBudget;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public GeminiClient(string apiKey, string defaultModel, bool thinkingEnabled, int tokenBudget) {
        _apiKey = apiKey;
        _defaultModel = defaultModel;
        _thinkingEnabled = thinkingEnabled;
        _defaultTokenBudget = tokenBudget;

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);

        _jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public async Task<List<string>> GetAvailableModelsAsync() {
        if (string.IsNullOrWhiteSpace(_apiKey)) return new List<string>();

        string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
        try {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<string>();

            string json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);
            var modelsNode = root?["models"]?.AsArray();

            var result = new List<string>();
            if (modelsNode != null) {
                foreach (var node in modelsNode) {
                    string name = node?["name"]?.ToString() ?? "";
                    if (name.StartsWith("models/")) name = name.Substring(7);

                    var methods = node?["supportedGenerationMethods"]?.AsArray();
                    bool supportsGenerate = false;
                    if (methods != null) {
                        foreach (var m in methods) {
                            if (m?.ToString() == "generateContent") {
                                supportsGenerate = true;
                                break;
                            }
                        }
                    }

                    if (supportsGenerate && !string.IsNullOrEmpty(name)) {
                        result.Add(name);
                    }
                }
            }
            result.Sort((a, b) => {
                bool aGemini = a.Contains("gemini");
                bool bGemini = b.Contains("gemini");
                if (aGemini && !bGemini) return -1;
                if (!aGemini && bGemini) return 1;
                return string.Compare(b, a, StringComparison.Ordinal);
            });
            return result;
        }
        catch {
            return new List<string>();
        }
    }

    public async Task<AiAnalysisResult> AnalyzeProjectAsync(
        string userPrompt,
        string projectContext,
        string? overrideModel = null,
        int? overrideBudget = null,
        string? systemPrompt = null) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("Gemini API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        int budgetToUse = (overrideBudget.HasValue && overrideBudget.Value > 0) ? overrideBudget.Value : _defaultTokenBudget;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelToUse}:generateContent?key={_apiKey}";

        string resolvedSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt) ? systemPrompt : ProjectConstants.DefaultAiSystemPrompt;

        var sbFull = new StringBuilder();
        sbFull.AppendLine("--- SYSTEM INSTRUCTION ---");
        sbFull.AppendLine(resolvedSystemPrompt);
        sbFull.AppendLine();
        sbFull.AppendLine("--- TASK DESCRIPTION (TARGET) ---");
        sbFull.AppendLine(userPrompt);
        sbFull.AppendLine();
        sbFull.AppendLine("--- PROJECT FILE INDEX & CONTENT ---");
        sbFull.AppendLine(projectContext);
        string fullText = sbFull.ToString();

        var payload = new JsonObject();
        var parts = new JsonArray();
        parts.Add(new JsonObject { ["text"] = fullText });

        var contentObj = new JsonObject();
        contentObj["role"] = "user";
        contentObj["parts"] = parts;

        payload["contents"] = new JsonArray { contentObj };

        var genConfig = new JsonObject();
        genConfig["temperature"] = 0.0;

        if (_thinkingEnabled) {
            var thinkingConfig = new JsonObject();
            thinkingConfig["thinkingBudget"] = budgetToUse;
            genConfig["thinkingConfig"] = thinkingConfig;
        } else {
            genConfig["responseMimeType"] = "application/json";
            genConfig["maxOutputTokens"] = budgetToUse > 0 ? budgetToUse : 8192;
        }
        payload["generationConfig"] = genConfig;

        string requestJson = payload.ToJsonString(_jsonOptions);

        var debugSb = new StringBuilder();
        string maskedUrl = url.Replace(_apiKey, "API_KEY_HIDDEN");
        debugSb.AppendLine($"POST {maskedUrl}");
        debugSb.AppendLine("Content-Type: application/json");
        debugSb.AppendLine();
        debugSb.AppendLine(requestJson);

        var result = new AiAnalysisResult {
            RequestJson = debugSb.ToString(),
            CleanRequestText = fullText,
            ProviderName = "Google Gemini"
        };

        var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, jsonContent);
        string responseBody = await response.Content.ReadAsStringAsync();

        try {
            var parsedResp = JsonNode.Parse(responseBody);
            result.RawResponseJson = parsedResp?.ToJsonString(_jsonOptions) ?? responseBody;
        }
        catch {
            result.RawResponseJson = responseBody;
        }

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"Gemini API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        ParseResponse(responseBody, result);
        return result;
    }

    public async Task<string> TestConnectionAsync(string? overrideModel = null) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("Gemini API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        if (string.IsNullOrWhiteSpace(modelToUse))
            modelToUse = ProjectConstants.DefaultGeminiModel;

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelToUse}:generateContent?key={_apiKey}";

        var payload = new JsonObject();
        var parts = new JsonArray { new JsonObject { ["text"] = "Hi" } };
        var contentObj = new JsonObject { ["role"] = "user", ["parts"] = parts };
        payload["contents"] = new JsonArray { contentObj };

        var genConfig = new JsonObject {
            ["maxOutputTokens"] = 512,
            ["temperature"] = 0.7
        };
        payload["generationConfig"] = genConfig;

        string requestJson = payload.ToJsonString(_jsonOptions);
        var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, jsonContent);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"Gemini API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        var root = JsonNode.Parse(responseBody);
        var candidates = root?["candidates"]?.AsArray();
        if (candidates != null && candidates.Count > 0) {
            var responseParts = candidates[0]?["content"]?["parts"]?.AsArray();
            if (responseParts != null) {
                var sb = new StringBuilder();
                foreach (var part in responseParts) {
                    string? text = part?["text"]?.ToString();
                    if (!string.IsNullOrEmpty(text)) sb.Append(text);
                }
                string reply = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(reply)) return reply;
            }
        }

        return "Connected successfully (Empty response received).";
    }

    private string ExtractErrorMessage(string json) {
        try {
            var node = JsonNode.Parse(json);
            return node?["error"]?["message"]?.ToString() ?? json;
        }
        catch { return json; }
    }

    private void ParseResponse(string json, AiAnalysisResult result) {
        try {
            var root = JsonNode.Parse(json);
            var candidates = root?["candidates"]?.AsArray();
            if (candidates == null || candidates.Count == 0) return;

            var parts = candidates[0]?["content"]?["parts"]?.AsArray();
            if (parts == null) return;

            var sb = new StringBuilder();
            foreach (var part in parts) {
                var text = part?["text"]?.ToString();
                if (!string.IsNullOrEmpty(text)) sb.Append(text);
            }

            result.RawContentText = sb.ToString();
            string jsonText = CleanJsonText(result.RawContentText);
            var paths = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (paths != null) result.SelectedFiles = paths;
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Parse error: {ex.Message}");
        }
    }

    private string CleanJsonText(string text) {
        var match = Regex.Match(text, @"```json\s*(\[[\s\S]*?\])\s*```");
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(text, @"```\s*(\[[\s\S]*?\])\s*```");
        if (match.Success) return match.Groups[1].Value;

        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start >= 0 && end > start) {
            return text.Substring(start, end - start + 1);
        }
        return text;
    }
}