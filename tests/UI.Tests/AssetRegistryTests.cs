using ABStock.Application.Assets;
using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Реестр активов заменил «актив в сессии один». Его правила видны на всех
/// экранах сразу: порядок строк, уникальность тикера в адресе и предел в 8
/// активов, под который рассчитана высота «Активов».
/// </summary>
public sealed class AssetRegistryTests
{
    [Fact]
    public void Активы_идут_в_порядке_создания()
    {
        var catalog = NewRegistry();

        catalog.Add(Draft("Гелиос Энерго"), Profile());
        catalog.Add(Draft("КвантЭнерго"), Profile());
        catalog.Add(Draft("Балтийский Транзит"), Profile());

        Assert.Equal(["GLEN", "KVAN", "BLTR"], catalog.Assets.Select(asset => asset.Symbol));
    }

    [Fact]
    public void Совпавший_тикер_получает_второй_вариант_а_не_дубль()
    {
        var catalog = NewRegistry();

        var first = catalog.Add(Draft("Гелиос Энерго"), Profile());
        var second = catalog.Add(Draft("Гелиос Энергия"), Profile());

        Assert.Equal("GLEN", first.Symbol);
        Assert.NotEqual(first.Symbol, second.Symbol);
        Assert.StartsWith("GLE", second.Symbol);
        Assert.Equal(4, second.Symbol.Length);
    }

    [Fact]
    public void Форма_видит_тот_тикер_который_получит_актив()
    {
        var catalog = NewRegistry();
        catalog.Add(Draft("Гелиос Энерго"), Profile());

        var suggested = catalog.SuggestSymbol("Гелиос Энергия");
        var created = catalog.Add(Draft("Гелиос Энергия"), Profile());

        Assert.Equal(suggested, created.Symbol);
    }

    [Fact]
    public void Девятый_актив_не_создаётся()
    {
        var catalog = NewRegistry();

        for (var i = 0; i < IAssetRegistry.Limit; i++)
        {
            catalog.Add(Draft($"Актив {i}"), Profile());
        }

        Assert.True(catalog.IsFull);
        Assert.Throws<InvalidOperationException>(() => catalog.Add(Draft("Лишний"), Profile()));
        Assert.Equal(IAssetRegistry.Limit, catalog.Assets.Select(asset => asset.Symbol).Distinct().Count());
    }

    [Fact]
    public void Архив_освобождает_слот_но_не_тикер()
    {
        var catalog = NewRegistry();
        catalog.Add(Draft("Гелиос Энерго"), Profile());

        catalog.Archive("GLEN");
        var again = catalog.Add(Draft("Гелиос Энерго"), Profile());

        Assert.Single(catalog.Assets);
        // Прошлый прогон «GLEN» и новый актив не должны стать неотличимы.
        Assert.NotEqual("GLEN", again.Symbol);
    }

    [Fact]
    public void Правка_описания_правит_тот_же_актив()
    {
        var catalog = NewRegistry();
        var created = catalog.Add(Draft("Гелиос Энерго"), Profile());

        var updated = catalog.Update("glen", Draft("Гелиос Солар", growth: 90), Profile());

        // Тот же актив: адрес, тикер и история прогонов не рвутся.
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("GLEN", updated.Symbol);
        Assert.Equal("Гелиос Солар", catalog.Find("GLEN")?.Name);
        Assert.Equal(90, updated.GrowthPotential);
        Assert.Single(catalog.Assets);
    }

    [Fact]
    public void Созданный_актив_забирает_черновик_формы()
    {
        var catalog = NewRegistry();
        catalog.SetDraft(Draft("Гелиос Энерго"));

        catalog.Add(Draft("Гелиос Энерго"), Profile());

        Assert.Null(catalog.Draft);
    }

    [Fact]
    public void Стартовая_цена_лежит_на_шаге_005()
    {
        var catalog = NewRegistry();

        var asset = catalog.Add(Draft("Гелиос Энерго", growth: 37), Profile(0.63m));

        // Цену считает бэкенд; интерфейс её не передаёт.
        Assert.Equal(0m, asset.StartPrice % 0.05m);
        Assert.InRange(asset.StartPrice, 50m, 200m);
    }

    [Fact]
    public void Ревизия_растёт_на_каждой_правке()
    {
        var catalog = NewRegistry();
        var changes = 0;
        catalog.Changed += () => changes++;

        catalog.SetDraft(Draft("Гелиос Энерго"));
        catalog.Add(Draft("Гелиос Энерго"), Profile());
        catalog.Archive("GLEN");

        Assert.Equal(3, catalog.Revision);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Тикер_и_параметры_формы_хранит_каталог()
    {
        var backend = new InMemoryAssetCatalog();
        var before = new AssetRegistry(backend);
        before.Add(Draft("Гелиос Энерго", growth: 37), Profile());
        before.Add(Draft("Гелиос Энергия"), Profile());

        var after = new AssetRegistry(backend);

        Assert.Equal(
            before.Assets.Select(asset => (asset.Id, asset.Symbol)),
            after.Assets.Select(asset => (asset.Id, asset.Symbol)));
        Assert.Equal("Энергетика", after.Assets[0].Industry);
        Assert.Equal(37, after.Assets[0].GrowthPotential);
    }

    [Fact]
    public void Служебный_тикер_старого_актива_заменяется_читаемым()
    {
        // Актив из базы до миграции: каталог выдал ему тикер «AS…» из идентификатора.
        var backend = new InMemoryAssetCatalog();
        var legacy = backend.Create(new CreateAssetRequest(Profile() with { Name = "Гелиос Энерго" }));

        var registry = new AssetRegistry(backend);

        Assert.Equal("GLEN", registry.Assets.Single().Symbol);
        Assert.Equal(legacy.AssetId, registry.Assets.Single().Id);
    }

    [Fact]
    public void Архивный_актив_уходит_из_списка_но_остаётся_в_каталоге()
    {
        var backend = new InMemoryAssetCatalog();
        var registry = new AssetRegistry(backend);
        var created = registry.Add(Draft("Гелиос Энерго"), Profile());

        registry.Archive("GLEN");

        Assert.Empty(registry.Assets);
        Assert.True(backend.Get(created.Id)?.IsArchived);
    }

    [Fact]
    public void Созданный_актив_объявляется_рынку()
    {
        var catalog = NewRegistry();
        SessionAsset? announced = null;
        catalog.Added += asset => announced = asset;

        var created = catalog.Add(Draft("Гелиос Энерго"), Profile());

        Assert.Equal(created.Id, announced?.Id);
    }

    internal static AssetRegistry NewRegistry() => new(new InMemoryAssetCatalog());

    internal static AssetDraft Draft(string name, int growth = 50) =>
        new(name, "Описание актива для теста.", AssetType.Stock, "Энергетика", false, growth);

    internal static AssetProfile Profile(decimal sensitivity = 0.62m) =>
        new("Тест", AssetType.Stock, "Описание", [new AssetFactor("Спрос", true, 0.7m, [])], sensitivity);
}
