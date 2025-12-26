using System.IO;

namespace TxtConverter.Core.Logic;

public class FileScanner {
    private readonly List<string> _extensions;
    private readonly HashSet<string> _ignoredFolders;

    // HARDCODED BLACKLIST
    // Файлы, которые являются "шумом" для LLM, даже если их расширение (например .json) разрешено.
    private readonly HashSet<string> _ignoredFiles = new() {
        // Node / JS
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "npm-debug.log",
        "yarn-error.log",
        
        // Rust / Tauri
        "cargo.lock",
        "desktop-schema.json", // Автогенерируемая схема Tauri, о которой вы говорили
        
        // Python
        "poetry.lock",
        "pipfile.lock",
        
        // System
        ".ds_store",
        "thumbs.db"
    };

    public FileScanner(List<string> extensions, List<string> ignoredFolders) {
        // Нормализация расширений: убираем точку, приводим к нижнему регистру
        _extensions = extensions
            .Select(e => e.Trim().TrimStart('.').ToLower())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();

        // Нормализация папок
        _ignoredFolders = new HashSet<string>(
            ignoredFolders.Select(f => f.Trim().ToLower())
        );

        // Всегда игнорируем папку с результатами
        _ignoredFolders.Add(ProjectConstants.OutputDirName.ToLower());
    }

    public Task<List<string>> ScanAsync(string sourcePath) {
        return Task.Run(() => {
            var results = new List<string>();
            var rootDir = new DirectoryInfo(sourcePath);

            if (!rootDir.Exists) return results;

            WalkDirectory(rootDir, results);

            // Сортируем для красоты
            results.Sort();
            return results;
        });
    }

    private void WalkDirectory(DirectoryInfo directory, List<string> results) {
        // 1. Проверяем файлы в текущей папке
        try {
            foreach (var file in directory.EnumerateFiles()) {
                if (IsFileMatch(file.Name)) {
                    results.Add(file.FullName);
                }
            }
        }
        catch (UnauthorizedAccessException) { /* Ignore */ }

        // 2. Рекурсивно идем в подпапки (если они не игнорируемые)
        try {
            foreach (var dir in directory.EnumerateDirectories()) {
                string dirName = dir.Name.ToLower();

                // Пропускаем игнорируемые и скрытые (кроме .gitignore, если вдруг папка так называется, хотя это файл)
                if (_ignoredFolders.Contains(dirName)) continue;
                
                // Игнорируем скрытые папки (.git, .vscode и т.д.), но разрешаем src-tauri и т.п.
                // Логика: если начинается с точки и не является .gitignore (редкий кейс для папки, но оставим для безопасности)
                if (dirName.StartsWith(".") && dirName != ".gitignore") continue;

                WalkDirectory(dir, results);
            }
        }
        catch (UnauthorizedAccessException) { /* Ignore */ }
    }

    private bool IsFileMatch(string fileName) {
        string lowerName = fileName.ToLower();

        // 1. CRITICAL CHECK: Global Blacklist
        // Сначала проверяем, не находится ли файл в черном списке
        if (_ignoredFiles.Contains(lowerName)) return false;

        // 2. Всегда включаем MD (документация полезна для контекста)
        if (lowerName.EndsWith(".md")) return true;

        // 3. Проверка расширения
        string ext = Path.GetExtension(lowerName).TrimStart('.');

        // Если расширение есть в списке
        if (_extensions.Contains(ext)) return true;

        // Или если имя файла целиком в списке (например "dockerfile", "makefile", "license")
        if (_extensions.Contains(lowerName)) return true;

        return false;
    }
}