namespace TxtConverter.Services.Ai;

public interface IAiClient {
    /// <summary>
    /// Получает список доступных моделей от API провайдера.
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync();

    /// <summary>
    /// Отправляет контекст проекта и промпт пользователя для анализа.
    /// </summary>
    /// <param name="userPrompt">Задача пользователя</param>
    /// <param name="projectContext">Полный текст проекта</param>
    /// <param name="overrideModel">Модель (если отличается от дефолтной)</param>
    /// <param name="overrideBudget">Бюджет токенов (если применимо)</param>
    /// <param name="systemPrompt">Настраиваемые системные инструкции анализа</param>
    Task<AiAnalysisResult> AnalyzeProjectAsync(
        string userPrompt,
        string projectContext,
        string? overrideModel = null,
        int? overrideBudget = null,
        string? systemPrompt = null);

    /// <summary>
    /// Выполняет тестовый запрос к API с простым приветствием ("Hi") для проверки соединения и валидности настроек.
    /// </summary>
    /// <param name="overrideModel">Модель (если отличается от дефолтной)</param>
    /// <returns>Текст ответа модели</returns>
    Task<string> TestConnectionAsync(string? overrideModel = null);
}