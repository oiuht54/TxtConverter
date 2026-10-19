using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TxtConverter.Core.Logic;
using TxtConverter.Core.Logic.Processing;
using TxtConverter.Services;

namespace TxtConverter.Cli;

/// <summary>Everything the CLI needs to know about a scanned project.</summary>
internal sealed class ResolvedProject {
    public string RootPath { get; init; } = "";
    public string PresetName { get; set; } = "";
    public bool PresetAutoDetected { get; set; }
    public List<string> Extensions { get; set; } = new();
    public List<string> IgnoredFolders { get; set; } = new();
    public string ExclusionsCsv { get; set; } = "";
    public List<string> FoundFiles { get; set; } = new();

    /// <summary>Files that end up in the report with full content (everything else becomes a stub).</summary>
    public HashSet<string> SelectedForMerge { get; set; } = new();

    public string Relative(string file) {
        try {
            return Path.GetRelativePath(RootPath, file).Replace('\\', '/');
        }
        catch {
            return file;
        }
    }
}

/// <summary>
/// Applies the same rules as the GUI: preset lookup / auto detection, extension filtering,
/// global ignore lists and the exclusion (stub) rules.
/// </summary>
internal static class ProjectResolver {
    public static async Task<ResolvedProject> ResolveAsync(ParsedArgs args, CliContext ctx) {
        string root = CliCommand.ResolveRootPath(args, ctx);
        var project = new ResolvedProject { RootPath = root };

        ResolvePreset(args, ctx, project);
        project.Extensions = args.Has("ext")
            ? args.CsvValues("ext")
            : CliCommand.SplitCsv(PresetManager.Instance.GetExtensionsFor(project.PresetName));

        project.IgnoredFolders = CliCommand.SplitCsv(PresetManager.Instance.GetIgnoredFoldersFor(project.PresetName))
            .Union(args.CsvValues("ignore"), StringComparer.OrdinalIgnoreCase)
            .Union(CliCommand.SplitCsv(ctx.Prefs.GetGlobalIgnoredFolders()), StringComparer.OrdinalIgnoreCase)
            .ToList();

        string localExclusions = args.Has("exclude")
            ? string.Join(",", args.CsvValues("exclude"))
            : PresetManager.Instance.GetExclusionsFor(project.PresetName);
        project.ExclusionsCsv = string.Join(",",
            CliCommand.SplitCsv(localExclusions).Union(CliCommand.SplitCsv(ctx.Prefs.GetGlobalExcludedPaths()), StringComparer.OrdinalIgnoreCase));

        ctx.Log.Detail($"Scanning: {root}");
        var scanner = new FileScanner(project.Extensions, project.IgnoredFolders);
        project.FoundFiles = await scanner.ScanAsync(root);

        var matcher = new ExclusionMatcher(project.ExclusionsCsv);
        project.SelectedForMerge = new HashSet<string>(
            project.FoundFiles.Where(f => !matcher.IsExcluded(f, root)),
            StringComparer.OrdinalIgnoreCase);

        return project;
    }

    private static void ResolvePreset(ParsedArgs args, CliContext ctx, ResolvedProject project) {
        string? explicitPreset = args.Value("preset");
        if (!string.IsNullOrWhiteSpace(explicitPreset)) {
            if (!PresetManager.Instance.HasPreset(explicitPreset))
                throw CliException.NotFound($"Unknown preset '{explicitPreset}'. Run 'TxtConverter.exe presets list' to see available presets.");
            project.PresetName = explicitPreset;
            return;
        }

        string? detected = PresetManager.Instance.AutoDetectPreset(project.RootPath);
        if (!string.IsNullOrWhiteSpace(detected) && PresetManager.Instance.HasPreset(detected)) {
            project.PresetName = detected;
            project.PresetAutoDetected = true;
            ctx.Log.Detail($"Preset auto-detected: {detected}");
            return;
        }

        string lastPreset = ctx.Prefs.GetLastPreset();
        if (PresetManager.Instance.HasPreset(lastPreset)) {
            project.PresetName = lastPreset;
            ctx.Log.Detail($"Project type not recognized, using last used preset: {lastPreset}");
            return;
        }

        project.PresetName = "Manual";
        ctx.Log.Warn("Project type could not be auto-detected. Using the empty 'Manual' preset.");
    }

    /// <summary>Applies the --include / --exclude-file / --include-file / --select flags on top of the default selection.</summary>
    public static void ApplySelectionFlags(ParsedArgs args, ResolvedProject project, CliContext ctx) {
        string? selectMode = args.Value("select");
        if (!string.IsNullOrWhiteSpace(selectMode)) {
            switch (selectMode.ToLowerInvariant()) {
                case "all":
                    project.SelectedForMerge = new HashSet<string>(project.FoundFiles, StringComparer.OrdinalIgnoreCase);
                    break;
                case "none":
                    project.SelectedForMerge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    throw CliException.Usage($"Invalid value '{selectMode}' for --select. Allowed: all, none.");
            }
        }

        foreach (string file in ReadPathList(args.Value("exclude-file"), project, ctx)) {
            project.SelectedForMerge.Remove(file);
        }

        foreach (string pattern in args.CsvValues("include")) {
            int before = project.SelectedForMerge.Count;
            foreach (string file in project.FoundFiles) {
                if (GlobMatcher.IsMatch(project.Relative(file), pattern))
                    project.SelectedForMerge.Add(file);
            }
            ctx.Log.Trace($"Pattern '{pattern}' added {project.SelectedForMerge.Count - before} files");
        }

        foreach (string file in ReadPathList(args.Value("include-file"), project, ctx)) {
            project.SelectedForMerge.Add(file);
        }
    }

    /// <summary>
    /// Reads a text file with one project relative path per line and maps the entries onto scanned files.
    /// Lines starting with # are ignored, entries may also be globs.
    /// </summary>
    public static List<string> ReadPathList(string? listFile, ResolvedProject project, CliContext ctx) {
        if (string.IsNullOrWhiteSpace(listFile)) return new List<string>();

        if (!File.Exists(listFile))
            throw CliException.NotFound($"List file not found: {listFile}");

        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in project.FoundFiles) {
            known[project.Relative(file)] = file;
            known[Path.GetFileName(file)] = file;
        }

        var result = new List<string>();
        foreach (string rawLine in File.ReadAllLines(listFile)) {
            string line = rawLine.Trim().Trim('"').Replace('\\', '/').TrimStart('/');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (known.TryGetValue(line, out string? match)) {
                result.Add(match);
                continue;
            }

            // Fall back to a glob match so lists may contain patterns.
            foreach (string file in project.FoundFiles) {
                if (GlobMatcher.IsMatch(project.Relative(file), line)) result.Add(file);
            }
        }

        ctx.Log.Trace($"List file '{listFile}' resolved to {result.Count} files");
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}