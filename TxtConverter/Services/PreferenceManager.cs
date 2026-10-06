using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using TxtConverter.Core;
using TxtConverter.Core.Enums;

namespace TxtConverter.Services;

public class PresetModel {
    public string Name { get; set; } = string.Empty;
    public string Extensions { get; set; } = string.Empty;
    public string IgnoredFolders { get; set; } = string.Empty;
    public string Exclusions { get; set; } = string.Empty;
}

public class AppSettings {
    public string Language { get; set; } = ProjectConstants.LangEn;
    public string LastSourceDir { get; set; } = string.Empty;
    public string LastPreset { get; set; } = "Unity Engine";
    public bool GenerateStructure { get; set; } = false;
    public bool CompactMode { get; set; } = true;
    public bool GenerateMerged { get; set; } = true;
    public bool GeneratePdf { get; set; } = true;
    public PdfMode PdfMode { get; set; } = PdfMode.Standard;
    public CompressionLevel Compression { get; set; } = CompressionLevel.Smart;

    // AI Common
    public bool AiEnabled { get; set; } = false;
    public AiProvider AiProvider { get; set; } = AiProvider.GoogleGemini;
    public bool AiThinkingEnabled { get; set; } = true;
    public int AiThinkingBudget { get; set; } = ProjectConstants.DefaultThinkingBudget;
    public string AiSystemPrompt { get; set; } = string.Empty;

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

    // Custom OpenAI Compatible Specific
    public string CustomOpenAiEndpoint { get; set; } = ProjectConstants.DefaultCustomOpenAiEndpoint;
    public string CustomOpenAiApiKey { get; set; } = string.Empty;
    public string CustomOpenAiModel { get; set; } = ProjectConstants.DefaultCustomOpenAiModel;
    public int CustomOpenAiMaxTokens { get; set; } = 4096;
    public double CustomOpenAiTemperature { get; set; } = 0.5;
    public double CustomOpenAiTopP { get; set; } = 0.7;

    // Telemetry & Installation
    public string InstallationId { get; set; } = string.Empty;
    public bool IsTelemetryEnabled { get; set; } = true;

    // Global Settings
    public string GlobalIgnoredFolders { get; set; } = string.Empty;
    public string GlobalExcludedPaths { get; set; } = string.Empty;

    public List<PresetModel> CustomPresets { get; set; } = new();
}

public class PreferenceManager {
    private static PreferenceManager? _instance;
    public static PreferenceManager Instance => _instance ??= new PreferenceManager();

    private AppSettings _settings;
    private readonly string _settingsPath;
    private readonly object _fileLock = new object();

