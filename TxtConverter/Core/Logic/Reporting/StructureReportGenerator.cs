using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

public class StructureReportGenerator {
    private readonly string _rootPath;
    private readonly HashSet<string> _processedFiles;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly CompressionLevel _compressionLevel;
    private readonly bool _compactMode;

    public StructureReportGenerator(
        string rootPath,
        HashSet<string> processedFiles,
        HashSet<string> filesSelectedForMerge,
        List<string> ignoredFolders,
        CompressionLevel compressionLevel,
        bool compactMode) {
        _rootPath = rootPath;
        _processedFiles = processedFiles;
        _filesSelectedForMerge = filesSelectedForMerge;
        _compressionLevel = compressionLevel;
        _compactMode = compactMode;
    }

    /// <summary>
    /// Генерирует структуру проекта, используя неразрывные пробелы для каждого файла.
    /// Это предотвращает перенос имени файла по частям на новую строку.
    /// </summary>
    public string Generate() {
        var sb = new StringBuilder();
        
        // 1. Заголовки
        sb.AppendLine(Loc("report_structure_header"));
        sb.AppendLine(string.Format(Loc("report_generated_date"), DateTime.Now));
        sb.AppendLine();

        // 2. Легенда (Динамическая локализация на основе выбранного в интерфейсе языка)
        sb.AppendLine(Loc("report_legend_title"));
        sb.AppendLine(" " + Loc("report_legend_full"));
        sb.AppendLine(" " + Loc("report_legend_excluded"));
        sb.AppendLine();

        // 3. Сортировка по относительному пути
        var sortedRelativePaths = _processedFiles
            .Select(f => Path.GetRelativePath(_rootPath, f).Replace("\\", "/"))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sortedRelativePaths.Count == 0) {
            sb.AppendLine("[No files match active preset / Нет файлов по выбранному пресету]");
            return sb.ToString();
        }

        var items = new List<string>();
        for (int i = 0; i < sortedRelativePaths.Count; i++) {
            string absolutePath = Path.GetFullPath(Path.Combine(_rootPath, sortedRelativePaths[i]));
            bool isFull = _filesSelectedForMerge.Contains(absolutePath);
            string statusTag = isFull ? "[F]" : "[E]";
            
            // Использование \u00A0 (неразрывный пробел) гарантирует, что элемент перенесется целиком
            string indexString = (i + 1).ToString();
            string item = $"{indexString}.\u00A0{sortedRelativePaths[i]}\u00A0{statusTag}";
            items.Add(item);
        }

        // Объединяем элементы четырьмя стандартными пробелами.
        // Перенос в PDF и текстовых редакторах будет происходить строго по этим границам.
        sb.AppendLine(string.Join("    ", items));
        sb.AppendLine();
        
        return sb.ToString();
    }

    private string Loc(string key) => LanguageManager.Instance.GetString(key);
}