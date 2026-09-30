using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TxtConverter.Core;

namespace TxtConverter.Services;

public class ReleaseInfo {
    public string TagName { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class UpdateCheckResult {
    public bool Success { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public ReleaseInfo? Release { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class UpdateCheckerService {
    private static UpdateCheckerService? _instance;
    public static UpdateCheckerService Instance => _instance ??= new UpdateCheckerService();

    private readonly HttpClient _httpClient;

    private UpdateCheckerService() {
        var handler = new HttpClientHandler {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };

        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Strict compliance with GitHub REST API documentation:
        // Use genuine Application User-Agent; spoofed browser strings trigger Cloudflare/GitHub 403 Forbidden.
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"TxtConverter-App/{ProjectConstants.CurrentVersion} (GitHub: {ProjectConstants.GitHubRepo})");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync() {
        var result = await CheckForUpdatesDetailedAsync();
        return (result.Success && result.IsUpdateAvailable) ? result.Release : null;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesDetailedAsync() {
        var errors = new List<string>();

        // Strategy 1: Fetch releases array and evaluate the highest semantic version
        try {
            string listUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/releases";
            using var response = await _httpClient.GetAsync(listUrl);
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) {
                    ReleaseInfo? highestRelease = null;
                    string highestTag = ProjectConstants.CurrentVersion;

                    foreach (var releaseElement in doc.RootElement.EnumerateArray()) {
                        bool isDraft = releaseElement.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
                        if (isDraft) continue;

                        string tagName = releaseElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                        string htmlUrl = releaseElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                        string body = releaseElement.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                        string name = releaseElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(tagName)) continue;
                        if (string.IsNullOrWhiteSpace(htmlUrl)) {
                            htmlUrl = $"https://github.com/{ProjectConstants.GitHubRepo}/releases/tag/{tagName}";
                        }

                        if (IsNewerVersion(highestTag, tagName)) {
                            highestTag = tagName;
                            highestRelease = new ReleaseInfo {
                                TagName = tagName,
                                HtmlUrl = htmlUrl,
                                Body = body,
                                Name = name
                            };
                        }
                    }

                    if (highestRelease != null) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = true,
                            Release = highestRelease
                        };
                    }

                    if (doc.RootElement.GetArrayLength() > 0) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = false
                        };
                    }
                }
            } else {
                errors.Add($"Releases API returned HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) {
            errors.Add($"Releases API: {ex.Message}");
        }

        // Strategy 2: Fallback to /releases/latest endpoint
        try {
            string latestUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/releases/latest";
            using var response = await _httpClient.GetAsync(latestUrl);
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(tagName)) {
                    if (string.IsNullOrWhiteSpace(htmlUrl)) {
                        htmlUrl = $"https://github.com/{ProjectConstants.GitHubRepo}/releases/tag/{tagName}";
                    }

                    if (IsNewerVersion(ProjectConstants.CurrentVersion, tagName)) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = true,
                            Release = new ReleaseInfo {
                                TagName = tagName,
                                HtmlUrl = htmlUrl,
                                Body = body,
                                Name = name
                            }
                        };
                    }

                    return new UpdateCheckResult {
                        Success = true,
                        IsUpdateAvailable = false
                    };
                }
            } else {
                errors.Add($"Latest Release API returned HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) {
            errors.Add($"Latest Release API: {ex.Message}");
        }

        // Strategy 3: Fallback to Git Tags (/tags) when formal GitHub Release objects were not created
        try {
            string tagsUrl = $"https://api.github.com/repos/{ProjectConstants.GitHubRepo}/tags";
            using var response = await _httpClient.GetAsync(tagsUrl);
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) {
                    string highestTag = ProjectConstants.CurrentVersion;
                    bool foundAny = false;

                    foreach (var tagElement in doc.RootElement.EnumerateArray()) {
                        string tagName = tagElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(tagName)) continue;
                        foundAny = true;

                        if (IsNewerVersion(highestTag, tagName)) {
                            highestTag = tagName;
                        }
                    }

                    if (IsNewerVersion(ProjectConstants.CurrentVersion, highestTag)) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = true,
                            Release = new ReleaseInfo {
                                TagName = highestTag,
                                HtmlUrl = $"https://github.com/{ProjectConstants.GitHubRepo}/releases/tag/{highestTag}",
                                Name = highestTag,
                                Body = $"Version {highestTag} was tagged on GitHub."
                            }
                        };
                    }

                    if (foundAny) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = false
                        };
                    }
                }
            } else {
                errors.Add($"Tags API returned HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) {
            errors.Add($"Tags API: {ex.Message}");
        }

        // Strategy 4: Fallback to GitHub Web Redirect (bypasses GitHub REST API rate limits and token requirements)
        try {
            string webUrl = $"https://github.com/{ProjectConstants.GitHubRepo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Head, webUrl);
            using var response = await _httpClient.SendAsync(request);

            var finalUri = response.RequestMessage?.RequestUri?.ToString() ?? "";
            int tagIdx = finalUri.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
            if (tagIdx >= 0) {
                string tagName = finalUri.Substring(tagIdx + "/releases/tag/".Length).TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(tagName)) {
                    if (IsNewerVersion(ProjectConstants.CurrentVersion, tagName)) {
                        return new UpdateCheckResult {
                            Success = true,
                            IsUpdateAvailable = true,
                            Release = new ReleaseInfo {
                                TagName = tagName,
                                HtmlUrl = finalUri,
                                Name = tagName,
                                Body = $"Release {tagName} is available on GitHub."
                            }
                        };
                    }

                    return new UpdateCheckResult {
                        Success = true,
                        IsUpdateAvailable = false
                    };
                }
            }
        }
        catch (Exception ex) {
            errors.Add($"Web redirect check: {ex.Message}");
        }

        return new UpdateCheckResult {
            Success = false,
            IsUpdateAvailable = false,
            ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : "Could not retrieve version details from GitHub."
        };
    }

    public static int[]? ParseVersionComponents(string versionStr) {
        if (string.IsNullOrWhiteSpace(versionStr)) return null;

        var match = Regex.Match(versionStr, @"\b(\d+(\.\d+)*)\b");
        if (!match.Success) return null;

        var parts = match.Value.Split('.');
        var components = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            if (!int.TryParse(parts[i], out components[i])) {
                return null;
            }
        }
        return components;
    }

    public static int CompareVersions(string verA, string verB) {
        var compA = ParseVersionComponents(verA);
        var compB = ParseVersionComponents(verB);

        if (compA == null && compB == null) return 0;
        if (compA == null) return -1;
        if (compB == null) return 1;

        int maxLen = Math.Max(compA.Length, compB.Length);
        for (int i = 0; i < maxLen; i++) {
            int valA = i < compA.Length ? compA[i] : 0;
            int valB = i < compB.Length ? compB[i] : 0;
            if (valA > valB) return 1;
            if (valA < valB) return -1;
        }
        return 0;
    }

    public static bool IsNewerVersion(string currentVerStr, string latestVerStr) {
        return CompareVersions(latestVerStr, currentVerStr) > 0;
    }
}