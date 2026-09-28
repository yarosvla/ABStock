namespace ABStock.AI;

public sealed class OpenAISettings
{
    /// <summary>
    /// Ключ OpenAI. Берётся из переменной окружения OPENAI_API_KEY, иначе из
    /// конфигурации OpenAI:ApiKey. Тот же ключ нужен Python-сервису: профиль
    /// генерирует он.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Адрес Python-сервиса (Python/main.py): FinBERT и генерация профиля.
    /// Конфигурация AI:ServiceUrl.
    /// </summary>
    public required Uri ServiceUrl { get; init; }
}
