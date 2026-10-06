namespace TxtConverter.Core;

public static class ProjectConstants {
    public const string OutputDirName = "_ConvertedToTxt";
    public const string MergedFileSuffix = "_Full_Source_code.txt";
    public const string AppDataFolderName = "oiuht54/TxtConverter";
    public const string SettingsFileName = "settings.json";
    public const string LangEn = "en";
    public const string LangRu = "ru";

    // Defaults
    public const string DefaultGeminiModel = "gemini-flash-lite-latest";
    public const string DefaultNvidiaModel = "minimaxai/minimax-m2";
    public const string DefaultCustomOpenAiEndpoint = "https://api.openai.com/v1";
    public const string DefaultCustomOpenAiModel = "gpt-4o-mini";
    public const int DefaultThinkingBudget = 16000;

    // Version, Author and Repository constants
    public const string CurrentVersion = "1.8.6";
    public const string GitHubRepo = "oiuht54/TxtConverter";
    public const string GitHubUrl = "https://github.com/oiuht54/TxtConverter";
    public const string Author = "oiuht54/Diziac";
}