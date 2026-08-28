using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Разбор значений из localStorage. В хранилище может лежать что угодно —
/// правка руками, значение из старой версии, порча, — и ни одно из них не
/// имеет права уронить загрузку страницы: настройка не то, ради чего
/// приложение может не открыться.
/// </summary>
public class UserPreferencesTests
{
    [Theory]
    [InlineData("graphite")]
    [InlineData("steel")]
    [InlineData("ultramarine")]
    [InlineData("azure")]
    [InlineData("turquoise")]
    [InlineData("lavender")]
    [InlineData("amethyst")]
    [InlineData("orchid")]
    [InlineData("brass")]
    [InlineData("copper")]
    public void Все_десять_пресетов_известны(string key) =>
        Assert.True(UserPreferences.IsKnownAccent(key));

    [Fact]
    public void Пресетов_ровно_десять() =>
        Assert.Equal(10, UserPreferences.Accents.Count);

    [Fact]
    public void Графит_стоит_первым_и_остаётся_значением_по_умолчанию()
    {
        Assert.Equal("graphite", UserPreferences.Accents[0].Key);
        Assert.Equal("graphite", UserPreferences.DefaultAccent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ultramarine")]      // регистр важен: атрибут в CSS точный
    [InlineData("ультрамарин")]
    [InlineData("rebeccapurple")]
    [InlineData("{\"accent\":\"brass\"}")]
    public void Испорченный_акцент_даёт_графит_а_не_исключение(string? stored) =>
        Assert.Equal("graphite", UserPreferences.NormalizeAccent(stored));

    [Theory]
    [InlineData("10s")]
    [InlineData("30s")]
    [InlineData("1m")]
    [InlineData("5m")]
    [InlineData("15m")]
    [InlineData("1h")]
    public void Все_шесть_таймфреймов_известны(string key) =>
        Assert.True(Timeframes.IsKnown(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2m")]
    [InlineData("30S")]
    [InlineData("15м")]              // кириллическая «м» вместо латинской «m»
    public void Испорченный_таймфрейм_даёт_значение_по_умолчанию(string? stored) =>
        Assert.Equal("30s", Timeframes.Normalize(stored));

    [Fact]
    public void Таймфрейм_по_умолчанию_тот_же_что_стоял_на_Торгах_до_настройки() =>
        // На макете выбран 15м, но это иллюстрация выбранного состояния.
        // Менять поведение для того, кто настройку не трогал, нельзя.
        Assert.Equal("30s", Timeframes.DefaultKey);

    [Fact]
    public void Список_таймфреймов_один_и_тот_же_и_идёт_от_короткого_к_длинному()
    {
        var durations = Timeframes.All.Select(option => option.Duration).ToArray();
        Assert.Equal(durations.OrderBy(d => d).ToArray(), durations);
        Assert.Equal(6, Timeframes.All.Count);
    }

    [Theory]
    [InlineData("Иван Петров", "ИП")]
    [InlineData("Оператор", "ОП")]
    [InlineData("Алексей Ковалёв", "АК")]
    [InlineData("иван петров", "ИП")]
    [InlineData("Иван Сергеевич Петров", "ИП")]   // первое и последнее слово
    [InlineData("  Иван   Петров  ", "ИП")]
    [InlineData("Я", "Я")]
    public void Инициалы_считаются_из_имени(string name, string expected) =>
        Assert.Equal(expected, UserPreferences.GetInitials(name));

    [Fact]
    public void Пустое_имя_не_роняет_инициалы() =>
        // Инициалы пустого имени — инициалы имени по умолчанию: пустой кружок
        // в шапке хуже, чем «ОП».
        Assert.Equal("ОП", UserPreferences.GetInitials("   "));

    [Fact]
    public void Название_неизвестного_пресета_не_бросает() =>
        Assert.Equal("Графит", UserPreferences.GetAccentLabel("нет такого"));

    [Fact]
    public void Все_названия_пресетов_по_русски_и_различаются()
    {
        var labels = UserPreferences.Accents.Select(preset => preset.Label).ToArray();

        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.All(labels, label => Assert.DoesNotMatch("[A-Za-z]", label));
    }
}
