using System.IO;
using System.Security.Cryptography;
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

    // AI Settings
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = ProjectConstants.DefaultAiModel;
    public int AiThinkingBudget { get; set; } = ProjectConstants.DefaultThinkingBudget;
    public bool AiThinkingEnabled { get; set; } = true;

    // Telemetry Settings
    public string InstallationId { get; set; } = string.Empty;
    public bool IsTelemetryEnabled { get; set; } = true;
}

public class PreferenceManager
{
    private static PreferenceManager? _instance;
    public static PreferenceManager Instance => _instance ??= new PreferenceManager();

    private AppSettings _settings;
    private readonly string _settingsPath;
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("Tartarus_TxtConverter_Secure_Key");

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
                    // Decrypt API Key
                    if (!string.IsNullOrEmpty(_settings.AiApiKey))
                    {
                        try
                        {
                            _settings.AiApiKey = DecryptString(_settings.AiApiKey);
                        }
                        catch { /* Ignore decryption errors */ }
                    }
                }
            }
            catch
            {
                _settings = new AppSettings();
            }
        }

        // Ensure Installation ID exists (First Run Logic)
        if (string.IsNullOrEmpty(_settings.InstallationId))
        {
            _settings.InstallationId = Guid.NewGuid().ToString();
            Save(); // Save immediately to persist ID
        }
    }

    public void Save()
    {
        try
        {
            // Temporary encrypt sensitive data
            string plainKey = _settings.AiApiKey;
            if (!string.IsNullOrEmpty(plainKey))
            {
                _settings.AiApiKey = EncryptString(plainKey);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsPath, json);

            // Restore plain text for runtime usage
            _settings.AiApiKey = plainKey;
        }
        catch
        {
            // Silently fail on save errors
        }
    }

    private string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, s_entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string DecryptString(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;
        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, s_entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    // --- Getters / Setters ---

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

    // Telemetry
    public string GetInstallationId() => _settings.InstallationId;
    public bool GetTelemetryEnabled() => _settings.IsTelemetryEnabled;
    public void SetTelemetryEnabled(bool enabled) { _settings.IsTelemetryEnabled = enabled; Save(); }
}