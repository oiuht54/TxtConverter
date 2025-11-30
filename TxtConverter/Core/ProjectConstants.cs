namespace TxtConverter.Core;

public static class ProjectConstants
{
    public const string OutputDirName = "_ConvertedToTxt";
    public const string ReportStructureFile = "_FileStructure.md";
    public const string MergedFileSuffix = "_Full_Source_code.txt";
    public const string AppDataFolderName = "TartarusCore/TxtConverter";
    public const string SettingsFileName = "settings.json";

    public const string LangEn = "en";
    public const string LangRu = "ru";

    // AI Defaults
    // Используем актуальную модель с поддержкой thinkingConfig
    public const string DefaultAiModel = "gemini-2.5-flash-lite";
    public const int DefaultThinkingBudget = 24000; // Оптимальный бюджет для этой модели
}