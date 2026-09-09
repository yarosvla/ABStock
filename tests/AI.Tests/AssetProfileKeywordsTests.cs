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
            keywords.Skip(3), // первые три — название, отрасль, господдержка
            keyword => Assert.True(keyword.Length >= 5, $"«{keyword}» короче пяти знаков"));
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
    public void Название_и_отрасль_всегда_в_ключевых()
    {
        var keywords = Keywords("Региональный энергетический холдинг с тремя станциями и сетями.");

        Assert.Contains("Гелиос Энерго", keywords);
        Assert.Contains("Энергетика", keywords);
    }

    [Fact]
    public void Тип_актива_в_ключевые_не_идёт()
    {
        // Раньше «Акция» стояла в ключевых словах, и тест это закреплял.
        // Поведение изменено намеренно: тип актива уже показан чипом в шапке
        // профиля и строкой в панели «Исходное описание», третье появление —
        // дубль по разделу 9.0. Сопоставлению новостей он тоже не помогает:
        // слово «акция» в новостях про энергетику не встречается.
        var keywords = Keywords("Региональный энергетический холдинг с тремя станциями и сетями.");

        Assert.DoesNotContain("Акция", keywords);
    }

    [Fact]
    public void Глаголы_не_попадают_в_ключевые()
    {
        // «строит», «обслуживает» описывают действие, а не признак актива, и
        // в чипе читались как характеристика (пункты 71 и 85 бэклога).
        var keywords = Keywords(
            "Энергетическая компания строит и обслуживает солнечные электростанции в южных регионах.");

        Assert.DoesNotContain("строит", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("обслуживает", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Существительные_с_глагольными_окончаниями_остаются()
    {
        // Фильтр глаголов работает по окончанию и потому ошибается: «бюджет»
        // и «кредит» кончаются так же. Исключения перечислены явно, и тест
        // держит именно их.
        var keywords = Keywords("Компания увеличила бюджет и привлекла кредит на модернизацию.");

        Assert.Contains("бюджет", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("кредит", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Слова_из_описания_идут_строчными()
    {
        // «Энергетическая» в начале описания с прописной не потому, что имя
        // собственное, а потому что начало предложения.
        var keywords = Keywords("Энергетическая компания развивает теплоснабжение города.");

        Assert.DoesNotContain("Энергетическая", keywords);
    }
}
