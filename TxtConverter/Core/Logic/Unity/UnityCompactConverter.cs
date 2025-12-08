using System.Text;
using System.Text.RegularExpressions;

namespace TxtConverter.Core.Logic.Unity;

public class UnityCompactConverter {
    private static readonly Regex HeaderRegex = new(@"^--- !u!(\d+) &(\d+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex FileIdRegex = new(@"fileID:\s*(-?\d+)", RegexOptions.Compiled);
    
    // Unused regex removed if not needed, or kept for future
    // private static readonly Regex PropRegex = new(@"^\s*(\w+):\s*(.+)$", RegexOptions.Compiled);

    private readonly Dictionary<string, UnityObject> _objects = new();
    private readonly StringBuilder _output = new();

    public static string Convert(string content) {
        return new UnityCompactConverter().Process(content);
    }

    private string Process(string content) {
        var matches = HeaderRegex.Matches(content);
        if (matches.Count == 0) return content;

        // Исправлено: Удалена неиспользуемая переменная 'start'
        for (int i = 0; i < matches.Count; i++) {
            var match = matches[i];
            int nextIndex = (i == matches.Count - 1) ? content.Length : matches[i + 1].Index;
            
            string blockContent = content.Substring(match.Index, nextIndex - match.Index);
            ParseBlock(match.Groups[1].Value, match.Groups[2].Value, blockContent);
        }

        var rootTransforms = new List<UnityObject>();
        foreach (var obj in _objects.Values) {
            // Transform (4) or RectTransform (224)
            if (obj.ClassId == "4" || obj.ClassId == "224") { 
                if (string.IsNullOrEmpty(obj.ParentTransformId) || obj.ParentTransformId == "0") {
                    rootTransforms.Add(obj);
                }
                else {
                    if (_objects.TryGetValue(obj.ParentTransformId, out var parent)) {
                        parent.Children.Add(obj);
                    }
                }
            }
        }

        if (rootTransforms.Count == 0) {
            return "(Unity YAML Content: Structured hierarchy not found, returning summary)\nObjects found: " + _objects.Count;
        }

        foreach (var root in rootTransforms) {
            PrintTree(root, "");
        }

        return _output.ToString().Trim();
    }

    private void ParseBlock(string classId, string fileId, string content) {
        // Skip common heavy assets: Mesh(43), Material(21), Texture2D(28), AnimationClip(74), Avatar(9000000)
        // Kept lightweight ones.
        if (classId == "157" || classId == "196" || classId == "104" || classId == "29" || classId == "850595691") 
            return;

        var obj = new UnityObject { ClassId = classId, FileId = fileId };
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines) {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("m_Name:")) 
                obj.Name = GetValue(trimmed);
            else if (trimmed.StartsWith("m_GameObject:")) 
                obj.GameObjectId = ExtractFileId(trimmed);
            else if (trimmed.StartsWith("m_Father:")) 
                obj.ParentTransformId = ExtractFileId(trimmed);
            else if (trimmed.StartsWith("m_Component:")) 
                continue;
            else if (trimmed.StartsWith("- component:")) 
                obj.ComponentIds.Add(ExtractFileId(trimmed));
            else if (classId == "114") { // MonoBehaviour
                int colon = trimmed.IndexOf(':');
                if (colon > 0) {
                    string key = trimmed.Substring(0, colon).Trim();
                    string val = trimmed.Substring(colon + 1).Trim();
                    if (!key.StartsWith("m_") && key != "serializedVersion") {
                        obj.Properties[key] = val;
                    }
                }
            }
        }
        _objects[fileId] = obj;
    }

    private void PrintTree(UnityObject transform, string indent) {
        string gameObjectName = "Unknown";
        UnityObject? go = null;

        if (!string.IsNullOrEmpty(transform.GameObjectId) && _objects.TryGetValue(transform.GameObjectId, out go)) {
            gameObjectName = go.Name;
        }

        _output.Append(indent).Append(gameObjectName);

        if (go != null && go.ComponentIds.Count > 0) {
            var comps = new List<string>();
            foreach (var compId in go.ComponentIds) {
                if (compId == transform.FileId) continue;
                if (_objects.TryGetValue(compId, out var comp)) {
                    string compName = GetComponentName(comp.ClassId);
                    if (comp.ClassId == "114") { // Script
                        if (comp.Properties.Count > 0) {
                            var props = comp.Properties.Select(k => $"{k.Key}:{ShortenVal(k.Value)}");
                            compName += $"({string.Join(", ", props)})";
                        }
                    }
                    comps.Add(compName);
                }
            }
            if (comps.Count > 0) {
                _output.Append(" [").Append(string.Join(", ", comps)).Append("]");
            }
        }
        _output.Append('\n');

        foreach (var child in transform.Children) {
            PrintTree(child, indent + "  ");
        }
    }

    private string GetComponentName(string classId) {
        return classId switch {
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

    private string ExtractFileId(string line) {
        var m = FileIdRegex.Match(line);
        return m.Success ? m.Groups[1].Value : "";
    }

    private string GetValue(string line) {
        int idx = line.IndexOf(':');
        return idx == -1 ? "" : line.Substring(idx + 1).Trim();
    }

    private string ShortenVal(string val) {
        if (val.Length > 20) return val.Substring(0, 17) + "...";
        return val;
    }

    private class UnityObject {
        public string FileId { get; set; } = "";
        public string ClassId { get; set; } = "";
        public string Name { get; set; } = ""; 
        public string GameObjectId { get; set; } = ""; 
        public string ParentTransformId { get; set; } = ""; 
        public List<string> ComponentIds { get; set; } = new(); 
        public List<UnityObject> Children { get; set; } = new(); 
        public Dictionary<string, string> Properties { get; set; } = new(); 
    }
}