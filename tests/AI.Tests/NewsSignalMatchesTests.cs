using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Числа совпадений — третий множитель формулы силы влияния и связь новости
/// с профилем актива. Раньше они вычислялись и терялись внутри сервиса,
/// попадая только в текст объяснения. Тест держит проводку: что насчитал
/// сопоставитель, то и лежит в NewsSignal — без округлений и перестановок.
/// </summary>
public class NewsSignalMatchesTests
{
    private sealed class FixedMatcher(AspectMatchResult result) : IAspectMatcher
    {
        public AspectMatchResult Match(String newsText, AssetProfile profile) => result;
    }

    private sealed class FixedFinBert : IFinBertAnalyzer
    {
        public FinBertResult Analyze(String text) => new()
        {
            PositiveProbability = 0.60m,
            NeutralProbability = 0.30m,
            NegativeProbability = 0.10m
        };
    }

    private static readonly AssetProfile Profile = new(
        Name: "Гелиос Энерго",
        AssetType: AssetType.Stock,
        Description: "Региональный энергетический холдинг.",
        PositiveFactors: ["рост тарифов", "господдержка", "новая ТЭЦ"],
        NegativeFactors: ["износ сетей", "долговая нагрузка"],
        Risks: ["авария на подстанции"],
        NewsSensitivity: 0.70m,
        Keywords: ["энергетика"]);

    private static NewsSignal Analyze(AspectMatchResult match) =>
        new NewsProcessingService(new FixedFinBert(), new FixedMatcher(match))
            .Analyze(new NewsAnalysisRequest
            {
                NewsText = "Компания ввела в строй новую ТЭЦ.",
                Profile = Profile
            });

    [Fact]
    public void Signal_carries_match_counts_unchanged()
    {
        var signal = Analyze(new AspectMatchResult(3, 2, 1, 0.75m));

        Assert.Equal(3, signal.PositiveMatches);
        Assert.Equal(2, signal.NegativeMatches);
        Assert.Equal(1, signal.RiskMatches);
        Assert.Equal(0.75m, signal.MatchScore);
    }

    [Fact]
    public void Signal_carries_zero_matches_as_zero()
    {
        var signal = Analyze(new AspectMatchResult(0, 0, 0, 0m));

        Assert.Equal(0, signal.PositiveMatches);
        Assert.Equal(0, signal.NegativeMatches);
        Assert.Equal(0, signal.RiskMatches);
        Assert.Equal(0m, signal.MatchScore);
    }

    [Fact]
    public void MatchScore_is_the_third_multiplier_of_impact_score()
    {
        var signal = Analyze(new AspectMatchResult(3, 2, 1, 0.75m));

        var expected = Math.Round(signal.Confidence * Profile.NewsSensitivity * signal.MatchScore, 4);
        Assert.Equal(expected, signal.ImpactScore);
    }
}
