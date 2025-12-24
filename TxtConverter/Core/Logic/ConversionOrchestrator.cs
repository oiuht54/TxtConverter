using System.IO;
using TxtConverter.Core.Enums;
using TxtConverter.Core.Logic.Processing;
using TxtConverter.Core.Logic.Reporting;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic;

/// <summary>
/// Orchestrates the conversion process.
/// Replaces the old monolithic "Converter.cs".
/// Coordinates Scanner -> Processor -> Generators.
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
    private readonly bool _genPdf; // NEW
    private readonly bool _pdfCompactMode; // NEW

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
        bool genPdf = false, // NEW
        bool pdfCompactMode = false) { // NEW

        _sourceDirPath = sourceDirPath;
        _filesToProcess = filesToProcess;
        _filesSelectedForMerge = filesSelectedForMerge;
        _ignoredFolders = ignoredFolders;
        _genStructure = genStructure;
        _compactMode = compactMode;
        _compressionLevel = compressionLevel;
        _genMerged = genMerged;
        _genPdf = genPdf;
        _pdfCompactMode = pdfCompactMode;

        _processor = new FileContentProcessor(_compressionLevel);
    }

    public async Task RunAsync(IProgress<double> progress, IProgress<string> status) {
        await Task.Run(() => {
            status.Report(Loc("task_preparing"));

            // 1. Prepare Output
            string outputDir = Path.Combine(_sourceDirPath, ProjectConstants.OutputDirName);
            PrepareOutputDirectory(outputDir);

            var processedFilesMap = new Dictionary<string, string>(); // SourcePath -> DestPath inside _ConvertedToTxt
            int total = _filesToProcess.Count;
            int count = 0;

            // 2. Process Files Loop
            foreach (var sourceFile in _filesToProcess) {
                count++;
                progress.Report((double)count / total);
                string fileName = Path.GetFileName(sourceFile);
                status.Report(string.Format(Loc("task_processing"), fileName));

                // Determine destination path
                string destFileName = fileName.ToLower().EndsWith(".md") ? fileName : fileName + ".txt";
                string destFile = Path.Combine(outputDir, destFileName);

                try {
                    // Unified Processing Logic (Reads, Normalizes, Compresses)
                    string compressedContent = _processor.ReadAndProcess(sourceFile);
                    File.WriteAllText(destFile, compressedContent, System.Text.Encoding.UTF8);
                }
                catch (Exception ex) {
                    // Fallback: simple copy if processing fails completely
                    try { File.Copy(sourceFile, destFile, true); } catch { }
                    System.Diagnostics.Debug.WriteLine($"Processing error for {fileName}: {ex.Message}");
                }

                processedFilesMap[sourceFile] = destFile;
            }

            // 3. Generate Structure Report (Required for PDF too)
            string structureContent = "";
            if (_genStructure || _genPdf) {
                status.Report(Loc("task_generating_structure"));
                var structureGen = new StructureReportGenerator(
                    _sourceDirPath,
                    processedFilesMap.Keys.ToHashSet(), // Set of successfully processed source paths
                    _filesSelectedForMerge,
                    _ignoredFolders,
                    _compressionLevel,
                    _compactMode
                );
                // Modified to return string
                structureContent = structureGen.Generate(outputDir);
            }

            // 4. Generate Merged File
            string projectName = Path.GetFileName(_sourceDirPath);

            if (_genMerged && processedFilesMap.Count > 0) {
                status.Report(Loc("task_merging"));
                string outputFileName = "_" + projectName + ProjectConstants.MergedFileSuffix;
                string destPath = Path.Combine(outputDir, outputFileName);

                var mergedGen = new MergedFileGenerator(
                    projectName,
                    processedFilesMap,
                    _filesSelectedForMerge,
                    _compressionLevel
                );
                mergedGen.Generate(destPath);
            }

            // 5. Generate PDF Report (NEW)
            if (_genPdf && processedFilesMap.Count > 0) {
                status.Report(Loc("task_pdf"));
                string pdfName = "_" + projectName + "_Report.pdf";
                string pdfPath = Path.Combine(outputDir, pdfName);

                try {
                    var pdfGen = new PdfReportGenerator(
                        projectName,
                        structureContent,
                        processedFilesMap,
                        _filesSelectedForMerge,
                        _pdfCompactMode // Pass flag
                    );
                    pdfGen.Generate(pdfPath);
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"PDF Error: {ex.Message}");
                    // Non-critical, just log
                }
            }

            status.Report(Loc("task_done"));
            progress.Report(1.0);
        });
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