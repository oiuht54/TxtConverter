using System.IO;
using System.Security.Cryptography; // Для DPAPI
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

    // В JSON файле это поле будет содержать ЗАШИФРОВАННУЮ строку.
    // В памяти приложения оно хранит РАСШИФРОВАННУЮ строку для работы.
    public string AiApiKey { get; set; } = string.Empty;

    public string AiModel { get; set; } = ProjectConstants.DefaultAiModel;
    public int AiThinkingBudget { get; set; } = ProjectConstants.DefaultThinkingBudget;
    public bool AiThinkingEnabled { get; set; } = true;
}

public class PreferenceManager
{
    private static PreferenceManager? _instance;
    public static PreferenceManager Instance => _instance ??= new PreferenceManager();

    private AppSettings _settings;
    private readonly string _settingsPath;

    // Entropy (соль) делает шифрование уникальным для этого приложения,
    // чтобы другие приложения от того же пользователя не могли расшифровать ключ.
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

                    // Попытка расшифровать API ключ при загрузке
                    if (!string.IsNullOrEmpty(_settings.AiApiKey))
                    {
                        try
                        {
                            _settings.AiApiKey = DecryptString(_settings.AiApiKey);
                        }
                        catch
                        {
                            // Если произошла ошибка (CryptographicException или FormatException),
                            // значит в файле хранится старый, незашифрованный ключ.
                            // Мы просто оставляем его как есть. 
                            // При следующем Save() он будет зашифрован.
                        }
                    }
                }
            }
            catch
            {
                // Ошибка чтения файла (например, битый JSON), сброс к дефолтным
                _settings = new AppSettings();
            }
        }
    }

    public void Save()
    {
        try
        {
            // 1. Сохраняем "чистый" ключ во временную переменную
            string plainKey = _settings.AiApiKey;

            // 2. Шифруем ключ перед сериализацией, если он есть
            if (!string.IsNullOrEmpty(plainKey))
            {
                _settings.AiApiKey = EncryptString(plainKey);
            }

            // 3. Сериализуем и пишем в файл
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsPath, json);

            // 4. ВАЖНО: Возвращаем "чистый" ключ обратно в объект в памяти, 
            // чтобы приложение могло продолжать работать без перезагрузки
            _settings.AiApiKey = plainKey;
        }
        catch
        {
            // Логирование ошибки сохранения, если необходимо
        }
    }

    private string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            // DataProtectionScope.CurrentUser означает, что расшифровать может только текущий пользователь Windows
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
        // Здесь мы не ловим исключения намеренно, чтобы Load() мог определить,
        // что это старый формат (plain text), и поймать ошибку уровнем выше.
        byte[] cipherBytes = Convert.FromBase64String(cipherText);
        byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, s_entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
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
}