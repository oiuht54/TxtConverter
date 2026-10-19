using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Cli;

/// <summary>
/// Matches project relative paths against simple glob patterns (<c>*</c>, <c>**</c>, <c>?</c>).
/// Rules:
///   <list type="bullet">
///     <item><c>src/**</c>        - everything under src</item>
///     <item><c>*.cs</c>          - any file with this extension at any depth</item>
///     <item><c>Assets/Scripts/*  - direct children only</item>
///   </list>
/// </summary>
internal static class GlobMatcher {
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string relativePath, string pattern) {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        string normalizedPath = Normalize(relativePath);
        string normalizedPattern = Normalize(pattern);

        // A trailing slash or a directory name means "everything inside".
        if (normalizedPattern.EndsWith("/", StringComparison.Ordinal))
            normalizedPattern += "**";

        bool rootedPattern = normalizedPattern.Contains("/", StringComparison.Ordinal);

        if (!rootedPattern) {
            // Bare pattern (e.g. "*.cs"): match the file name anywhere in the tree.
            string fileName = normalizedPath.Contains('/') ? normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..] : normalizedPath;
            return RegexFor(normalizedPattern).IsMatch(fileName);
        }

        return RegexFor(normalizedPattern).IsMatch(normalizedPath);
    }

    private static string Normalize(string value) {
        string v = value.Trim().Replace('\\', '/').ToLowerInvariant();
        while (v.StartsWith("./", StringComparison.Ordinal)) v = v.Substring(2);
        v = v.TrimStart('/');
        return v;
    }

    private static Regex RegexFor(string pattern) {
        return Cache.GetOrAdd(pattern, p => {
            var sb = new StringBuilder("^");
            for (int i = 0; i < p.Length; i++) {
                char c = p[i];
                if (c == '*') {
                    if (i + 1 < p.Length && p[i + 1] == '*') {
                        if (i + 2 < p.Length && p[i + 2] == '/') {
                            // "**/" - zero or more directories.
                            sb.Append("(?:.*/)?");
                            i += 2;
                        }
                        else {
                            sb.Append(".*");
                            i++;
                        }
                    }
                    else {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?') {
                    sb.Append("[^/]");
                }
                else {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
        });
    }
}