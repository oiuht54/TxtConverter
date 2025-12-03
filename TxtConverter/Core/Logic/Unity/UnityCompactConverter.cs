using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Unity;

public class UnityCompactConverter
{
    // Regex для заголовка объекта: --- !u!1 &123456
    // Group 1: ClassID (1=GameObject, 4=Transform, etc)
    // Group 2: FileID (Unique ID in file)
    private static readonly Regex HeaderRegex = new(@"^--- !u!(\d+) &(\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

    // Извлечение полей ссылок: m_Father: {fileID: 1234}
    private static readonly Regex FileIdRegex = new(@"fileID:\s*(-?\d+)", RegexOptions.Compiled);

    // Извлечение простых свойств: m_Name: MyObject
    private static readonly Regex PropRegex = new(@"^\s*(\w+):\s*(.+)$", RegexOptions.Compiled);

    private readonly Dictionary<string, UnityObject> _objects = new();
    private readonly StringBuilder _output = new();

    public static string Convert(string content)
    {
        return new UnityCompactConverter().Process(content);
    }

    private string Process(string content)
    {
        // 1. Разбиваем файл на блоки объектов
        // Unity YAML разделяет объекты строкой "--- !u!ClassID &FileID"
        // Используем Split, но сохраняем разделители или ищем совпадения

        var matches = HeaderRegex.Matches(content);
        if (matches.Count == 0) return content; // Не похоже на YAML Unity

        int start = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int nextIndex = (i == matches.Count - 1) ? content.Length : matches[i + 1].Index;

            string blockContent = content.Substring(match.Index, nextIndex - match.Index);
            ParseBlock(match.Groups[1].Value, match.Groups[2].Value, blockContent);
        }

        // 2. Строим иерархию
        // Unity хранит плоский список. Связи:
        // GameObject -> хранит список Component ID
        // Transform (один из компонентов) -> хранит Father ID (Transform) и Children IDs (Transforms)

        var rootTransforms = new List<UnityObject>();

        foreach (var obj in _objects.Values)
        {
            if (obj.ClassId == "4" || obj.ClassId == "224") // Transform или RectTransform
            {
                if (string.IsNullOrEmpty(obj.ParentTransformId) || obj.ParentTransformId == "0")
                {
                    rootTransforms.Add(obj);
                }
                else
                {
                    if (_objects.TryGetValue(obj.ParentTransformId, out var parent))
                    {
                        parent.Children.Add(obj);
                    }
                }
            }
        }

        // 3. Выводим дерево
        if (rootTransforms.Count == 0)
        {
            // Если иерархию не нашли, выводим как есть (возможно это не сцена, а ассет настроек)
            return "(Unity YAML Content: Structured hierarchy not found, returning summary)\nObjects found: " + _objects.Count;
        }

        foreach (var root in rootTransforms)
        {
            PrintTree(root, "");
        }

        return _output.ToString().Trim();
    }

    private void ParseBlock(string classId, string fileId, string content)
    {
        // Игнорируем технический мусор Unity
        // 157: LightmapSettings, 196: NavMeshSettings, 104: RenderSettings, 29: OcclusionCullingSettings
        if (classId == "157" || classId == "196" || classId == "104" || classId == "29" || classId == "850595691")
            return;

        var obj = new UnityObject { ClassId = classId, FileId = fileId };

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("m_Name:"))
                obj.Name = GetValue(trimmed);

            else if (trimmed.StartsWith("m_GameObject:"))
                obj.GameObjectId = ExtractFileId(trimmed);

            else if (trimmed.StartsWith("m_Father:"))
                obj.ParentTransformId = ExtractFileId(trimmed);

            else if (trimmed.StartsWith("m_Component:"))
                continue; // Начало списка

            else if (trimmed.StartsWith("- component:"))
                obj.ComponentIds.Add(ExtractFileId(trimmed));

            else if (classId == "114") // MonoBehaviour (Скрипт)
            {
                // Собираем публичные поля скрипта, игнорируя служебные m_
                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string key = trimmed.Substring(0, colon).Trim();
                    string val = trimmed.Substring(colon + 1).Trim();

                    if (!key.StartsWith("m_") && key != "serializedVersion")
                    {
                        obj.Properties[key] = val;
                    }
                }
            }
        }

        _objects[fileId] = obj;
    }

    private void PrintTree(UnityObject transform, string indent)
    {
        // Transform ссылается на GameObject. Нам нужно имя GameObject.
        string gameObjectName = "Unknown";
        UnityObject? go = null;

        if (!string.IsNullOrEmpty(transform.GameObjectId) && _objects.TryGetValue(transform.GameObjectId, out go))
        {
            gameObjectName = go.Name;
        }

        _output.Append(indent).Append(gameObjectName);

        // Собираем компоненты на этом GameObject
        if (go != null && go.ComponentIds.Count > 0)
        {
            var comps = new List<string>();
            foreach (var compId in go.ComponentIds)
            {
                if (compId == transform.FileId) continue; // Не выводим Transform, он и так понятен по структуре

                if (_objects.TryGetValue(compId, out var comp))
                {
                    string compName = GetComponentName(comp.ClassId);
                    if (comp.ClassId == "114") // Script
                    {
                        // Если есть свойства, добавим их
                        if (comp.Properties.Count > 0)
                        {
                            var props = comp.Properties.Select(k => $"{k.Key}:{ShortenVal(k.Value)}");
                            compName += $"({string.Join(", ", props)})";
                        }
                    }
                    comps.Add(compName);
                }
            }

            if (comps.Count > 0)
            {
                _output.Append(" [").Append(string.Join(", ", comps)).Append("]");
            }
        }

        _output.Append('\n');

        foreach (var child in transform.Children)
        {
            PrintTree(child, indent + "  ");
        }
    }

    private string GetComponentName(string classId)
    {
        return classId switch
        {
            "4" => "Transform",
            "224" => "RectTransform",
            "20" => "Camera",
            "81" => "AudioListener",
            "114" => "Script",
            "212" => "SpriteRenderer",
            "23" => "MeshRenderer",
            "33" => "MeshFilter",
            "65" => "BoxCollider",
            "135" => "SphereCollider",
            "136" => "CapsuleCollider",
            "64" => "MeshCollider",
            "50" => "Rigidbody",
            "54" => "Rigidbody2D",
            "61" => "BoxCollider2D",
            "58" => "CircleCollider2D",
            "223" => "Canvas",
            "222" => "CanvasRenderer",
            "108" => "Light",
            _ => $"Comp#{classId}"
        };
    }

    private string ExtractFileId(string line)
    {
        var m = FileIdRegex.Match(line);
        return m.Success ? m.Groups[1].Value : "";
    }

    private string GetValue(string line)
    {
        int idx = line.IndexOf(':');
        return idx == -1 ? "" : line.Substring(idx + 1).Trim();
    }

    private string ShortenVal(string val)
    {
        if (val.Length > 20) return val.Substring(0, 17) + "...";
        return val;
    }

    private class UnityObject
    {
        public string FileId { get; set; } = "";
        public string ClassId { get; set; } = "";
        public string Name { get; set; } = ""; // Из m_Name
        public string GameObjectId { get; set; } = ""; // Для компонентов: к какому GO привязан
        public string ParentTransformId { get; set; } = ""; // Для Transform: кто родитель

        public List<string> ComponentIds { get; set; } = new(); // Для GameObject: список компонентов
        public List<UnityObject> Children { get; set; } = new(); // Восстановленные дети
        public Dictionary<string, string> Properties { get; set; } = new(); // Для скриптов
    }
}