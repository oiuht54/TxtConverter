using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Csharp;

public class CsCompactConverter
{
    private static readonly Regex BlockCommentRegex = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);

    public static string Convert(string content)
    {
        // 1. Удаляем блочные комментарии
        content = BlockCommentRegex.Replace(content, "");

        // 2. Разбиваем на строки
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(content.Length);

        string? previousLine = null;

        foreach (var rawLine in lines)
        {
            // 3. Убираем отступы (ведущие и хвостовые пробелы)
            string line = rawLine.Trim();

            // 4. Пропускаем пустые строки и строки с одними комментариями
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            // 5. Логика переноса скобки "{"
            // Если текущая строка это просто "{", мы добавляем её к предыдущей
            if (line == "{")
            {
                if (previousLine != null)
                {
                    // Дописываем { к буферу (в sb уже записан previousLine без {)
                    sb.Append(" {");
                    // Обновляем "виртуальную" предыдущую строку для контекста следующей итерации (хоть она и не используется)
                    previousLine += " {";
                }
                else
                {
                    // Если это самая первая строка файла (редко, но бывает)
                    sb.Append("{");
                    previousLine = "{";
                }
            }
            else
            {
                // Если это не первая строка в файле, добавляем перенос перед новой строкой
                if (sb.Length > 0) sb.Append('\n');

                sb.Append(line);
                previousLine = line;
            }
        }

        return sb.ToString();
    }
}