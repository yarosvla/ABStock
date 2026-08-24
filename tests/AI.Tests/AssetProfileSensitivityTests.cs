using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Чувствительность рисуется шкалой с зонами (DESIGN.md 9.17). Если значение
/// почти всегда упирается в потолок 0,95, шкала перестаёт что-либо различать —
/// поэтому масштаб проверяется тестами, а не на глаз.
/// </summary>
public class AssetProfileSensitivityTests
{
    private const decimal Min = 0.45m;
    private const decimal Max = 0.95m;
    private const decimal LowToMedium = 0.60m;
    private const decimal MediumToHigh = 0.75m;

    private static readonly AssetProfileService Service = new();

    private static decimal Sensitivity(
        AssetType type = AssetType.Stock,
        string industry = "Энергетика",
        bool government = true,
        int? growth = null) =>
        Service.CreateProfile(new AssetProfileRequest
        {
            AssetType = type,
            Name = "Гелиос Энерго",
            Description = "Региональный энергетический холдинг с тремя ТЭЦ и сетевым хозяйством.",
            Industry = industry,
            IncludeGovernmentSupport = government,
            GrowthPotential = growth
        }).NewsSensitivity;

    [Theory]
    [InlineData(AssetType.Stock)]
    [InlineData(AssetType.Bond)]
    [InlineData(AssetType.Commodity)]
    [InlineData(AssetType.Crypto)]
    public void Значение_не_выходит_за_подписанный_на_экране_диапазон(AssetType type)
    {
        foreach (var growth in new int?[] { null, 0, 25, 50, 75, 100 })
        {
            foreach (var government in new[] { true, false })
            {
                var value = Sensitivity(type, government: government, growth: growth);

                Assert.InRange(value, Min, Max);
            }
        }
    }

    [Fact]
    public void Средний_потенциал_роста_попадает_в_среднюю_зону()
    {
        var value = Sensitivity(growth: 50);

        Assert.InRange(value, LowToMedium, MediumToHigh);
    }

    [Fact]
    public void Незаполненный_потенциал_роста_не_двигает_значение() =>
        Assert.Equal(Sensitivity(growth: 50), Sensitivity(growth: null));

    [Fact]
    public void Потенциал_роста_двигает_значение_в_свою_сторону()
    {
        var low = Sensitivity(growth: 30);
        var middle = Sensitivity(growth: 50);
        var high = Sensitivity(growth: 80);

        Assert.True(low < middle, $"{low} должно быть меньше {middle}");
        Assert.True(middle < high, $"{middle} должно быть меньше {high}");
    }

    [Fact]
    public void Акция_не_упирается_в_потолок_при_высоком_потенциале() =>
        Assert.True(Sensitivity(growth: 100) < Max, "у акции должен оставаться запас до максимума");

    [Fact]
    public void Облигация_менее_чувствительна_чем_криптовалюта() =>
        Assert.True(
            Sensitivity(AssetType.Bond, growth: 50) < Sensitivity(AssetType.Crypto, growth: 50),
            "облигация не может реагировать на новости сильнее криптовалюты");

    [Fact]
    public void Государственная_поддержка_повышает_чувствительность() =>
        Assert.True(Sensitivity(government: true, growth: 50) > Sensitivity(government: false, growth: 50));
}
