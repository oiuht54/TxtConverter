using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Processing;
using TxtConverter.Core.Logic.Reporting;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic;

/// <summary>
/// Orchestrates the conversion process.
/// Coordinates Scanner -> Processor -> Generators.
/// Embeds structure report inline to avoid separate messy files.
/// </summary>
public class ConversionOrchestrator {
    private readonly string _sourceDirPath;
    private readonly List<string> _filesToProcess;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly List<string> _ignoredFolders;

    // Config
    private readonly bool _genStructure;
    private readonly bool _compactMode;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _genMerged;
    private readonly bool _genPdf;
    private readonly PdfMode _pdfMode;

    // Services
    private readonly FileContentProcessor _processor;

    public ConversionOrchestrator(
        string sourceDirPath,
        List<string> filesToProcess,
        HashSet<string> filesSelectedForMerge,
        List<string> ignoredFolders,
        bool genStructure,
        bool compactMode,
        CompressionLevel compressionLevel,
        bool genMerged,
        bool genPdf,
        PdfMode pdfMode) {
        _sourceDirPath = sourceDirPath;
        _filesToProcess = filesToProcess;
        _filesSelectedForMerge = filesSelectedForMerge;
        _ignoredFolders = ignoredFolders;
        _genStructure = genStructure;
        _compactMode = compactMode;
        _compressionLevel = compressionLevel;
        _genMerged = genMerged;
        _genPdf = genPdf;
        _pdfMode = pdfMode;
        _processor = new FileContentProcessor(_compressionLevel);
    }

    public async Task RunAsync(IProgress<double> progress, IProgress<string> status) {
        await Task.Run(() => {
            status.Report(Loc("task_preparing"));

            // 1. Prepare Output Folder
            string outputDir = Path.Combine(_sourceDirPath, ProjectConstants.OutputDirName);
            PrepareOutputDirectory(outputDir);

            var processedFilesMap = new ConcurrentDictionary<string, string>();
            var inMemoryContents = new ConcurrentDictionary<string, string>();

            // Generate unique names to prevent overwriting of files with same names in different subfolders
            var uniqueNamesMap = GenerateUniqueFileNames(_filesToProcess);
            int total = _filesToProcess.Count;
            int count = 0;

            var lastReportTime = DateTime.MinValue;
            var reportLock = new object();

            // 2. Parallel Processing Files Loop (Saturates SSD I/O queue and leverages all CPU cores)
            Parallel.ForEach(_filesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, sourceFile => {
                string uniqueName = uniqueNamesMap[sourceFile];
                string destFileName = uniqueName.ToLower().EndsWith(".md") ? uniqueName : uniqueName + ".txt";
                string destFile = Path.Combine(outputDir, destFileName);
                string compressedContent;

                try {
                    // Unified Processing Logic (Reads, Normalizes, Compresses)
                    compressedContent = _processor.ReadAndProcess(sourceFile);
                    File.WriteAllText(destFile, compressedContent, Encoding.UTF8);
                }
                catch (Exception ex) {
                    // Fallback to simple copy if processing fails
                    try {
                        File.Copy(sourceFile, destFile, true);
                        compressedContent = File.ReadAllText(destFile, Encoding.UTF8);
                    }
                    catch {
                        compressedContent = string.Empty;
                    }
                    System.Diagnostics.Debug.WriteLine($"Processing error for {Path.GetFileName(sourceFile)}: {ex.Message}");
                }

                processedFilesMap[sourceFile] = destFile;
                inMemoryContents[sourceFile] = compressedContent;

                int current = Interlocked.Increment(ref count);

                // Throttled UI reporting to prevent WPF Dispatcher queue saturation
                lock (reportLock) {
                    var now = DateTime.UtcNow;
                    if ((now - lastReportTime).TotalMilliseconds >= 50 || current == total) {
                        lastReportTime = now;
                        progress.Report((double)current / total);
                        status.Report(string.Format(Loc("task_processing"), Path.GetFileName(sourceFile)));
                    }
                }
            });

            var finalFilesMap = new Dictionary<string, string>(processedFilesMap);
            var finalContentsMap = new Dictionary<string, string>(inMemoryContents);

            // 3. Generate Structure Report
            string structureContent = "";
            if (_genStructure) {
                status.Report(Loc("task_generating_structure"));
                var structureGen = new StructureReportGenerator(
                    _sourceDirPath,
                    finalFilesMap.Keys.ToHashSet(),
                    _filesSelectedForMerge,
                    _ignoredFolders,
                    _compressionLevel,
                    _compactMode
                );
                structureContent = structureGen.Generate();
            }

            // 4. Generate Merged File using In-Memory Content (zero redundant disk re-reads)
            string projectName = Path.GetFileName(_sourceDirPath);
            if (_genMerged && finalFilesMap.Count > 0) {
                status.Report(Loc("task_merging"));
                string outputFileName = "_" + projectName + ProjectConstants.MergedFileSuffix;
                string destPath = Path.Combine(outputDir, outputFileName);
                var mergedGen = new MergedFileGenerator(
                    _sourceDirPath,
                    finalFilesMap,
                    _filesSelectedForMerge,
                    _compressionLevel,
                    structureContent,
                    finalContentsMap
                );
                mergedGen.Generate(destPath);
            }

            // 5. Generate PDF Report using In-Memory Content (zero redundant disk re-reads)
            if (_genPdf && finalFilesMap.Count > 0) {
                status.Report(Loc("task_pdf"));
                string pdfName = "_" + projectName + "_Report.pdf";
                string pdfPath = Path.Combine(outputDir, pdfName);
                try {
                    var pdfGen = new PdfReportGenerator(
                        _sourceDirPath,
                        structureContent,
                        finalFilesMap,
                        _filesSelectedForMerge,
                        _pdfMode,
                        finalContentsMap
                    );
                    pdfGen.Generate(pdfPath);
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"PDF Error: {ex.Message}");
                }
            }

            status.Report(Loc("task_done"));
            progress.Report(1.0);
        });
    }

    private Dictionary<string, string> GenerateUniqueFileNames(List<string> sourceFiles) {
        var result = new Dictionary<string, string>();
        var fileNameGroups = sourceFiles.GroupBy(f => Path.GetFileName(f)).ToList();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in fileNameGroups) {
            if (group.Count() == 1) {
                string destName = group.Key;
                int counter = 1;
                while (usedNames.Contains(destName)) {
                    destName = $"{Path.GetFileNameWithoutExtension(group.Key)}_{counter}{Path.GetExtension(group.Key)}";
                    counter++;
                }
                result[group.First()] = destName;
                usedNames.Add(destName);
            }
            else {
                foreach (var file in group) {
                    string baseName = group.Key;
                    string dir = Path.GetDirectoryName(file) ?? string.Empty;
                    string parentFolder = Path.GetFileName(dir);
                    string destName = string.IsNullOrEmpty(parentFolder) ? baseName : $"{parentFolder}_{baseName}";
                    int counter = 1;
                    string finalName = destName;
                    while (usedNames.Contains(finalName)) {
                        finalName = $"{Path.GetFileNameWithoutExtension(destName)}_{counter}{Path.GetExtension(destName)}";
                        counter++;
                    }
                    result[file] = finalName;
                    usedNames.Add(finalName);
                }
            }
        }
        return result;
    }

    private void PrepareOutputDirectory(string path) {
        if (Directory.Exists(path)) {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.GetFiles()) file.Delete();
            foreach (var sub in dir.GetDirectories()) sub.Delete(true);
        }
        else {
            Directory.CreateDirectory(path);
        }
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}