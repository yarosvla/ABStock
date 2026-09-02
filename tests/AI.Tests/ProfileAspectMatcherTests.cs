using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Сопоставление новости с профилем. До появления этого разбора на его месте
/// стояла константа <c>AspectMatchResult(2, 0, 0, 0.5m)</c>: экран показывал
/// «вес 2 совпавших пунктов профиля» для любой новости, и третий множитель
/// формулы силы влияния не зависел ни от чего.
/// </summary>
public class ProfileAspectMatcherTests
{
    private static readonly AssetProfile Profile = new(
        Name: "Гелиос Энерго",
        AssetType: AssetType.Stock,
        Description: "Региональный энергетический холдинг.",
        PositiveFactors: ["Ввод новых генерирующих мощностей", "Долгосрочные контракты на поставку тепла"],
        NegativeFactors: ["Аварии в сетевом хозяйстве"],
        Risks: ["Операционные задержки"],
        NewsSensitivity: 0.78m,
        Keywords: ["энергетика"]);

    private static NewsSignal Analyze(string newsText) =>
        new NewsProcessingService(new StubFinBertAnalyzer(), new ProfileAspectMatcher())
            .Analyze(new NewsAnalysisRequest
            {
                NewsText = newsText,
                Profile = Profile
            });

    [Fact]
    public void Разные_новости_дают_разные_совпадения()
    {
        var about = Analyze("Гелиос Энерго вводит третий блок ТЭЦ проектной мощностью 320 МВт.");
        var unrelated = Analyze("Курс валюты подрос перед выходными без особых причин.");

        Assert.NotEqual(about.MatchScore, unrelated.MatchScore);
    }

    [Fact]
    public void Совпавший_пункт_назван_и_привязан_к_фрагменту_новости()
    {
        var signal = Analyze("Компания сообщила про аварию на подстанции: сетевое хозяйство изношено.");

        var match = Assert.Single(
            signal.Factors,
            factor => factor.Kind == NewsFactorKind.Negative);

        Assert.Equal("Аварии в сетевом хозяйстве", match.Text);
        Assert.False(string.IsNullOrWhiteSpace(match.Excerpt));
    }

    [Fact]
    public void Словоформы_сводятся_к_одному_слову()
    {
        // «аварию» против «Аварии», «мощностью» против «мощностей»: русский
        // флективный, и без сведения форм совпадений не нашлось бы вовсе.
        var signal = Analyze("Ввод мощностью 320 МВт перенесён, задержки продолжаются.");

        Assert.Contains(signal.Factors, factor => factor.Text == "Ввод новых генерирующих мощностей");
        Assert.Contains(signal.Factors, factor => factor.Text == "Операционные задержки");
    }

    [Fact]
    public void Случайное_созвучие_не_считается_совпадением()
    {
        // «сектор» и «сети» имеют общее начало в одну букву — это разные слова.
        var signal = Analyze("Сектор показал рост, аналитики ждут продолжения.");

        Assert.DoesNotContain(signal.Factors, factor => factor.Text == "Аварии в сетевом хозяйстве");
    }

    [Fact]
    public void Вес_совпадения_собран_по_формуле_из_артборда()
    {
        // Артборд «Новости · Результат анализа»: три совпавших пункта дают вес
        // 2,10, и сила влияния 1,41 = 0,86 × 0,78 × 2,10. Отсюда база 1,5 и
        // шаг 0,2 на пункт — тест держит именно это соотношение.
        var signal = Analyze("Ввод новых генерирующих мощностей, контракты на поставку тепла, энергетика.");

        var expected = 1.5m + 0.2m * signal.Factors.Count;

        Assert.Equal(expected, signal.MatchScore);
        Assert.InRange(signal.MatchScore, 1.5m, 2.5m);
    }

    [Fact]
    public void Сила_влияния_остаётся_в_шкале_раздела_10_1()
    {
        // Раздел 10.1: ImpactScore ≈ 0,3–2,5.
        var signal = Analyze("Ввод новых генерирующих мощностей и долгосрочные контракты на поставку тепла.");

        Assert.InRange(signal.ImpactScore, 0.3m, 2.5m);
    }
}
