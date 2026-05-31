using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TxtConverter.Core.Logic.Processing;

/// <summary>
/// Фильтрует и определяет, подходит ли файл под правила автоматического исключения (Stubs).
/// Поддерживает как простые имена файлов, так и относительные пути к файлам и папкам от корня проекта.
/// </summary>
public class ExclusionMatcher {
    private readonly HashSet<string> _globalFilenames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _relativeRules = new();

    public ExclusionMatcher(string exclusionsCsv) {
        if (string.IsNullOrWhiteSpace(exclusionsCsv)) {
            return;
        }

        var rules = exclusionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(r => r.Trim())
                                  .Where(r => !string.IsNullOrEmpty(r));

        foreach (var rule in rules) {
            string normalized = rule.Replace('\\', '/');
            if (normalized.Contains('/')) {
                // Удаляем ведущие слэши для обеспечения согласованности относительных путей
                _relativeRules.Add(normalized.TrimStart('/').ToLower());
            } else {
                _globalFilenames.Add(normalized.ToLower());
            }
        }
    }

    /// <summary>
    /// Проверяет, должен ли файл быть исключен из контекста (переведен в Stub).
    /// </summary>
    public bool IsExcluded(string filePath, string rootPath) {
        string filename = Path.GetFileName(filePath).ToLower();
        if (_globalFilenames.Contains(filename)) {
            return true;
        }

        string relPath;
        try {
            relPath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/').ToLower().TrimStart('/');
        }
        catch {
            return false;
        }

        foreach (var rule in _relativeRules) {
            // Точное совпадение относительного пути до файла
            if (relPath == rule) {
                return true;
            }

            // Проверка, находится ли файл внутри исключенной папки
            string folderPrefix = rule.EndsWith("/") ? rule : rule + "/";
            if (relPath.StartsWith(folderPrefix)) {
                return true;
            }
        }

        return false;
    }
}