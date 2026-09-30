using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TxtConverter.Core;

namespace TxtConverter.Services.Ai;

public class OpenAiCompatibleClient : IAiClient {
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly double _topP;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAiCompatibleClient(
        string endpoint,
        string apiKey,
        string defaultModel,
        int maxTokens,
        double temperature,
        double topP) {
        _baseUrl = NormalizeEndpoint(endpoint);
        _apiKey = apiKey;
        _defaultModel = defaultModel;
        _maxTokens = maxTokens > 0 ? maxTokens : 4096;
        _temperature = temperature;
        _topP = topP;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        if (!string.IsNullOrWhiteSpace(_apiKey)) {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public async Task<List<string>> GetAvailableModelsAsync() {
        var result = new List<string>();

        // 1. Try standard OpenAI /models endpoint
        string url = $"{_baseUrl}/models";
        bool fetched = await TryFetchModelsUrlAsync(url, result);

        // 2. If failed and baseUrl does not end with /v1, try appending /v1/models (common with Ollama / LM Studio)
        if (!fetched && !_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            fetched = await TryFetchModelsUrlAsync($"{_baseUrl}/v1/models", result);
        }

        // 3. If still failed, try Ollama's native /api/tags endpoint
        if (!fetched) {
            string hostOnly = _baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? _baseUrl.Substring(0, _baseUrl.Length - 3).TrimEnd('/')
                : _baseUrl;
            fetched = await TryFetchModelsUrlAsync($"{hostOnly}/api/tags", result);
        }

        result = result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Sort();
        return result.Count > 0 ? result : GetFallbackModels();
    }

    private async Task<bool> TryFetchModelsUrlAsync(string url, List<string> result) {
        try {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return false;

            string json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);
            if (root == null) return false;

            int initialCount = result.Count;

            // Format A: { "data": [ { "id": "model" }, ... ] }
            var dataNode = root["data"]?.AsArray();
            if (dataNode != null) {
                ExtractModelsFromArray(dataNode, result);
            }

            // Format B: { "models": [ { "name": "model" }, ... ] } (e.g. Ollama tags)
            var modelsNode = root["models"]?.AsArray();
            if (modelsNode != null) {
                ExtractModelsFromArray(modelsNode, result);
            }

            // Format C: Root array [ { "id": "model" }, ... ] or [ "model1", ... ]
            if (root is JsonArray arr) {
                ExtractModelsFromArray(arr, result);
            }

            return result.Count > initialCount;
        }
        catch {
            return false;
        }
    }

    private static void ExtractModelsFromArray(JsonArray arr, List<string> result) {
        foreach (var node in arr) {
            if (node is JsonObject obj) {
                string id = obj["id"]?.ToString() ??
                            obj["name"]?.ToString() ??
                            obj["model"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(id)) {
                    result.Add(id);
                }
            }
            else if (node is JsonValue val) {
                string id = val.ToString();
                if (!string.IsNullOrWhiteSpace(id)) {
                    result.Add(id);
                }
            }
        }
    }

    public async Task<AiAnalysisResult> AnalyzeProjectAsync(string userPrompt, string projectContext, string? overrideModel = null, int? overrideBudget = null) {
        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;

        var sbSys = new StringBuilder();
        sbSys.AppendLine("You are a **Static Code Analysis Engine**.");
        sbSys.AppendLine("Your goal is to build a complete execution environment for a specific task.");
        sbSys.AppendLine("Do not guess based on filenames. **READ THE CODE** to find dependencies.");
        sbSys.AppendLine();
        sbSys.AppendLine("### EXECUTION PROTOCOL:");
        sbSys.AppendLine("1. **Identify the Target:** Find the script(s) that directly implement the task logic.");
        sbSys.AppendLine("2. **Scan for Hard Dependencies (The 'Mid Model' Strategy):**");
        sbSys.AppendLine(" - Look inside the Target Script.");
        sbSys.AppendLine(" - If it calls `PoolManager.get(...)` -> INCLUDE `PoolManager.gd`.");
        sbSys.AppendLine(" - If it uses `preload(\"res://path/to/item.tres\")` -> INCLUDE `item.tres`.");
        sbSys.AppendLine(" - If it instantiates a Scene (`.tscn`), INCLUDE that `.tscn` file.");
        sbSys.AppendLine(" - If it inherits `extends InteractiveObject`, INCLUDE `InteractiveObject.gd`.");
        sbSys.AppendLine("3. **Scan for Data Definitions:**");
        sbSys.AppendLine(" - If the Target uses a variable typed as a custom Class/Resource, include the file where that Class is defined.");
        sbSys.AppendLine("4. **Identify Reference Patterns:**");
        sbSys.AppendLine(" - Does another file in the project solve a similar problem? (e.g., if writing `VoxelWorld`, look at `WallGenerator`). Include it as a coding pattern reference.");
        sbSys.AppendLine();
        sbSys.AppendLine("### FILTERING RULES:");
        sbSys.AppendLine("- **Strict Relevance:** Do NOT include thematic cousins (e.g., do not include 'WandGenerator' for 'TerrainGeneration' just because they both generate things). Only include if they share a base class or utility library.");
        sbSys.AppendLine("- **Completeness:** If code A calls code B, and code B is missing, the code is broken. Include B.");
        sbSys.AppendLine();
        sbSys.AppendLine("### OUTPUT FORMAT:");
        sbSys.AppendLine("[\"path/to/target.gd\", \"path/to/dependency.gd\", \"path/to/resource.tres\"]");
        sbSys.AppendLine("(Return ONLY JSON)");

        var sbUser = new StringBuilder();
        sbUser.AppendLine("--- TASK DESCRIPTION ---");
        sbUser.AppendLine(userPrompt);
        sbUser.AppendLine();
        sbUser.AppendLine("--- PROJECT CONTEXT ---");
        sbUser.AppendLine(projectContext);

        var payload = new JsonObject();
        payload["model"] = modelToUse;

        bool isOpenAiReasoningModel = modelToUse.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                                      modelToUse.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                                      modelToUse.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

        if (isOpenAiReasoningModel) {
            payload["max_completion_tokens"] = _maxTokens;
        } else {
            payload["temperature"] = _temperature;
            payload["top_p"] = _topP;
            payload["max_tokens"] = _maxTokens;
        }

        payload["stream"] = false;

        var messages = new JsonArray();
        messages.Add(new JsonObject { ["role"] = "system", ["content"] = sbSys.ToString() });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = sbUser.ToString() });
        payload["messages"] = messages;

