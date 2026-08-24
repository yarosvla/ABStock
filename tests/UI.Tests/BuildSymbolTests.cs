using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Тикер показывают «Торги», «Новости», «Профиль» и «Создание актива» —
/// правило его сборки одно на всю систему (DESIGN.md 10).
/// </summary>
public class BuildSymbolTests
{
    [Theory]
    [InlineData("Гелиос Энерго", "GLEN")]
    [InlineData("КвантЭнерго", "KVAN")]
    [InlineData("Газпром", "GAZP")]
    [InlineData("Северная Сталь", "SVST")]
    public void Строит_латинский_тикер_из_названия(string name, string expected) =>
        Assert.Equal(expected, ActiveAssetDefaults.BuildSymbol(name));

    [Theory]
    [InlineData("Гелиос Энерго")]
    [InlineData("КвантЭнерго")]
    [InlineData("Газпром")]
    [InlineData("Аэрофлот")]
    [InlineData("ТЭЦ")]
    [InlineData("Северо Западная Энергетическая Компания")]
    public void Тикер_только_латиница_и_цифры_uppercase(string name)
    {
        var symbol = ActiveAssetDefaults.BuildSymbol(name);

        Assert.All(symbol, character =>
            Assert.True(character is >= 'A' and <= 'Z' or >= '0' and <= '9', $"«{character}» не латиница"));
    }

    [Theory]
    [InlineData("Гелиос Энерго")]
    [InlineData("КвантЭнерго")]
    [InlineData("Аэро")]
    [InlineData("ТЭЦ")]
    [InlineData("Северо Западная Энергетическая Компания")]
    public void Тикер_длиной_три_или_четыре_знака(string name)
    {
        var symbol = ActiveAssetDefaults.BuildSymbol(name);

        Assert.InRange(symbol.Length, 3, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Без_названия_отдаёт_запасной_тикер(string name) =>
        Assert.Equal("ABST", ActiveAssetDefaults.BuildSymbol(name));

    [Fact]
    public void Тикер_не_зависит_от_регистра_названия() =>
        Assert.Equal(
            ActiveAssetDefaults.BuildSymbol("гелиос энерго"),
            ActiveAssetDefaults.BuildSymbol("ГЕЛИОС ЭНЕРГО"));
}
