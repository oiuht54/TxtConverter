namespace TxtConverter.Core;

public static class ProjectConstants {
    public const string OutputDirName = "_ConvertedToTxt";
    public const string MergedFileSuffix = "_Full_Source_code.txt";
    public const string AppDataFolderName = "TartarusCore/TxtConverter";
    public const string SettingsFileName = "settings.json";
    public const string LangEn = "en";
    public const string LangRu = "ru";

    // Defaults
    public const string DefaultGeminiModel = "gemini-flash-lite-latest";
    public const string DefaultNvidiaModel = "minimaxai/minimax-m2";
    public const int DefaultThinkingBudget = 16000;
}