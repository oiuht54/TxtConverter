using System.IO;
using System.Linq;

namespace TxtConverter.Core.Logic;

/// <summary>
/// Resolves file paths returned by an AI provider back to real files of the scanned project.
/// Shared between the GUI (AI Smart Select window) and the CLI (<c>ai select</c>).
/// </summary>
public static class AiFileMatcher {

    /// <summary>
    /// Matches AI-suggested paths against the list of known project files.
    /// Tries: absolute/rooted path -> normalized relative path suffix -> unique file name.
    /// </summary>
    /// <param name="allFiles">Files discovered by the scanner.</param>
    /// <param name="rootPath">Project root directory.</param>
    /// <param name="aiPaths">Raw paths extracted from the AI response.</param>
    /// <returns>Matched absolute file paths (duplicates removed).</returns>
    public static List<string> Match(IReadOnlyCollection<string> allFiles, string rootPath, IReadOnlyCollection<string> aiPaths) {
        var matched = new HashSet<string>();
        var fileMap = new Dictionary<string, string>();
        foreach (var file in allFiles) {
            string key = Path.GetFullPath(file).ToLower();
            if (!fileMap.ContainsKey(key)) fileMap[key] = file;
        }

        foreach (var rawAiPath in aiPaths) {
            string aiClean = rawAiPath.Trim().Trim('\"', '\'');
            if (string.IsNullOrWhiteSpace(aiClean)) continue;

            string? foundOriginalPath = null;
            try {
                string fullPathCandidate = Path.GetFullPath(Path.Combine(rootPath, aiClean)).ToLower();
                if (fileMap.TryGetValue(fullPathCandidate, out var original)) {
                    foundOriginalPath = original;
                }
            }
            catch { }

            if (foundOriginalPath == null) {
                string aiNorm = aiClean.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).ToLower();
                var matchKey = fileMap.Keys.FirstOrDefault(k =>
                    k.EndsWith(Path.DirectorySeparatorChar + aiNorm) ||
                    k == aiNorm
                );
                if (matchKey != null) {
                    foundOriginalPath = fileMap[matchKey];
                }
            }

            if (foundOriginalPath == null) {
                string aiFileName = Path.GetFileName(aiClean).ToLower();
                var candidates = allFiles.Where(f => Path.GetFileName(f).ToLower() == aiFileName).ToList();
                if (candidates.Count == 1) {
                    foundOriginalPath = candidates[0];
                }
            }

            if (foundOriginalPath != null) {
                matched.Add(foundOriginalPath);
            }
        }

        return matched.ToList();
    }
}