using System;
using System.Collections.Generic;
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

public class NvidiaClient : IAiClient {
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly double _topP;
    private readonly bool _reasoningEnabled;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string BaseUrl = "https://integrate.api.nvidia.com/v1";

    public NvidiaClient(string apiKey, string defaultModel, int maxTokens, double temperature, double topP, bool reasoningEnabled) {
        _apiKey = apiKey;
        _defaultModel = defaultModel;
        _maxTokens = maxTokens;
        _temperature = temperature;
        _topP = topP;
        _reasoningEnabled = reasoningEnabled;

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public async Task<List<string>> GetAvailableModelsAsync() {
        if (string.IsNullOrWhiteSpace(_apiKey)) return new List<string>();

        string url = $"{BaseUrl}/models";
        try {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<string>();

            string json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);
            var dataNode = root?["data"]?.AsArray();

            var result = new List<string>();
            if (dataNode != null) {
                foreach (var node in dataNode) {
                    string id = node?["id"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id)) {
                        result.Add(id);
                    }
                }
            }
            result.Sort();
            return result;
        }
        catch {
            return new List<string> {
                _defaultModel,
                "minimaxai/minimax-m2",
                "meta/llama-3.1-70b-instruct",
                "deepseek-ai/deepseek-r1"
            };
        }
    }

    public async Task<AiAnalysisResult> AnalyzeProjectAsync(
        string userPrompt,
        string projectContext,
        string? overrideModel = null,
        int? overrideBudget = null,
        string? systemPrompt = null) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("NVIDIA API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        string resolvedSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt) ? systemPrompt : ProjectConstants.DefaultAiSystemPrompt;

        var sbUser = new StringBuilder();
        sbUser.AppendLine("--- TASK DESCRIPTION ---");
        sbUser.AppendLine(userPrompt);
        sbUser.AppendLine();
        sbUser.AppendLine("--- PROJECT CONTEXT ---");
        sbUser.AppendLine(projectContext);

        var payload = new JsonObject();
        payload["model"] = modelToUse;
        payload["temperature"] = _temperature;
        payload["top_p"] = _topP;
        payload["max_tokens"] = _maxTokens > 0 ? _maxTokens : 4096;
        payload["stream"] = false;

        if (_reasoningEnabled) {
            var templateKwargs = new JsonObject();
            templateKwargs["thinking"] = true;
            templateKwargs["effort"] = "high";
            templateKwargs["reasoning_effort"] = "high";
            payload["chat_template_kwargs"] = templateKwargs;
            payload["reasoning_effort"] = "high";
            payload["effort"] = "high";
        }

        var messages = new JsonArray();
        messages.Add(new JsonObject { ["role"] = "system", ["content"] = resolvedSystemPrompt });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = sbUser.ToString() });
        payload["messages"] = messages;

        string requestJson = payload.ToJsonString(_jsonOptions);

        var debugSb = new StringBuilder();
        string maskedKey = _apiKey.Length > 8 ? _apiKey.Substring(0, 4) + "..." + _apiKey.Substring(_apiKey.Length - 4) : "***";
        debugSb.AppendLine($"POST {BaseUrl}/chat/completions");
        debugSb.AppendLine($"Authorization: Bearer {maskedKey}");
        debugSb.AppendLine("Content-Type: application/json");
        debugSb.AppendLine();
        debugSb.AppendLine(requestJson);

        var result = new AiAnalysisResult {
            RequestJson = debugSb.ToString(),
            CleanRequestText = resolvedSystemPrompt + "\n\n" + sbUser.ToString(),
            ProviderName = "NVIDIA NIM"
        };

        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{BaseUrl}/chat/completions", content);
        string responseBody = await response.Content.ReadAsStringAsync();

        try {
            var parsedResp = JsonNode.Parse(responseBody);
            result.RawResponseJson = parsedResp?.ToJsonString(_jsonOptions) ?? responseBody;
        }
        catch {
            result.RawResponseJson = responseBody;
        }

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"NVIDIA API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        ParseResponse(responseBody, result);
        return result;
    }

    public async Task<string> TestConnectionAsync(string? overrideModel = null) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("NVIDIA API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        if (string.IsNullOrWhiteSpace(modelToUse))
            modelToUse = ProjectConstants.DefaultNvidiaModel;

        var payload = new JsonObject {
            ["model"] = modelToUse,
            ["max_tokens"] = 256,
            ["temperature"] = 0.5,
            ["stream"] = false
        };

        var messages = new JsonArray {
            new JsonObject { ["role"] = "user", ["content"] = "Hi" }
        };
        payload["messages"] = messages;

        string requestJson = payload.ToJsonString(_jsonOptions);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{BaseUrl}/chat/completions", content);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) {
            throw new Exception($"NVIDIA API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        var root = JsonNode.Parse(responseBody);
        var choices = root?["choices"]?.AsArray();
        if (choices != null && choices.Count > 0) {
            string? reply = choices[0]?["message"]?["content"]?.ToString();
            if (!string.IsNullOrEmpty(reply)) return reply.Trim();
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
}