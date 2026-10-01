using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Веер — главная демонстрация продукта: один текст, разные реакции. Правило
/// «задет» и выбор самого задетого актива видны сразу на трёх экранах.
/// </summary>
public sealed class NewsFanTests
{
    private static readonly SessionAsset[] Catalog = BuildCatalog();

    [Fact]
    public void Актив_задет_если_совпал_хотя_бы_один_пункт_профиля()
    {
        var fan = Fan(("GLEN", Signal(0.18m, match: 0.4m)), ("KVAN", Signal(0.74m, match: 0m)));

        // Сила ниже начала шкалы (0,30) — всё равно задет: порога по силе нет.
        Assert.True(fan.For("GLEN")!.IsHit);
        Assert.False(fan.For("KVAN")!.IsHit);
    }

    [Fact]
    public void Незадетый_и_неразобранный_актив_остаются_строками_веера()
    {
        var fan = Fan(("GLEN", Signal(0.38m)));

        Assert.Equal(["GLEN", "KVAN", "BLTR"], fan.Assets.Select(asset => asset.Symbol));
        Assert.False(fan.For("BLTR")!.WasAnalyzed);
    }

    [Fact]
    public void Самый_задетый_тот_у_кого_сила_больше_а_не_первый_по_порядку()
    {
        var fan = Fan(("GLEN", Signal(0.38m)), ("KVAN", Signal(0.74m)), ("BLTR", Signal(0.9m, match: 0m)));

        Assert.Equal("KVAN", fan.Lead?.Symbol);
        Assert.Equal(0.74m, fan.MaxImpact);
    }

    [Fact]
    public void Тональность_и_уверенность_одни_на_новость()
    {
        var fan = Fan(("KVAN", Signal(0.74m)));

        Assert.Equal(SignalPolarity.Positive, fan.Polarity);
        Assert.Equal(0.46m, fan.Confidence);
    }

    [Fact]
    public void Новость_никого_не_задевшая_не_имеет_лидера() =>
        Assert.Null(Fan(("GLEN", Signal(0.2m, match: 0m))).Lead);

    private static NewsFan Fan(params (string Symbol, NewsSignal Signal)[] signals) =>
        NewsFan.FromSignals(Catalog, signals.ToDictionary(pair => pair.Symbol, pair => pair.Signal));

    private static NewsSignal Signal(decimal impact, decimal match = 0.5m) =>
        new(SignalPolarity.Positive, 0.46m, impact, "") { MatchScore = match };

    private static SessionAsset[] BuildCatalog()
    {
        var catalog = AssetRegistryTests.NewRegistry();
        catalog.Add(AssetRegistryTests.Draft("Гелиос Энерго"), AssetRegistryTests.Profile());
        catalog.Add(AssetRegistryTests.Draft("КвантЭнерго"), AssetRegistryTests.Profile());
        catalog.Add(AssetRegistryTests.Draft("Балтийский Транзит"), AssetRegistryTests.Profile());
        return [.. catalog.Assets];
    }
}
