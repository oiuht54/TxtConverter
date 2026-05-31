using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TxtConverter.Core.Logic;

public class FileScanner {
    private readonly List<string> _extensions;
    private readonly HashSet<string> _ignoredFolderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredRelativePaths = new(StringComparer.OrdinalIgnoreCase);
    private string _sourcePath = string.Empty;

    // ГЛОБАЛЬНЫЙ ЧЕРНЫЙ СПИСОК ФАЙЛОВ
    private readonly HashSet<string> _ignoredFiles = new(StringComparer.OrdinalIgnoreCase) {
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "npm-debug.log",
        "yarn-error.log",
        "cargo.lock",
        "desktop-schema.json",
        "poetry.lock",
        "pipfile.lock",
        ".ds_store",
        "thumbs.db"
    };

    public FileScanner(List<string> extensions, List<string> ignoredFolders) {
        // Нормализация расширений файлов
        _extensions = extensions
            .Select(e => e.Trim().TrimStart('.').ToLower())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();

        // Сортировка правил игнорирования папок: глобальные имена vs относительные пути от корня
        foreach (var folder in ignoredFolders) {
            string trimmed = folder.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string normalized = trimmed.Replace('\\', '/');
            if (normalized.Contains('/')) {
                _ignoredRelativePaths.Add(normalized.TrimStart('/').ToLower());
            } else {
                _ignoredFolderNames.Add(normalized.ToLower());
            }
        }

        // Всегда игнорируем папку с результатами конвертации на глобальном уровне
        _ignoredFolderNames.Add(ProjectConstants.OutputDirName.ToLower());
    }

    public Task<List<string>> ScanAsync(string sourcePath) {
        _sourcePath = sourcePath;
        return Task.Run(() => {
            var results = new List<string>();
            var rootDir = new DirectoryInfo(sourcePath);
            if (!rootDir.Exists) return results;
            WalkDirectory(rootDir, results);
            results.Sort();
            return results;
        });
    }

    private void WalkDirectory(DirectoryInfo directory, List<string> results) {
        // 1. Проверяем файлы в текущей директории
        try {
            foreach (var file in directory.EnumerateFiles()) {
                if (IsFileMatch(file.Name)) {
                    results.Add(file.FullName);
                }
            }
        }
        catch (UnauthorizedAccessException) { /* Пропускаем недоступные файлы */ }

        // 2. Рекурсивно переходим в подпапки с учетом правил фильтрации путей
        try {
            foreach (var dir in directory.EnumerateDirectories()) {
                if (ShouldIgnoreDirectory(dir)) continue;

                string dirName = dir.Name.ToLower();
                // Игнорируем стандартные скрытые каталоги, кроме .gitignore
                if (dirName.StartsWith(".") && dirName != ".gitignore") continue;

                WalkDirectory(dir, results);
            }
        }
        catch (UnauthorizedAccessException) { /* Пропускаем недоступные папки */ }
    }

    private bool ShouldIgnoreDirectory(DirectoryInfo dir) {
        string dirName = dir.Name.ToLower();
        // Глобальное имя папки совпадает
        if (_ignoredFolderNames.Contains(dirName)) {
            return true;
        }

        if (string.IsNullOrEmpty(_sourcePath)) {
            return false;
        }

        try {
            // Вычисление относительного пути для точечной проверки
            string relPath = Path.GetRelativePath(_sourcePath, dir.FullName).Replace('\\', '/').ToLower().TrimStart('/');
            if (_ignoredRelativePaths.Contains(relPath)) {
                return true;
            }

            // Проверка, является ли текущая папка подпапкой любого из игнорируемых путей
            foreach (var ignoredRel in _ignoredRelativePaths) {
                string prefix = ignoredRel.EndsWith("/") ? ignoredRel : ignoredRel + "/";
                if (relPath.StartsWith(prefix)) {
                    return true;
                }
            }
        }
        catch {
            return false;
        }

        return false;
    }

    private bool IsFileMatch(string fileName) {
        string lowerName = fileName.ToLower();
        if (_ignoredFiles.Contains(lowerName)) return false;
        if (lowerName.EndsWith(".md")) return true;
        string ext = Path.GetExtension(lowerName).TrimStart('.');
        if (_extensions.Contains(ext)) return true;
        if (_extensions.Contains(lowerName)) return true;
        return false;
    }
}