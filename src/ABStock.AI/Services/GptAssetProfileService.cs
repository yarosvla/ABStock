using System.Net.Http.Json;
using ABStock.AI.Models;
using ABStock.Shared;
using ABStock.AI.Internal;
using Microsoft.Extensions.Logging;

namespace ABStock.AI.Services;

internal sealed class GptAssetProfileService : IAssetProfileService
{
    /// <summary>
    /// Сколько факторов каждой полярности остаётся в профиле. Поровну, а не
    /// «двадцать лучших»: общий топ легко собирался из одних позитивных, и
    /// полоса баланса на «Создании актива» показывала перекос, которого в
    /// ответе модели не было.
    /// </summary>
    private const int FactorsPerPolarity = 10;

    private readonly HttpClient _httpClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IProfilePromptBuilder _promptBuilder;
    private readonly ILogger<GptAssetProfileService> _logger;
    private readonly ITextTranslator _translator;

    public GptAssetProfileService(
        HttpClient httpClient,
        IEmbeddingService embeddingService,
        IProfilePromptBuilder promptBuilder,
        ILogger<GptAssetProfileService> logger,
        ITextTranslator translator)
    {
        _httpClient = httpClient;
        _embeddingService = embeddingService;
        _promptBuilder = promptBuilder;
        _logger = logger;
        _translator = translator;
    }

    public async Task<AssetProfile> CreateProfileAsync(
        AssetProfileRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return await GenerateAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Модель недоступна — нет ключа, не запущен Python-сервис, ответ
            // не прошёл проверку. Экран показывает запасной профиль с
            // бейджем и кнопкой «Построить профиль заново».
            _logger.LogWarning(ex, "Профиль «{Name}» собран запасным алгоритмом", request.Name);
            return FallbackProfileBuilder.Build(request);
        }
    }

    private async Task<AssetProfile> GenerateAsync(
        AssetProfileRequest request,
        CancellationToken ct)
    {
        var prompt = _promptBuilder.BuildPrompt(request);

        AssetProfileGenerationResponse? result = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var response =
                await _httpClient.PostAsJsonAsync(
                    "generate-profile",
                    new { prompt },
                    ct);

            response.EnsureSuccessStatusCode();

            result =
                await response.Content.ReadFromJsonAsync<AssetProfileGenerationResponse>(ct);

            if (result?.Factors == null)
            {
                continue;
            }

            var positiveCount =
                result.Factors.Count(f => f.IsPositive);

            var negativeCount =
                result.Factors.Count(f => !f.IsPositive);
/*
            var valid =
                result.Factors.Count >= 40 &&
                result.Factors.Count <= 50 &&
                positiveCount >= 15 &&
                negativeCount >= 15;
*/
            var valid =
                result.Factors.Count == 40 &&
                positiveCount == 20 &&
                negativeCount == 20;
                
            if (valid)
            {
                break;
            }

            _logger.LogInformation(
                "Попытка {Attempt}: модель вернула {Total} факторов ({Positive}+/{Negative}-), повторяю",
                attempt, result.Factors.Count, positiveCount, negativeCount);

            result = null;
        }

        if (result == null)
        {
            throw new InvalidOperationException(
                "GPT failed to generate a valid asset profile after 3 attempts.");
        }

        // Описание и все факторы — одним запросом: первым идёт описание.
        var texts = new[]
        {
            request.Description
        }.Concat(result.Factors.Select(f => f.Name)).ToArray();

        var englishTexts = await _translator.TranslateBatchToEnglishAsync(texts, ct);

        var embeddings = await _embeddingService.CreateEmbeddingsAsync(
            englishTexts,
            ct);

        var assetEmbedding = embeddings[0];

        var scoredFactors = result.Factors
            .Select((f, i) =>
            {
                var factorEmbedding = embeddings[i + 1];

                var similarity =
                    CosineSimilarityHelper.Calculate(
                        assetEmbedding,
                        factorEmbedding);

                return new
                {
                    Factor = f,
                    Similarity = similarity,
                    Embedding = factorEmbedding
                };
            })
            .ToList();

        // Порога по близости к описанию нет: у text-embedding-3-small косинус
        // описания и конкретного фактора обычно 0,3–0,6, и прежний порог 0,75
        // отсекал все факторы — профиль выходил пустым. Близость остаётся
        // в ранжировании.
        var selectedFactors = scoredFactors
            .GroupBy(x => x.Factor.IsPositive)
            .SelectMany(group => group
                .OrderByDescending(x => x.Similarity * x.Factor.Importance)
                .Take(FactorsPerPolarity))
            .OrderByDescending(x => x.Factor.Importance)
            .ToList();

        var factors = new List<AssetFactor>();

        foreach (var item in selectedFactors)
        {
            var f = item.Factor;

            factors.Add(new AssetFactor(
                f.Name,
                f.IsPositive,
                Math.Clamp(f.Importance, 0m, 1m),
                item.Embedding
            ));
        }

        return new AssetProfile(
            request.Name,
            request.AssetType,
            request.Description,
            factors,
            0.9m
        )
        {
            Source = ProfileSource.Ai
        };
    }
}
