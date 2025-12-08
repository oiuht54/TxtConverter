using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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

    // ИСПРАВЛЕНО: Только официальный эндпоинт NVIDIA
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
            // Fallback список, если API моделей недоступно
            return new List<string> { 
                _defaultModel, 
                "minimaxai/minimax-m2",
                "meta/llama-3.1-70b-instruct", 
                "deepseek-ai/deepseek-r1" 
            };
        }
    }

    public async Task<AiAnalysisResult> AnalyzeProjectAsync(string userPrompt, string projectContext, string? overrideModel = null, int? overrideBudget = null) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("NVIDIA API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;

        // --- SYSTEM INSTRUCTION (RESTORED) ---
        var sbSys = new StringBuilder();
        sbSys.AppendLine("You are a **Static Code Analysis Engine**.");
        sbSys.AppendLine("Your goal is to build a complete execution environment for a specific task.");
        sbSys.AppendLine("Do not guess based on filenames. **READ THE CODE** to find dependencies.");
        sbSys.AppendLine();
        sbSys.AppendLine("### EXECUTION PROTOCOL:");
        sbSys.AppendLine("1. **Identify the Target:** Find the script(s) that directly implement the task logic.");
        sbSys.AppendLine("2. **Scan for Hard Dependencies (The 'Mid Model' Strategy):**");
        sbSys.AppendLine("   - Look inside the Target Script.");
        sbSys.AppendLine("   - If it calls `PoolManager.get(...)` -> INCLUDE `PoolManager.gd`.");
        sbSys.AppendLine("   - If it uses `preload(\"res://path/to/item.tres\")` -> INCLUDE `item.tres`.");
        sbSys.AppendLine("   - If it instantiates a Scene (`.tscn`), INCLUDE that `.tscn` file.");
        sbSys.AppendLine("   - If it inherits `extends InteractiveObject`, INCLUDE `InteractiveObject.gd`.");
        sbSys.AppendLine("3. **Scan for Data Definitions:**");
        sbSys.AppendLine("   - If the Target uses a variable typed as a custom Class/Resource, include the file where that Class is defined.");
        sbSys.AppendLine("4. **Identify Reference Patterns:**");
        sbSys.AppendLine("   - Does another file in the project solve a similar problem? (e.g., if writing `VoxelWorld`, look at `WallGenerator`). Include it as a coding pattern reference.");
        sbSys.AppendLine();
        sbSys.AppendLine("### FILTERING RULES:");
        sbSys.AppendLine("- **Strict Relevance:** Do NOT include thematic cousins (e.g., do not include 'WandGenerator' for 'TerrainGeneration' just because they both generate things). Only include if they share a base class or utility library.");
        sbSys.AppendLine("- **Completeness:** If code A calls code B, and code B is missing, the code is broken. Include B.");
        sbSys.AppendLine();
        sbSys.AppendLine("### OUTPUT FORMAT:");
        sbSys.AppendLine("[\"path/to/target.gd\", \"path/to/dependency.gd\", \"path/to/resource.tres\"]");
        sbSys.AppendLine("(Return ONLY JSON)");
        // -------------------------------------

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
        messages.Add(new JsonObject { ["role"] = "system", ["content"] = sbSys.ToString() });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = sbUser.ToString() });
        payload["messages"] = messages;

        string requestJson = payload.ToJsonString(_jsonOptions);

        // --- DEBUG INFO ---
        var debugSb = new StringBuilder();
        string maskedKey = _apiKey.Length > 8 ? _apiKey.Substring(0, 4) + "..." + _apiKey.Substring(_apiKey.Length - 4) : "***";
        debugSb.AppendLine($"POST {BaseUrl}/chat/completions");
        debugSb.AppendLine($"Authorization: Bearer {maskedKey}");
        debugSb.AppendLine("Content-Type: application/json");
        debugSb.AppendLine();
        debugSb.AppendLine(requestJson);
        // ------------------

        var result = new AiAnalysisResult {
            RequestJson = debugSb.ToString(),
            CleanRequestText = sbSys.ToString() + "\n\n" + sbUser.ToString(),
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