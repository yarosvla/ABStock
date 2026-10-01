using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// <c>/trading</c> без тикера открывает последний выбранный актив (DESIGN.md 13).
/// Правило выбора — чистая функция: хранилище браузера в тестах не нужно.
/// </summary>
public sealed class SelectedAssetTests
{
    private static readonly SessionAsset[] Assets = Build("Гелиос Энерго", "КвантЭнерго", "Балтийский Транзит");

    [Fact]
    public void Запомненный_актив_открывается_если_он_ещё_в_каталоге() =>
        Assert.Equal("KVAN", SelectedAsset.Pick("kvan", Assets, ["GLEN", "KVAN"]));

    [Fact]
    public void Ушедший_из_каталога_актив_уступает_первому_в_торгах() =>
        Assert.Equal("KVAN", SelectedAsset.Pick("AKAG", Assets, ["KVAN", "BLTR"]));

    [Fact]
    public void Без_торгов_выбирается_первый_созданный() =>
        Assert.Equal("GLEN", SelectedAsset.Pick(null, Assets, []));

    [Fact]
    public void Без_активов_выбирать_нечего() =>
        Assert.Null(SelectedAsset.Pick("GLEN", [], []));

    private static SessionAsset[] Build(params string[] names)
    {
        var catalog = AssetRegistryTests.NewRegistry();

        foreach (var name in names)
        {
            catalog.Add(AssetRegistryTests.Draft(name), AssetRegistryTests.Profile());
        }

        return [.. catalog.Assets];
    }
}
