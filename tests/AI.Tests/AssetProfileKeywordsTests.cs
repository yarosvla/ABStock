using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Ключевые слова показываются чипами и читаются как характеристика актива,
/// поэтому служебные слова из описания в них попадать не должны.
/// </summary>
public class AssetProfileKeywordsTests
{
    private static readonly AssetProfileService Service = new();

    private static IReadOnlyList<string> Keywords(string description) =>
        Service.CreateProfile(new AssetProfileRequest
        {
            AssetType = AssetType.Stock,
            Name = "Гелиос Энерго",
            Description = description,
            Industry = "Энергетика",
            IncludeGovernmentSupport = true
        }).Keywords;

    [Fact]
    public void Служебные_слова_не_попадают_в_ключевые()
    {
        var keywords = Keywords(
            "Какой текст здесь написан, такой профиль и получится: описание компании, " +
            "которая является генерирующей, поэтому данные общей мощности важны.");

        Assert.DoesNotContain("написан", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("описание", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("которая", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("является", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("поэтому", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("общей", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Короткие_слова_не_попадают_в_ключевые()
    {
        var keywords = Keywords("Какой был текст, три ТЭЦ и сети, тариф на них уже есть.");

        Assert.All(
            keywords.Skip(4), // первые четыре — название, отрасль, тип актива, господдержка
            keyword => Assert.True(keyword.Length >= 6, $"«{keyword}» короче шести знаков"));
    }

    [Fact]
    public void Числа_не_попадают_в_ключевые()
    {
        var keywords = Keywords(
            "Мощность 1240000 мегаватт, ввод в 2027000 году, модернизация энергоблока идёт.");

        Assert.All(keywords, keyword => Assert.False(
            keyword.All(char.IsDigit),
            $"«{keyword}» — это число, а не ключевое слово"));
    }

    [Fact]
    public void Содержательные_слова_остаются()
    {
        var keywords = Keywords(
            "Региональный энергетический холдинг ведёт модернизацию энергоблока и теплоснабжение города.");

        Assert.Contains("модернизацию", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Название_отрасль_и_тип_актива_всегда_в_ключевых()
    {
        var keywords = Keywords("Региональный энергетический холдинг с тремя станциями и сетями.");

        Assert.Contains("Гелиос Энерго", keywords);
        Assert.Contains("Энергетика", keywords);
        Assert.Contains("Акция", keywords);
    }
}
