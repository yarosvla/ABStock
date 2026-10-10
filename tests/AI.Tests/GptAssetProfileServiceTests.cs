using System.Net;
using System.Text.Json;
using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace ABStock.AI.Tests;

public class GptAssetProfileServiceTests
{
    private static readonly AssetProfileRequest Request = new()
    {
        AssetType = AssetType.Stock,
        Name = "Гелиос Энерго",
        Description = "Региональный энергетический холдинг.",
        Industry = "Энергетика",
        IncludeGovernmentSupport = true
    };

    private static GptAssetProfileService CreateService(HttpMessageHandler handler, IEmbeddingService embeddings) =>
    new(
        new HttpClient(handler) { BaseAddress = new Uri("http://ai.test/") },
        embeddings,
        new ProfilePromptBuilder(),
        NullLogger<GptAssetProfileService>.Instance,
        new FakeTextTranslator());
    
    [Fact]
    public async Task Valid_answer_gives_balanced_ai_profile()
    {
        var generated = Enumerable.Range(0, 40)
            .Select(i => new { name = $"Фактор {i}", isPositive = i % 2 == 0, importance = 0.5m + i / 100m })
            .ToArray();

        // Все векторы одинаковы — близость к описанию у всех 1, прежний
        // порог 0,75 здесь ни при чём; проверяется отбор поровну.
        var vectors = generated.ToDictionary(f => f.name, _ => new[] { 1f, 1f });
        vectors[Request.Description] = [1f, 1f];

        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new { factors = generated }));
        var profile = await CreateService(handler, new DictionaryEmbeddings(vectors)).CreateProfileAsync(Request);

        Assert.Equal(ProfileSource.Ai, profile.Source);
        Assert.Equal(10, profile.PositiveFactors.Count());
        Assert.Equal(10, profile.NegativeFactors.Count());
        Assert.All(profile.Factors, factor => Assert.NotEmpty(factor.Embedding));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Unavailable_model_gives_fallback_profile()
    {
        var handler = new StubHttpHandler(HttpStatusCode.ServiceUnavailable, "{}");

        var profile = await CreateService(handler, new UnavailableEmbeddingService()).CreateProfileAsync(Request);

        Assert.Equal(ProfileSource.Fallback, profile.Source);
        Assert.NotEmpty(profile.PositiveFactors);
        Assert.NotEmpty(profile.NegativeFactors);
        Assert.Contains(profile.Factors, factor => factor.Name.Contains("Государственная поддержка"));
    }

    [Fact]
    public async Task Invalid_answer_is_retried_three_times_then_falls_back()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"factors":[{"name":"x","isPositive":true,"importance":0.5}]}""");

        var profile = await CreateService(handler, new UnavailableEmbeddingService()).CreateProfileAsync(Request);

        Assert.Equal(3, handler.Calls);
        Assert.Equal(ProfileSource.Fallback, profile.Source);
    }
}


internal sealed class FakeTextTranslator : ITextTranslator
{
    public Task<string> TranslateToEnglishAsync(
        string text,
        CancellationToken ct = default)
    {
        return Task.FromResult(text);
    }

    public Task<IReadOnlyList<string>> TranslateBatchToEnglishAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        return Task.FromResult(texts);
    }
}