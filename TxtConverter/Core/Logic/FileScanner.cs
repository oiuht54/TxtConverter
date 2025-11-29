using System.IO;

namespace TxtConverter.Core.Logic;

public class FileScanner
{
    private readonly List<string> _extensions;
    private readonly HashSet<string> _ignoredFolders;

    public FileScanner(List<string> extensions, List<string> ignoredFolders)
    {
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

    public Task<List<string>> ScanAsync(string sourcePath)
    {
        return Task.Run(() =>
        {
            var results = new List<string>();
            var rootDir = new DirectoryInfo(sourcePath);

            if (!rootDir.Exists) return results;

            WalkDirectory(rootDir, results);

            // Сортируем для красоты
            results.Sort();
            return results;
        });
    }

    private void WalkDirectory(DirectoryInfo directory, List<string> results)
    {
        // 1. Проверяем файлы в текущей папке
        try
        {
            foreach (var file in directory.EnumerateFiles())
            {
                if (IsFileMatch(file.Name))
                {
                    results.Add(file.FullName);
                }
            }
        }
        catch (UnauthorizedAccessException) { /* Ignore */ }

        // 2. Рекурсивно идем в подпапки (если они не игнорируемые)
        try
        {
            foreach (var dir in directory.EnumerateDirectories())
            {
                string dirName = dir.Name.ToLower();

                // Пропускаем игнорируемые и скрытые (кроме .gitignore)
                if (_ignoredFolders.Contains(dirName)) continue;
                if (dirName.StartsWith(".") && dirName != ".gitignore") continue;

                WalkDirectory(dir, results);
            }
        }
        catch (UnauthorizedAccessException) { /* Ignore */ }
    }

    private bool IsFileMatch(string fileName)
    {
        string lowerName = fileName.ToLower();
        if (lowerName.EndsWith(".md")) return true; // Всегда включаем MD (обычно это документация)

        // Проверка расширения
        string ext = Path.GetExtension(lowerName).TrimStart('.');

        // Если расширение есть в списке
        if (_extensions.Contains(ext)) return true;

        // Или если имя файла целиком в списке (например "dockerfile")
        if (_extensions.Contains(lowerName)) return true;

        return false;
    }
}