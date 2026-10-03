using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Сила влияния — сумма вкладов задетых факторов:
/// тональность × близость × вес, умноженная на чувствительность актива.
/// Знак влияния определяется тональностью новости.
/// </summary>
public class NewsProcessingServiceTests
{
    private const string News = "Компания ввела в строй новую ТЭЦ.";

    // Новость вдоль первой оси. Фактор «ТЭЦ» — близость 1, «сети» — 0.
    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        [News] = [1f, 0f],
        ["Ввод новых генерирующих мощностей"] = [1f, 0f],
        ["Аварии в сетевом хозяйстве"] = [0f, 1f],
        ["Рост тарифов на тепло"] = [1f, 0f]
    };

    private static Task<NewsSignal> AnalyzeAsync(AssetProfile profile, FixedFinBert finBert)
    {
        var embeddings = new DictionaryEmbeddings(Vectors);
        var service = new NewsProcessingService(finBert, new RealFactorMatcher(embeddings), embeddings);

        return service.AnalyzeAsync(new NewsAnalysisRequest { NewsText = News, Profile = profile });
    }

    [Fact]
    public async Task Only_factors_above_threshold_are_counted()
    {
        var profile = Profiles.With(
            new AssetFactor("Ввод новых генерирующих мощностей", true, 0.9m, [1f, 0f]),
            new AssetFactor("Аварии в сетевом хозяйстве", false, 0.7m, [0f, 1f]));

        var signal = await AnalyzeAsync(profile, new FixedFinBert(0.70m, 0.20m, 0.10m));

        Assert.Equal(SignalPolarity.Positive, signal.Polarity);
        Assert.Equal(1, signal.PositiveMatches);
        Assert.Equal(0, signal.NegativeMatches);
        Assert.Equal(1m, signal.MatchScore);

        var factor = Assert.Single(signal.Factors);
        Assert.Equal(NewsFactorKind.Positive, factor.Kind);
        Assert.Equal("Ввод новых генерирующих мощностей", factor.Text);

        // (0,70 − 0,10) × 1 × 0,9 × 0,80
        Assert.Equal(0.432m, signal.ImpactScore);
    }

    [Fact]
    public async Task Positive_news_produces_positive_impact()
    {
        var profile = Profiles.With(
            new AssetFactor("Рост тарифов на тепло", false, 0.5m, [1f, 0f]));

        var signal = await AnalyzeAsync(
            profile,
            new FixedFinBert(0.70m, 0.20m, 0.10m));

        Assert.Equal(SignalPolarity.Positive, signal.Polarity);
        Assert.Equal(1, signal.NegativeMatches);
        Assert.True(signal.ImpactScore > 0m);
    }

    [Fact]
    public async Task Negative_news_produces_negative_impact()
    {
        var profile = Profiles.With(
            new AssetFactor("Рост тарифов на тепло", false, 0.5m, [1f, 0f]));

        var signal = await AnalyzeAsync(
            profile,
            new FixedFinBert(0.10m, 0.20m, 0.70m));

        Assert.Equal(SignalPolarity.Negative, signal.Polarity);
        Assert.Equal(1, signal.NegativeMatches);
        Assert.True(signal.ImpactScore < 0m);
    }

    [Fact]
    public async Task No_relevant_factors_means_zero_impact()
    {
        var profile = Profiles.With(
            new AssetFactor("Аварии в сетевом хозяйстве", false, 0.7m, [0f, 1f]));

        var signal = await AnalyzeAsync(profile, new FixedFinBert(0.10m, 0.20m, 0.70m));

        Assert.Equal(SignalPolarity.Negative, signal.Polarity);
        Assert.Empty(signal.Factors);
        Assert.Equal(0m, signal.MatchScore);
        Assert.Equal(0m, signal.ImpactScore);
    }

    [Fact]
    public async Task Factors_without_embedding_are_embedded_on_the_fly()
    {
        // Так выглядят запасной и демо-профиль: факторы собраны без модели.
        var profile = Profiles.With(
            new AssetFactor("Ввод новых генерирующих мощностей", true, 0.9m, []),
            new AssetFactor("Аварии в сетевом хозяйстве", false, 0.7m, [0f, 1f]));

        var embeddings = new DictionaryEmbeddings(Vectors);
        var matches = await new RealFactorMatcher(embeddings).MatchAsync([1f, 0f], profile);

        var batch = Assert.Single(embeddings.BatchCalls);
        Assert.Equal(["Ввод новых генерирующих мощностей"], batch);
        Assert.Equal(1m, matches[0].Similarity);
        Assert.Equal(0m, matches[1].Similarity);
    }
}
