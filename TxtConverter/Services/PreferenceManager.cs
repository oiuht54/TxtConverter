using System.IO;
using System.Text;
using System.Text.Json;
using TxtConverter.Core;
using TxtConverter.Core.Enums;

namespace TxtConverter.Services;

public class AppSettings
{
    public string Language { get; set; } = ProjectConstants.LangEn;
    public string LastSourceDir { get; set; } = string.Empty;
    public string LastPreset { get; set; } = "Unity Engine";
    public bool GenerateStructure { get; set; } = false;
    public bool CompactMode { get; set; } = true;
    public bool GenerateMerged { get; set; } = true;
    public CompressionLevel Compression { get; set; } = CompressionLevel.Smart;

    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = ProjectConstants.DefaultAiModel;
    public int AiThinkingBudget { get; set; } = ProjectConstants.DefaultThinkingBudget;
    public bool AiThinkingEnabled { get; set; } = true;

    public string InstallationId { get; set; } = string.Empty;
    public bool IsTelemetryEnabled { get; set; } = true;
}

public class PreferenceManager
{
    private static PreferenceManager? _instance;
    public static PreferenceManager Instance => _instance ??= new PreferenceManager();

    private AppSettings _settings;
    private readonly string _settingsPath;

    private PreferenceManager()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(appData, ProjectConstants.AppDataFolderName);
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, ProjectConstants.SettingsFileName);
        _settings = new AppSettings();
    }

    public void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                    // Decode key if present
                    if (!string.IsNullOrEmpty(_settings.AiApiKey))
                    {
                        _settings.AiApiKey = FromBase64(_settings.AiApiKey);
                    }
                }
            }
            catch
            {
                _settings = new AppSettings();
            }
        }

        if (string.IsNullOrEmpty(_settings.InstallationId))
        {
            _settings.InstallationId = Guid.NewGuid().ToString();
            Save();
        }
    }

    public void Save()
    {
        try
        {
            // Simple obfuscation to avoid plain text in JSON (Portability over Security)
            string plainKey = _settings.AiApiKey;
            if (!string.IsNullOrEmpty(plainKey))
            {
                _settings.AiApiKey = ToBase64(plainKey);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsPath, json);

            // Restore plain key in memory
            _settings.AiApiKey = plainKey;
        }
        catch
        {
            // Ignore save errors
        }
    }

    // Removed DPAPI to avoid NuGet dependency on System.Security.Cryptography.ProtectedData
    private string ToBase64(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }
        catch { return plainText; }
    }

    private string FromBase64(string base64Text)
    {
        if (string.IsNullOrEmpty(base64Text)) return base64Text;
        try
        {
            // Check if string is likely base64 before trying
            if (base64Text.Trim().Length % 4 != 0) return base64Text;

            byte[] bytes = Convert.FromBase64String(base64Text);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return base64Text; }
    }

    public string GetLanguage() => _settings.Language;
    public void SetLanguage(string lang) { _settings.Language = lang; Save(); }

    public string GetLastSourceDir() => _settings.LastSourceDir;
    public void SetLastSourceDir(string path) { _settings.LastSourceDir = path; Save(); }

    public string GetLastPreset() => _settings.LastPreset;
    public void SetLastPreset(string preset) { _settings.LastPreset = preset; Save(); }

    public bool GetGenerateStructure() => _settings.GenerateStructure;
    public void SetGenerateStructure(bool val) { _settings.GenerateStructure = val; Save(); }

    public bool GetCompactMode() => _settings.CompactMode;
    public void SetCompactMode(bool val) { _settings.CompactMode = val; Save(); }

    public bool GetGenerateMerged() => _settings.GenerateMerged;
    public void SetGenerateMerged(bool val) { _settings.GenerateMerged = val; Save(); }

    public CompressionLevel GetCompressionLevel() => _settings.Compression;
    public void SetCompressionLevel(CompressionLevel level) { _settings.Compression = level; Save(); }

    public string GetAiApiKey() => _settings.AiApiKey;
    public void SetAiApiKey(string key) { _settings.AiApiKey = key; Save(); }

    public string GetAiModel() => _settings.AiModel;
    public void SetAiModel(string model) { _settings.AiModel = model; Save(); }

    public int GetAiThinkingBudget() => _settings.AiThinkingBudget;
    public void SetAiThinkingBudget(int tokens) { _settings.AiThinkingBudget = tokens; Save(); }

    public bool GetAiThinkingEnabled() => _settings.AiThinkingEnabled;
    public void SetAiThinkingEnabled(bool enabled) { _settings.AiThinkingEnabled = enabled; Save(); }

    public string GetInstallationId() => _settings.InstallationId;

    public bool GetTelemetryEnabled() => _settings.IsTelemetryEnabled;
    public void SetTelemetryEnabled(bool enabled) { _settings.IsTelemetryEnabled = enabled; Save(); }
}