        string requestJson = payload.ToJsonString(_jsonOptions);

        var debugSb = new StringBuilder();
        string maskedKey = !string.IsNullOrEmpty(_apiKey)
            ? (_apiKey.Length > 8 ? _apiKey.Substring(0, 4) + "..." + _apiKey.Substring(_apiKey.Length - 4) : "***")
            : "[No Key]";

        debugSb.AppendLine($"POST {_baseUrl}/chat/completions");
        if (!string.IsNullOrEmpty(_apiKey)) {
            debugSb.AppendLine($"Authorization: Bearer {maskedKey}");
        }
        debugSb.AppendLine("Content-Type: application/json");
        debugSb.AppendLine();
        debugSb.AppendLine(requestJson);

        var result = new AiAnalysisResult {
            RequestJson = debugSb.ToString(),
            CleanRequestText = sbSys.ToString() + "\n\n" + sbUser.ToString(),
            ProviderName = $"OpenAI Compatible ({_baseUrl})"
        };

        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content);
        string responseBody = await response.Content.ReadAsStringAsync();

        try {
            var parsedResp = JsonNode.Parse(responseBody);
            result.RawResponseJson = parsedResp?.ToJsonString(_jsonOptions) ?? responseBody;
        }
        catch {
            result.RawResponseJson = responseBody;
        }

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"OpenAI API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        ParseResponse(responseBody, result);
        return result;
    }

    public async Task<string> TestConnectionAsync(string? overrideModel = null) {
        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        if (string.IsNullOrWhiteSpace(modelToUse))
            modelToUse = ProjectConstants.DefaultCustomOpenAiModel;

        var payload = new JsonObject {
            ["model"] = modelToUse,
            ["stream"] = false
        };

        bool isOpenAiReasoningModel = modelToUse.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                                      modelToUse.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                                      modelToUse.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

        if (isOpenAiReasoningModel) {
            payload["max_completion_tokens"] = 256;
        } else {
            payload["max_tokens"] = 256;
            payload["temperature"] = 0.7;
        }

        var messages = new JsonArray {
            new JsonObject { ["role"] = "user", ["content"] = "Hi" }
        };
        payload["messages"] = messages;

        string requestJson = payload.ToJsonString(_jsonOptions);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"OpenAI API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        var root = JsonNode.Parse(responseBody);
        var choices = root?["choices"]?.AsArray();
        if (choices != null && choices.Count > 0) {
            string? reply = choices[0]?["message"]?["content"]?.ToString();
            if (!string.IsNullOrEmpty(reply)) return reply.Trim();
        }

        return "Connected successfully (Empty response received).";
    }

    private static string NormalizeEndpoint(string? endpoint) {
        if (string.IsNullOrWhiteSpace(endpoint)) {
            return "https://api.openai.com/v1";
        }

        string trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "/chat/completions".Length).TrimEnd('/');
        }
        else if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "/models".Length).TrimEnd('/');
        }
        return trimmed;
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
            var choices = root?["choices"]?.AsArray();
            if (choices == null || choices.Count == 0) return;

            var content = choices[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrEmpty(content)) return;

            result.RawContentText = content;
            string jsonText = CleanJsonText(content);
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

    private List<string> GetFallbackModels() {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(_defaultModel)) {
            list.Add(_defaultModel);
        }
        if (!list.Contains("gpt-4o-mini")) list.Add("gpt-4o-mini");
        if (!list.Contains("gpt-4o")) list.Add("gpt-4o");
        if (!list.Contains("deepseek-chat")) list.Add("deepseek-chat");
        return list;
    }
}