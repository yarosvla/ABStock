using System.Text.RegularExpressions;

namespace ABStock.UI.Tests;

/// <summary>
/// Параметр компонента <b>строкового</b> типа, записанный без <c>@</c>, — это
/// строковый ЛИТЕРАЛ, а не выражение. У нестрокового параметра такая запись
/// законна и означает выражение (<c>Disabled="startBlocked"</c> — это
/// C#-выражение, потому что <c>Disabled</c> объявлен <c>bool</c>); у строкового
/// она тоже законна, но означает совсем другое — и на экран уезжает имя
/// переменной.
///
/// Так и случилось: <c>Value="assetName"</c> на «Создании актива» и
/// <c>Value="nameDraft"</c> на «Профиле» рисовали в поле ввода текст
/// «assetName» и «nameDraft». Компилятор молчал, потому что <c>TextField.Value</c>
/// объявлен <c>string?</c>. Нашлось глазами на снимке базовой линии.
///
/// Проверка резолвит тип параметра по компоненту: сначала собирает по киту
/// карту «компонент → его строковые параметры», потом ищет в разметке места,
/// где такому параметру передан голый идентификатор, объявленный в том же
/// файле. Совпадение имени параметра с членом того же файла случайным не бывает.
/// </summary>
public sealed class RazorParameterBindingTests
{
    private static readonly Regex StringParameterPattern = new(
        @"\[Parameter[^\]]*\]\s*public\s+string\??\s+(?<name>[A-Za-z0-9_]+)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex AttributePattern = new(
        @"(?<name>\b[A-Z][A-Za-z0-9_]*)=""(?<value>[a-z][A-Za-z0-9_]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void СтроковыеПараметрыКомпонентовНеПринимаютИмяЧленаЛитералом()
    {
        var root = FindComponentsDirectory();
        var stringParameters = MapStringParameters(root);
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (var (component, tag, offset) in EnumerateComponentTags(text, stringParameters.Keys))
            {
                foreach (Match attribute in AttributePattern.Matches(tag))
                {
                    var parameter = attribute.Groups["name"].Value;
                    var value = attribute.Groups["value"].Value;

                    if (!stringParameters[component].Contains(parameter) || !DeclaresMember(text, value))
                    {
                        continue;
                    }

                    var line = text.Take(offset).Count(character => character == '\n') + 1;
                    violations.Add(
                        $"{Path.GetFileName(file)}:{line} — <{component} {parameter}=\"{value}\"> " +
                        $"передаёт литерал «{value}»: {component}.{parameter} объявлен string. " +
                        $"Нужно {parameter}=\"@{value}\".");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    /// <summary>Карта «компонент → имена его параметров строкового типа».</summary>
    private static Dictionary<string, HashSet<string>> MapStringParameters(string root)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            var names = StringParameterPattern
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (names.Count > 0)
            {
                map[Path.GetFileNameWithoutExtension(file)] = names;
            }
        }

        Assert.NotEmpty(map);
        return map;
    }

    /// <summary>Открывающие теги известных компонентов вместе с их содержимым до <c>&gt;</c>.</summary>
    private static IEnumerable<(string Component, string Tag, int Offset)> EnumerateComponentTags(
        string text,
        IEnumerable<string> components)
    {
        foreach (var component in components)
        {
            foreach (Match match in Regex.Matches(text, $@"<{Regex.Escape(component)}\b"))
            {
                var end = FindTagEnd(text, match.Index);
                if (end > match.Index)
                {
                    yield return (component, text[match.Index..end], match.Index);
                }
            }
        }
    }

    /// <summary>Конец открывающего тега: первый <c>&gt;</c> вне кавычек.</summary>
    private static int FindTagEnd(string text, int start)
    {
        var quoted = false;

        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                quoted = !quoted;
            }
            else if (text[index] == '>' && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Объявлено ли в файле ПОЛЕ или СВОЙСТВО с таким именем.
    ///
    /// Локальные <c>var</c> из блока <c>@{ }</c> сюда намеренно не попадают:
    /// в разметке есть законные литералы, совпадающие с именами локальных
    /// (<c>IconName="book"</c> рядом с <c>var book = OrderBookData</c>), и
    /// ложное срабатывание здесь дороже пропуска. Оба настоящих случая —
    /// <c>assetName</c> и <c>nameDraft</c> — были полями.
    /// </summary>
    private static bool DeclaresMember(string text, string identifier) =>
        Regex.IsMatch(
            text,
            $@"\b(private|protected|internal|public)\b[^;=\n(]*\b{Regex.Escape(identifier)}\b\s*(=|;|=>|\{{)");

    private static string FindComponentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ABStock.UI", "Components");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найден каталог src/ABStock.UI/Components.");
    }
}
