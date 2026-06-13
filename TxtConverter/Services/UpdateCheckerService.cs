using System;
using System.Net.Http;
using System.Text.Json;
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
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TxtConverter-UpdateChecker");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _latestReleaseUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/releases/latest";
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync() {
        try {
            var response = await _httpClient.GetAsync(_latestReleaseUrl);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl)) {
                return null;
            }

            return new ReleaseInfo {
                TagName = tagName,
                HtmlUrl = htmlUrl,
                Body = body,
                Name = name
            };
        }
        catch {
            return null; // Fail silently on offline or poor connection states
        }
    }

    public static bool IsNewerVersion(string currentVerStr, string latestVerStr) {
        if (string.IsNullOrWhiteSpace(currentVerStr) || string.IsNullOrWhiteSpace(latestVerStr)) {
            return false;
        }

        string currentClean = currentVerStr.TrimStart('v', 'V', ' ');
        string latestClean = latestVerStr.TrimStart('v', 'V', ' ');

        if (Version.TryParse(currentClean, out Version? current) && 
            Version.TryParse(latestClean, out Version? latest)) {
            return latest > current;
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