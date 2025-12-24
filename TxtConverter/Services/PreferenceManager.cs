using System.IO;
using System.Text;
using System.Text.Json;
using TxtConverter.Core;
using TxtConverter.Core.Enums;

namespace TxtConverter.Services;

public class AppSettings {
    public string Language { get; set; } = ProjectConstants.LangEn;
    public string LastSourceDir { get; set; } = string.Empty;
    public string LastPreset { get; set; } = "Unity Engine";
    public bool GenerateStructure { get; set; } = false;
    public bool CompactMode { get; set; } = true;
    public bool GenerateMerged { get; set; } = true;
    public bool GeneratePdf { get; set; } = true; // New Default
    public bool PdfCompactMode { get; set; } = false; // New Default

    public CompressionLevel Compression { get; set; } = CompressionLevel.Smart;

    // AI Common
    public AiProvider AiProvider { get; set; } = AiProvider.GoogleGemini;
    public bool AiThinkingEnabled { get; set; } = true;
    public int AiThinkingBudget { get; set; } = ProjectConstants.DefaultThinkingBudget;

    // Gemini Specific
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = ProjectConstants.DefaultGeminiModel;

    // Nvidia Specific
    public string NvidiaApiKey { get; set; } = string.Empty;
    public string NvidiaModel { get; set; } = ProjectConstants.DefaultNvidiaModel;
    public int NvidiaMaxTokens { get; set; } = 4096;
    public double NvidiaTemperature { get; set; } = 0.5;
    public double NvidiaTopP { get; set; } = 0.7;
    public bool NvidiaReasoningEnabled { get; set; } = false;

    public string InstallationId { get; set; } = string.Empty;
    public bool IsTelemetryEnabled { get; set; } = true;
}

public class PreferenceManager {
    private static PreferenceManager? _instance;
    public static PreferenceManager Instance => _instance ??= new PreferenceManager();

    private AppSettings _settings;
    private readonly string _settingsPath;

    private PreferenceManager() {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(appData, ProjectConstants.AppDataFolderName);
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, ProjectConstants.SettingsFileName);
        _settings = new AppSettings();
    }

    public void Load() {
        if (File.Exists(_settingsPath)) {
            try {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) {
                    _settings = loaded;
                    if (!string.IsNullOrEmpty(_settings.AiApiKey)) _settings.AiApiKey = FromBase64(_settings.AiApiKey);
                    if (!string.IsNullOrEmpty(_settings.NvidiaApiKey)) _settings.NvidiaApiKey = FromBase64(_settings.NvidiaApiKey);
                }
            }
            catch {
                _settings = new AppSettings();
            }
        }
        if (string.IsNullOrEmpty(_settings.InstallationId)) {
            _settings.InstallationId = Guid.NewGuid().ToString();
            Save();
        }
    }

    public void Save() {
        try {
            string plainGemini = _settings.AiApiKey;
            string plainNvidia = _settings.NvidiaApiKey;

            if (!string.IsNullOrEmpty(plainGemini)) _settings.AiApiKey = ToBase64(plainGemini);
            if (!string.IsNullOrEmpty(plainNvidia)) _settings.NvidiaApiKey = ToBase64(plainNvidia);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsPath, json);

            _settings.AiApiKey = plainGemini;
            _settings.NvidiaApiKey = plainNvidia;
        }
        catch { }
    }

    private string ToBase64(string plainText) {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }
        catch { return plainText; }
    }

    private string FromBase64(string base64Text) {
        if (string.IsNullOrEmpty(base64Text)) return base64Text;
        try {
            if (base64Text.Trim().Length % 4 != 0) return base64Text;
            byte[] bytes = Convert.FromBase64String(base64Text);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return base64Text; }
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

    public bool GetGeneratePdf() => _settings.GeneratePdf;
    public void SetGeneratePdf(bool val) { _settings.GeneratePdf = val; Save(); }

    public bool GetPdfCompactMode() => _settings.PdfCompactMode;
    public void SetPdfCompactMode(bool val) { _settings.PdfCompactMode = val; Save(); }

    public CompressionLevel GetCompressionLevel() => _settings.Compression;
    public void SetCompressionLevel(CompressionLevel level) { _settings.Compression = level; Save(); }

    // AI Common
    public AiProvider GetAiProvider() => _settings.AiProvider;
    public void SetAiProvider(AiProvider provider) { _settings.AiProvider = provider; Save(); }

    // Smart Getters
    public string GetAiApiKey() => _settings.AiProvider == AiProvider.NvidiaNim ? _settings.NvidiaApiKey : _settings.AiApiKey;
    public string GetAiModel() => _settings.AiProvider == AiProvider.NvidiaNim ? _settings.NvidiaModel : _settings.AiModel;

    // Gemini
    public bool GetAiThinkingEnabled() => _settings.AiThinkingEnabled;
    public void SetAiThinkingEnabled(bool enabled) { _settings.AiThinkingEnabled = enabled; Save(); }

    public int GetAiThinkingBudget() => _settings.AiThinkingBudget;
    public void SetAiThinkingBudget(int tokens) { _settings.AiThinkingBudget = tokens; Save(); }

    public string GetGeminiApiKey() => _settings.AiApiKey;
    public void SetGeminiApiKey(string key) { _settings.AiApiKey = key; Save(); }

    public string GetGeminiModel() => _settings.AiModel;
    public void SetGeminiModel(string model) { _settings.AiModel = model; Save(); }

    // Nvidia
    public string GetNvidiaApiKey() => _settings.NvidiaApiKey;
    public void SetNvidiaApiKey(string key) { _settings.NvidiaApiKey = key; Save(); }

    public string GetNvidiaModel() => _settings.NvidiaModel;
    public void SetNvidiaModel(string model) { _settings.NvidiaModel = model; Save(); }

    public int GetNvidiaMaxTokens() => _settings.NvidiaMaxTokens;
    public void SetNvidiaMaxTokens(int tokens) { _settings.NvidiaMaxTokens = tokens; Save(); }

    public double GetNvidiaTemperature() => _settings.NvidiaTemperature;
    public void SetNvidiaTemperature(double temp) { _settings.NvidiaTemperature = temp; Save(); }

    public double GetNvidiaTopP() => _settings.NvidiaTopP;
    public void SetNvidiaTopP(double topP) { _settings.NvidiaTopP = topP; Save(); }

    public bool GetNvidiaReasoningEnabled() => _settings.NvidiaReasoningEnabled;
    public void SetNvidiaReasoningEnabled(bool enabled) { _settings.NvidiaReasoningEnabled = enabled; Save(); }

    public string GetInstallationId() => _settings.InstallationId;
    public bool GetTelemetryEnabled() => _settings.IsTelemetryEnabled;
    public void SetTelemetryEnabled(bool enabled) { _settings.IsTelemetryEnabled = enabled; Save(); }
}