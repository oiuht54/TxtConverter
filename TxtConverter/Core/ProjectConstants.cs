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

    // AI Base System Prompt
    public const string DefaultAiSystemPrompt =
@"You are a **Static Code Analysis Engine**.
Your goal is to build a complete execution environment for a specific task.
Do not guess based on filenames. **READ THE CODE** to find dependencies.

### EXECUTION PROTOCOL:
1. **Identify the Target:** Find the script(s) that directly implement the task logic.
2. **Scan for Hard Dependencies (The 'Mid Model' Strategy):**
   - Look inside the Target Script.
   - If it calls `PoolManager.get(...)` -> INCLUDE `PoolManager.gd`.
   - If it uses `preload(""res://path/to/item.tres"")` -> INCLUDE `item.tres`.
   - If it instantiates a Scene (`.tscn`), INCLUDE that `.tscn` file.
   - If it inherits `extends InteractiveObject`, INCLUDE `InteractiveObject.gd`.
3. **Scan for Data Definitions:**
   - If the Target uses a variable typed as a custom Class/Resource, include the file where that Class is defined.
4. **Identify Reference Patterns:**
   - Does another file in the project solve a similar problem? (e.g., if writing `VoxelWorld`, look at `WallGenerator`). Include it as a coding pattern reference.

### FILTERING RULES:
- **Strict Relevance:** Do NOT include thematic cousins (e.g., do not include 'WandGenerator' for 'TerrainGeneration' just because they both generate things). Only include if they share a base class or utility library.
- **Completeness:** If code A calls code B, and code B is missing, the code is broken. Include B.

### OUTPUT FORMAT:
[""path/to/target.gd"", ""path/to/dependency.gd"", ""path/to/resource.tres""]
(Return ONLY JSON)";

    // Version, Author and Repository constants
    public const string CurrentVersion = "1.8.7";
    public const string GitHubRepo = "oiuht54/TxtConverter";
    public const string GitHubUrl = "https://github.com/oiuht54/TxtConverter";
    public const string Author = "oiuht54/Diziac";
}