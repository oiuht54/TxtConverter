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
    Task<AiAnalysisResult> AnalyzeProjectAsync(string userPrompt, string projectContext, string? overrideModel = null, int? overrideBudget = null);
}