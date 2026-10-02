using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class AssetCatalogTests
{
    [Fact]
    public void Create_AssignsStableIdAndPreservesAssetDetails()
    {
        var catalog = new InMemoryAssetCatalog();
        var beforeCreation = DateTimeOffset.UtcNow;
        var profile = CreateProfile("Energy", AssetType.Stock);

        var created = catalog.Create(new CreateAssetRequest(profile, 100m));
        var found = catalog.Get(created.AssetId);

        Assert.NotEqual(Guid.Empty, created.AssetId);
        Assert.NotNull(found);
        Assert.Equal(created.AssetId, found.AssetId);
        Assert.Equal(created.CreatedAt, found.CreatedAt);
        Assert.Equal("Energy", found.Name);
        Assert.Equal(profile.Description, found.Description);
        Assert.Equal(AssetType.Stock, found.AssetType);
        Assert.Equal(100m, found.StartPrice);
        Assert.Equal(profile.NewsSensitivity, found.Profile.NewsSensitivity);
        Assert.Equal(ProfileSource.Fallback, found.Profile.Source);
        Assert.InRange(found.CreatedAt, beforeCreation, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_KeepsMultipleAssetsIncludingThoseWithSameName()
    {
        var catalog = new InMemoryAssetCatalog();

        var first = catalog.Create(new CreateAssetRequest(CreateProfile("Energy"), 100m));
        var second = catalog.Create(new CreateAssetRequest(CreateProfile("Energy", AssetType.Bond), 200m));

        Assert.NotEqual(first.AssetId, second.AssetId);
        Assert.Equal(2, catalog.GetAll().Count);
        Assert.Equal(100m, catalog.Get(first.AssetId)!.StartPrice);
        Assert.Equal(AssetType.Stock, catalog.Get(first.AssetId)!.AssetType);
        Assert.Equal(200m, catalog.Get(second.AssetId)!.StartPrice);
        Assert.Equal(AssetType.Bond, catalog.Get(second.AssetId)!.AssetType);
    }

    [Fact]
    public void Get_UnknownAssetReturnsNull()
    {
        var catalog = new InMemoryAssetCatalog();

        Assert.Null(catalog.Get(Guid.NewGuid()));
        Assert.Null(catalog.Get(Guid.Empty));
    }

    [Fact]
    public void GetAll_ReturnsIndependentReadOnlySnapshot()
    {
        var catalog = new InMemoryAssetCatalog();
        Assert.Empty(catalog.GetAll());
        var first = catalog.Create(new CreateAssetRequest(CreateProfile("Energy"), 100m));
        var snapshot = catalog.GetAll();

        catalog.Create(new CreateAssetRequest(CreateProfile("Metal", AssetType.Commodity), 200m));

        Assert.Equal(first.AssetId, Assert.Single(snapshot).AssetId);
        Assert.Equal(2, catalog.GetAll().Count);
        var collection = Assert.IsAssignableFrom<ICollection<Asset>>(snapshot);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Equal(2, catalog.GetAll().Count);
    }

    [Fact]
    public void Create_DoesNotKeepMutableProfileDataFromCaller()
    {
        var catalog = new InMemoryAssetCatalog();
        var embedding = new[] { 0.1f, 0.2f };
        var factors = new List<AssetFactor> { new("Demand", true, 0.8m, embedding) };
        var profile = CreateProfile("Energy") with { Factors = factors };

        var created = catalog.Create(new CreateAssetRequest(profile, 100m));
        embedding[0] = 9f;
        factors.Clear();

        var found = catalog.Get(created.AssetId)!;
        var factor = Assert.Single(found.Profile.Factors);
        Assert.Equal("Demand", factor.Name);
        Assert.Equal(0.1f, factor.Embedding[0]);
    }

    [Fact]
    public void ReadMethods_DoNotExposeMutableCatalogData()
    {
        var catalog = new InMemoryAssetCatalog();
        var profile = CreateProfile("Energy") with
        {
            Factors = [new AssetFactor("Demand", true, 0.8m, [0.1f, 0.2f])]
        };
        var created = catalog.Create(new CreateAssetRequest(profile, 100m));

        created.Profile.Factors[0].Embedding[0] = 5f;
        var found = catalog.Get(created.AssetId)!;
        Assert.Equal(0.1f, found.Profile.Factors[0].Embedding[0]);
        found.Profile.Factors[0].Embedding[0] = 6f;
        var listed = Assert.Single(catalog.GetAll());
        Assert.Equal(0.1f, listed.Profile.Factors[0].Embedding[0]);
        listed.Profile.Factors[0].Embedding[0] = 7f;

        Assert.Equal(0.1f, catalog.Get(created.AssetId)!.Profile.Factors[0].Embedding[0]);
    }

    [Fact]
    public void Create_TrimsNameAndDescription()
    {
        var catalog = new InMemoryAssetCatalog();
        var profile = CreateProfile("  Energy  ") with { Description = "  Energy company.  " };

        var created = catalog.Create(new CreateAssetRequest(profile, 100m));

        Assert.Equal("Energy", created.Name);
        Assert.Equal("Energy company.", created.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingName(string? name)
    {
        var catalog = new InMemoryAssetCatalog();
        var profile = CreateProfile("Energy") with { Name = name! };

        Assert.ThrowsAny<ArgumentException>(() => catalog.Create(new CreateAssetRequest(profile, 100m)));
        Assert.Empty(catalog.GetAll());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingDescription(string? description)
    {
        var catalog = new InMemoryAssetCatalog();
        var profile = CreateProfile("Energy") with { Description = description! };

        Assert.ThrowsAny<ArgumentException>(() => catalog.Create(new CreateAssetRequest(profile, 100m)));
        Assert.Empty(catalog.GetAll());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveStartPrice(int startPrice)
    {
        var catalog = new InMemoryAssetCatalog();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            catalog.Create(new CreateAssetRequest(CreateProfile("Energy"), startPrice)));
        Assert.Empty(catalog.GetAll());
    }

    [Fact]
    public void Create_RejectsUnknownAssetType()
    {
        var catalog = new InMemoryAssetCatalog();

        Assert.Throws<ArgumentException>(() =>
            catalog.Create(new CreateAssetRequest(CreateProfile("Energy", (AssetType)999), 100m)));
        Assert.Empty(catalog.GetAll());
    }

    [Fact]
    public void Create_RejectsNullRequestAndProfile()
    {
        var catalog = new InMemoryAssetCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.Create(null!));
        Assert.Throws<ArgumentNullException>(() => catalog.Create(new CreateAssetRequest(null!, 100m)));
        Assert.Empty(catalog.GetAll());
    }

    [Fact]
    public void DependencyInjection_SharesCatalogAcrossScopesWithoutStartingSimulation()
    {
        using var provider = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstCatalog = firstScope.ServiceProvider.GetRequiredService<IAssetCatalog>();
        var secondCatalog = secondScope.ServiceProvider.GetRequiredService<IAssetCatalog>();

        var created = firstCatalog.Create(new CreateAssetRequest(CreateProfile("Energy"), 100m));

        Assert.Same(firstCatalog, secondCatalog);
        Assert.Equal(created.AssetId, Assert.Single(secondCatalog.GetAll()).AssetId);
        var runner = provider.GetRequiredService<ISimulationRunner>();
        Assert.False(runner.IsRunning);
        Assert.Null(runner.Current);
    }

    [Fact]
    public void DependencyInjection_PreservesExistingCatalogRegistration()
    {
        var catalog = new InMemoryAssetCatalog();
        using var provider = new ServiceCollection()
            .AddSingleton<IAssetCatalog>(catalog)
            .AddABStockApplication()
            .BuildServiceProvider();

        Assert.Same(catalog, provider.GetRequiredService<IAssetCatalog>());
    }

    [Fact]
    public async Task Catalog_SupportsConcurrentCreationAndReads()
    {
        var catalog = new InMemoryAssetCatalog();
        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var created = catalog.Create(new CreateAssetRequest(CreateProfile($"Asset {index}"), 100m));
            Assert.Equal(created.AssetId, catalog.Get(created.AssetId)!.AssetId);
            Assert.Contains(catalog.GetAll(), asset => asset.AssetId == created.AssetId);
            return created;
        }));

        var createdAssets = await Task.WhenAll(tasks);

        Assert.Equal(32, catalog.GetAll().Count);
        Assert.Equal(32, createdAssets.Select(asset => asset.AssetId).Distinct().Count());
    }

    private static AssetProfile CreateProfile(string name, AssetType type = AssetType.Stock) =>
        new(name, type, $"Description of {name}.", [], 0.9m)
        {
            Source = ProfileSource.Fallback
        };

    [Fact]
    public void Create_PreservesFormParametersAndCalculatesPriceOnBackend()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = catalog.Create(new(CreateProfile("Energy"))
        {
            Ticker = " glen ", Industry = " Energy ", IncludeGovernmentSupport = true, GrowthPotential = 50
        });

        Assert.Equal("GLEN", asset.Ticker);
        Assert.Equal("Energy", asset.Industry);
        Assert.True(asset.IncludeGovernmentSupport);
        Assert.Equal(50, asset.GrowthPotential);
        Assert.Equal(130m, asset.StartPrice);
        Assert.Equal(asset.Ticker, catalog.Get(asset.AssetId)!.Ticker);
        Assert.Null(asset.ArchivedAt);
    }

    [Fact]
    public void GeneratedTickerStaysStableAcrossRenamesAndSameNamesHaveDifferentTickers()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = catalog.Create(new(CreateProfile("Same")));
        var second = catalog.Create(new(CreateProfile("Same")));
        var updated = catalog.Update(first.AssetId, new(CreateProfile("Renamed"), first.Ticker, "", false, null));

        Assert.NotEqual(first.Ticker, second.Ticker);
        Assert.Equal(first.Ticker, updated.Ticker);
        Assert.Equal(first.AssetId, updated.AssetId);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(first.StartPrice, updated.StartPrice);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal("Same", first.Name);
        Assert.Equal("Renamed", catalog.Get(first.AssetId)!.Name);
    }

    [Theory]
    [InlineData(AssetType.Stock, 105)]
    [InlineData(AssetType.Bond, 110)]
    [InlineData(AssetType.Commodity, 97)]
    [InlineData(AssetType.Crypto, 81)]
    public void AutomaticPriceUsesTypeAndNullableGrowthPotential(AssetType type, int expectedPrice)
    {
        var asset = new InMemoryAssetCatalog().Create(new(CreateProfile("Asset", type)));
        Assert.Equal((decimal)expectedPrice, asset.StartPrice);
        Assert.Null(asset.GrowthPotential);
    }

    [Fact]
    public void ExplicitPriceAndRegisteredPricePolicyRemainSupported()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IAssetStartPricePolicy>(new FixedPricePolicy()).AddABStockApplication().BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAssetCatalog>();

        Assert.Equal(42m, catalog.Create(new(CreateProfile("Automatic"))).StartPrice);
        Assert.Equal(123.456m, catalog.Create(new(CreateProfile("Manual"), 123.456m)).StartPrice);
    }

    [Fact]
    public void ArchiveIsIdempotentPreservesDetailsAndHidesOnlyDefaultListing()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = catalog.Create(new(CreateProfile("Energy"), 100m) { Ticker = "GLEN" });

        var archived = catalog.Archive(asset.AssetId);
        var repeated = catalog.Archive(asset.AssetId);

        Assert.True(archived.IsArchived);
        Assert.Equal(archived.ArchivedAt, repeated.ArchivedAt);
        Assert.Equal(100m, catalog.Get(asset.AssetId)!.StartPrice);
        Assert.Empty(catalog.GetAll());
        Assert.Equal(asset.AssetId, Assert.Single(catalog.GetAll(includeArchived: true)).AssetId);
        Assert.Throws<InvalidOperationException>(() => catalog.Update(asset.AssetId,
            new(CreateProfile("Renamed"), "GLEN", "", false, null)));
        Assert.Throws<ArgumentException>(() => catalog.Create(new(CreateProfile("Other")) { Ticker = "glen" }));
    }

    [Fact]
    public void UpdateRejectsDuplicateTickerOrChangedTypeWithoutChangingStoredAsset()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = catalog.Create(new(CreateProfile("First"), 100m) { Ticker = "ONE" });
        catalog.Create(new(CreateProfile("Second"), 200m) { Ticker = "TWO" });

        Assert.Throws<ArgumentException>(() => catalog.Update(first.AssetId,
            new(CreateProfile("Renamed"), " two ", "", false, null)));
        Assert.Throws<ArgumentException>(() => catalog.Update(first.AssetId,
            new(CreateProfile("Renamed", AssetType.Bond), "ONE", "", false, null)));
        Assert.Equal("First", catalog.Get(first.AssetId)!.Name);
        Assert.Equal("ONE", catalog.Get(first.AssetId)!.Ticker);
        Assert.Throws<KeyNotFoundException>(() => catalog.Archive(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => catalog.Update(Guid.NewGuid(),
            new(CreateProfile("Unknown"), "UNKNOWN", "", false, null)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidGrowthPotentialIsRejectedForCreationAndUpdate(int growth)
    {
        var catalog = new InMemoryAssetCatalog();
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Create(new(CreateProfile("Invalid")) { GrowthPotential = growth }));
        var asset = catalog.Create(new(CreateProfile("Valid")));
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Update(asset.AssetId,
            new(CreateProfile("Invalid"), asset.Ticker, "", false, growth)));
        Assert.Equal("Valid", catalog.Get(asset.AssetId)!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("HAS SPACE")]
    [InlineData("ТИКЕР")]
    [InlineData("TOO-LONG-TICKER-123456789012345")]
    public void InvalidTickerIsRejected(string ticker)
    {
        var catalog = new InMemoryAssetCatalog();
        Assert.ThrowsAny<ArgumentException>(() => catalog.Create(new(CreateProfile("Asset")) { Ticker = ticker }));
        Assert.Empty(catalog.GetAll());
    }

    [Fact]
    public void UpdateDoesNotKeepOrExposeMutableProfileArrays()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = catalog.Create(new(CreateProfile("Asset")));
        var vector = new[] { 0.1f };
        var profile = CreateProfile("Changed") with { Factors = [new("Factor", true, 1m, vector)] };
        var updated = catalog.Update(asset.AssetId, new(profile, asset.Ticker, "Sector", true, 10));
        vector[0] = 10f;
        updated.Profile.Factors[0].Embedding[0] = 20f;

        Assert.Equal(0.1f, catalog.Get(asset.AssetId)!.Profile.Factors[0].Embedding[0]);
        Assert.Equal("Sector", catalog.Get(asset.AssetId)!.Industry);
    }

    private sealed class FixedPricePolicy : IAssetStartPricePolicy
    {
        public decimal Calculate(CreateAssetRequest request) => 42m;
    }
}
