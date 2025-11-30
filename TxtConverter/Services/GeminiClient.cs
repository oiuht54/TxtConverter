using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TxtConverter.Core;

namespace TxtConverter.Services;

public class GeminiResult
{
    public List<string> SelectedFiles { get; set; } = new();
    public string RequestJson { get; set; } = "";
    public string CleanRequestText { get; set; } = "";
    public string RawResponseJson { get; set; } = "";
    public string RawContentText { get; set; } = "";
}

public class GeminiClient
{
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly bool _thinkingEnabled;
    private readonly int _defaultTokenBudget;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public GeminiClient()
    {
        var prefs = PreferenceManager.Instance;
        _apiKey = prefs.GetAiApiKey();
        _defaultModel = prefs.GetAiModel();
        _thinkingEnabled = prefs.GetAiThinkingEnabled();
        _defaultTokenBudget = prefs.GetAiThinkingBudget();
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return new List<string>();

        string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<string>();

            string json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);
            var modelsNode = root?["models"]?.AsArray();

            var result = new List<string>();
            if (modelsNode != null)
            {
                foreach (var node in modelsNode)
                {
                    string name = node?["name"]?.ToString() ?? "";
                    if (name.StartsWith("models/")) name = name.Substring(7);

                    var methods = node?["supportedGenerationMethods"]?.AsArray();
                    bool supportsGenerate = false;
                    if (methods != null)
                    {
                        foreach (var m in methods)
                        {
                            if (m?.ToString() == "generateContent")
                            {
                                supportsGenerate = true;
                                break;
                            }
                        }
                    }

                    if (supportsGenerate && !string.IsNullOrEmpty(name))
                    {
                        result.Add(name);
                    }
                }
            }

            result.Sort((a, b) =>
            {
                bool aGemini = a.Contains("gemini");
                bool bGemini = b.Contains("gemini");
                if (aGemini && !bGemini) return -1;
                if (!aGemini && bGemini) return 1;
                return string.Compare(b, a, StringComparison.Ordinal);
            });

            return result;
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<GeminiResult> AnalyzeProjectAsync(string userPrompt, string projectContext, string? overrideModel = null, int? overrideBudget = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new Exception("API Key is missing. Please check Settings.");

        string modelToUse = !string.IsNullOrWhiteSpace(overrideModel) ? overrideModel : _defaultModel;
        int budgetToUse = (overrideBudget.HasValue && overrideBudget.Value > 0) ? overrideBudget.Value : _defaultTokenBudget;

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelToUse}:generateContent?key={_apiKey}";

        // --- SYSTEM INSTRUCTION: STATIC ANALYSIS & DEPENDENCY LINKING ---
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

        // Full Request Text
        var sbFull = new StringBuilder();
        sbFull.AppendLine("--- SYSTEM INSTRUCTION ---");
        sbFull.Append(sbSys.ToString());
        sbFull.AppendLine();
        sbFull.AppendLine("--- TASK DESCRIPTION (TARGET) ---");
        sbFull.AppendLine(userPrompt);
        sbFull.AppendLine();
        sbFull.AppendLine("--- PROJECT FILE INDEX & CONTENT ---");
        sbFull.AppendLine(projectContext);

        string fullText = sbFull.ToString();

        // Payload
        var payload = new JsonObject();
        var parts = new JsonArray();
        parts.Add(new JsonObject { ["text"] = fullText });

        var contentObj = new JsonObject();
        contentObj["role"] = "user";
        contentObj["parts"] = parts;

        payload["contents"] = new JsonArray { contentObj };

        var genConfig = new JsonObject();
        genConfig["temperature"] = 0.0; // Deterministic output

        if (_thinkingEnabled)
        {
            var thinkingConfig = new JsonObject();
            thinkingConfig["thinkingBudget"] = budgetToUse;
            genConfig["thinkingConfig"] = thinkingConfig;
        }
        else
        {
            genConfig["responseMimeType"] = "application/json";
            genConfig["maxOutputTokens"] = budgetToUse > 0 ? budgetToUse : 8192;
        }

        payload["generationConfig"] = genConfig;

        string requestJson = payload.ToJsonString(_jsonOptions);

        var result = new GeminiResult
        {
            RequestJson = requestJson,
            CleanRequestText = fullText
        };

        // Sending
        var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, jsonContent);
        string responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            var parsedResp = JsonNode.Parse(responseBody);
            result.RawResponseJson = parsedResp?.ToJsonString(_jsonOptions) ?? responseBody;
        }
        catch
        {
            result.RawResponseJson = responseBody;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Gemini API Error ({response.StatusCode}): {ExtractErrorMessage(responseBody)}");
        }

        ParseResponse(responseBody, result);
        return result;
    }

    private string ExtractErrorMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?["error"]?["message"]?.ToString() ?? json;
        }
        catch { return json; }
    }

    private void ParseResponse(string json, GeminiResult result)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var candidates = root?["candidates"]?.AsArray();
            if (candidates == null || candidates.Count == 0) return;

            var parts = candidates[0]?["content"]?["parts"]?.AsArray();
            if (parts == null) return;

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                var text = part?["text"]?.ToString();
                if (!string.IsNullOrEmpty(text)) sb.Append(text);
            }

            result.RawContentText = sb.ToString();

            string jsonText = CleanJsonText(result.RawContentText);

            var paths = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (paths != null) result.SelectedFiles = paths;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Parse error: {ex.Message}");
        }
    }

    private string CleanJsonText(string text)
    {
        var match = Regex.Match(text, @"```json\s*(\[[\s\S]*?\])\s*```");
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(text, @"```\s*(\[[\s\S]*?\])\s*```");
        if (match.Success) return match.Groups[1].Value;

        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }

        return text;
    }
}