    private PreferenceManager() {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(appData, ProjectConstants.AppDataFolderName);
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, ProjectConstants.SettingsFileName);
        _settings = new AppSettings();
    }

    public void Load() {
        lock (_fileLock) {
            if (File.Exists(_settingsPath)) {
                try {
                    string json = File.ReadAllText(_settingsPath);
                    var options = new JsonSerializerOptions {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, options);
                    if (loaded != null) {
                        _settings = loaded;
                        if (!string.IsNullOrEmpty(_settings.AiApiKey)) {
                            try { _settings.AiApiKey = FromBase64(_settings.AiApiKey); } catch { }
                        }
                        if (!string.IsNullOrEmpty(_settings.NvidiaApiKey)) {
                            try { _settings.NvidiaApiKey = FromBase64(_settings.NvidiaApiKey); } catch { }
                        }
                        if (!string.IsNullOrEmpty(_settings.CustomOpenAiApiKey)) {
                            try { _settings.CustomOpenAiApiKey = FromBase64(_settings.CustomOpenAiApiKey); } catch { }
                        }
                    }
                }
                catch {
                    _settings = new AppSettings();
                }
            }

            if (string.IsNullOrEmpty(_settings.InstallationId)) {
                _settings.InstallationId = Guid.NewGuid().ToString();
                SaveInternal();
            }
        }
    }

    public void Save() {
        lock (_fileLock) {
            SaveInternal();
        }
    }

    private void SaveInternal() {
        try {
            var settingsClone = new AppSettings {
                Language = _settings.Language,
                LastSourceDir = _settings.LastSourceDir,
                LastPreset = _settings.LastPreset,
                GenerateStructure = _settings.GenerateStructure,
                CompactMode = _settings.CompactMode,
                GenerateMerged = _settings.GenerateMerged,
                GeneratePdf = _settings.GeneratePdf,
                PdfMode = _settings.PdfMode,
                Compression = _settings.Compression,
                AiEnabled = _settings.AiEnabled,
                AiProvider = _settings.AiProvider,
                AiThinkingEnabled = _settings.AiThinkingEnabled,
                AiThinkingBudget = _settings.AiThinkingBudget,
                AiSystemPrompt = _settings.AiSystemPrompt,
                AiApiKey = ToBase64(_settings.AiApiKey),
                AiModel = _settings.AiModel,
                NvidiaApiKey = ToBase64(_settings.NvidiaApiKey),
                NvidiaModel = _settings.NvidiaModel,
                NvidiaMaxTokens = _settings.NvidiaMaxTokens,
                NvidiaTemperature = _settings.NvidiaTemperature,
                NvidiaTopP = _settings.NvidiaTopP,
                NvidiaReasoningEnabled = _settings.NvidiaReasoningEnabled,
                CustomOpenAiEndpoint = _settings.CustomOpenAiEndpoint,
                CustomOpenAiApiKey = ToBase64(_settings.CustomOpenAiApiKey),
                CustomOpenAiModel = _settings.CustomOpenAiModel,
                CustomOpenAiMaxTokens = _settings.CustomOpenAiMaxTokens,
                CustomOpenAiTemperature = _settings.CustomOpenAiTemperature,
                CustomOpenAiTopP = _settings.CustomOpenAiTopP,
                InstallationId = _settings.InstallationId,
                IsTelemetryEnabled = _settings.IsTelemetryEnabled,
                GlobalIgnoredFolders = _settings.GlobalIgnoredFolders,
                GlobalExcludedPaths = _settings.GlobalExcludedPaths,
                CustomPresets = _settings.CustomPresets != null ? new List<PresetModel>(_settings.CustomPresets) : new List<PresetModel>()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settingsClone, options);
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    private string ToBase64(string plainText) {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }
        catch {
            return plainText;
        }
    }

    private string FromBase64(string base64Text) {
        if (string.IsNullOrEmpty(base64Text)) return base64Text;
        try {
            string trimmed = base64Text.Trim();
            if (trimmed.Length % 4 != 0) return base64Text;
            byte[] bytes = Convert.FromBase64String(trimmed);
            return Encoding.UTF8.GetString(bytes);
        }
        catch {
            return base64Text;
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

    public bool GetGeneratePdf() => _settings.GeneratePdf;
    public void SetGeneratePdf(bool val) { _settings.GeneratePdf = val; Save(); }

    public PdfMode GetPdfMode() => _settings.PdfMode;
    public void SetPdfMode(PdfMode val) { _settings.PdfMode = val; Save(); }

    public CompressionLevel GetCompressionLevel() => _settings.Compression;
    public void SetCompressionLevel(CompressionLevel level) { _settings.Compression = level; Save(); }

    public bool GetAiEnabled() => _settings.AiEnabled;
    public void SetAiEnabled(bool val) { _settings.AiEnabled = val; Save(); }

    // AI Settings
    public AiProvider GetAiProvider() => _settings.AiProvider;
    public void SetAiProvider(AiProvider provider) { _settings.AiProvider = provider; Save(); }

    public string GetAiSystemPrompt() {
        return string.IsNullOrWhiteSpace(_settings.AiSystemPrompt)
            ? ProjectConstants.DefaultAiSystemPrompt
            : _settings.AiSystemPrompt;
    }

    public void SetAiSystemPrompt(string prompt) {
        _settings.AiSystemPrompt = prompt;
        Save();
    }

    public void ResetAiSystemPrompt() {
        _settings.AiSystemPrompt = ProjectConstants.DefaultAiSystemPrompt;
        Save();
    }

    public string GetAiApiKey() {
        return _settings.AiProvider switch {
            AiProvider.NvidiaNim => _settings.NvidiaApiKey,
            AiProvider.OpenAiCompatible => _settings.CustomOpenAiApiKey,
            _ => _settings.AiApiKey
        };
    }

    public string GetAiModel() {
        return _settings.AiProvider switch {
            AiProvider.NvidiaNim => _settings.NvidiaModel,
            AiProvider.OpenAiCompatible => _settings.CustomOpenAiModel,
            _ => _settings.AiModel
        };
    }

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

    // Custom OpenAI Compatible
    public string GetCustomOpenAiEndpoint() => string.IsNullOrWhiteSpace(_settings.CustomOpenAiEndpoint) ? ProjectConstants.DefaultCustomOpenAiEndpoint : _settings.CustomOpenAiEndpoint;
    public void SetCustomOpenAiEndpoint(string endpoint) { _settings.CustomOpenAiEndpoint = endpoint; Save(); }
    public string GetCustomOpenAiApiKey() => _settings.CustomOpenAiApiKey;
    public void SetCustomOpenAiApiKey(string key) { _settings.CustomOpenAiApiKey = key; Save(); }
    public string GetCustomOpenAiModel() => string.IsNullOrWhiteSpace(_settings.CustomOpenAiModel) ? ProjectConstants.DefaultCustomOpenAiModel : _settings.CustomOpenAiModel;
    public void SetCustomOpenAiModel(string model) { _settings.CustomOpenAiModel = model; Save(); }
    public int GetCustomOpenAiMaxTokens() => _settings.CustomOpenAiMaxTokens <= 0 ? 4096 : _settings.CustomOpenAiMaxTokens;
    public void SetCustomOpenAiMaxTokens(int tokens) { _settings.CustomOpenAiMaxTokens = tokens; Save(); }
    public double GetCustomOpenAiTemperature() => _settings.CustomOpenAiTemperature;
    public void SetCustomOpenAiTemperature(double temp) { _settings.CustomOpenAiTemperature = temp; Save(); }
    public double GetCustomOpenAiTopP() => _settings.CustomOpenAiTopP;
    public void SetCustomOpenAiTopP(double topP) { _settings.CustomOpenAiTopP = topP; Save(); }

    public string GetInstallationId() => _settings.InstallationId;
    public bool GetTelemetryEnabled() => _settings.IsTelemetryEnabled;
    public void SetTelemetryEnabled(bool enabled) { _settings.IsTelemetryEnabled = enabled; Save(); }

    // Custom Preset, Global Ignores & Exclusions
    public string GetGlobalIgnoredFolders() => _settings.GlobalIgnoredFolders;
    public void SetGlobalIgnoredFolders(string val) { _settings.GlobalIgnoredFolders = val; Save(); }
    public string GetGlobalExcludedPaths() => _settings.GlobalExcludedPaths;
    public void SetGlobalExcludedPaths(string val) { _settings.GlobalExcludedPaths = val; Save(); }
    public List<PresetModel> GetCustomPresets() => _settings.CustomPresets;
    public void SetCustomPresets(List<PresetModel> list) { _settings.CustomPresets = list; Save(); }
}