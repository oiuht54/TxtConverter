using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TxtConverter.Core;

namespace TxtConverter.Services;

public class UpdateCheckerService {
    private static UpdateCheckerService? _instance;
    public static UpdateCheckerService Instance => _instance ??= new UpdateCheckerService();

    private readonly HttpClient _httpClient;
    private readonly string _latestReleaseUrl;

    private UpdateCheckerService() {
        _httpClient = new HttpClient();
        // A standard robust browser User-Agent ensures bypass of strict firewall and corporate proxies
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _latestReleaseUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/releases/latest";
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync() {
        try {
            // 1. Try fetching the full list of releases to support Pre-releases (which /releases/latest ignores)
            string listUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/releases";
            var response = await _httpClient.GetAsync(listUrl);
            
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0) {
                    // Find the first release that is not a draft
                    foreach (var releaseElement in doc.RootElement.EnumerateArray()) {
                        bool isDraft = releaseElement.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
                        if (isDraft) continue;

                        string tagName = releaseElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                        string htmlUrl = releaseElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                        string body = releaseElement.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                        string name = releaseElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                        if (!string.IsNullOrWhiteSpace(tagName) && !string.IsNullOrWhiteSpace(htmlUrl)) {
                            return new ReleaseInfo {
                                TagName = tagName,
                                HtmlUrl = htmlUrl,
                                Body = body,
                                Name = name
                            };
                        }
                    }
                }
            }

            // 2. Fallback to /releases/latest if the list fails or is empty
            var latestResponse = await _httpClient.GetAsync(_latestReleaseUrl);
            if (latestResponse.IsSuccessStatusCode) {
                string json = await latestResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(tagName) && !string.IsNullOrWhiteSpace(htmlUrl)) {
                    return new ReleaseInfo {
                        TagName = tagName,
                        HtmlUrl = htmlUrl,
                        Body = body,
                        Name = name
                    };
                }
            }
        }
        catch (Exception ex) {
            // Output to diagnostics debug console for local verification
            System.Diagnostics.Debug.WriteLine($"Update check exception: {ex}");
        }
        return null;
    }

    public static bool IsNewerVersion(string currentVerStr, string latestVerStr) {
        if (string.IsNullOrWhiteSpace(currentVerStr) || string.IsNullOrWhiteSpace(latestVerStr)) {
            return false;
        }

        // Extracts version-like sequences (e.g. "1.8.0-beta" -> "1.8.0" or "zapret-1.7.2b" -> "1.7.2")
        var versionRegex = new Regex(@"\d+(\.\d+)+");
        
        var currentMatch = versionRegex.Match(currentVerStr);
        var latestMatch = versionRegex.Match(latestVerStr);

        if (currentMatch.Success && latestMatch.Success) {
            if (Version.TryParse(currentMatch.Value, out Version? current) && 
                Version.TryParse(latestMatch.Value, out Version? latest)) {
                return latest > current;
            }
        }
        return false;
    }
}

public class ReleaseInfo {
    public string TagName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string Body { get; set; } = "";
    public string Name { get; set; } = "";
}