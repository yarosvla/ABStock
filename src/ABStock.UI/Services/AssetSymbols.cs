namespace ABStock.UI.Services;

/// <summary>
/// Тикер актива. Правило сборки одно на всю систему (DESIGN.md 10), а
/// уникальность — свойство каталога: <see cref="Candidates"/> отдаёт варианты
/// по убыванию похожести, каталог берёт первый свободный.
/// </summary>
public static class AssetSymbols
{
    /// <summary>
    /// Варианты тикера для названия: сначала основной, потом второй и
    /// дальнейшие. При совпадении тикеров нужен другой вариант, а не дубль
    /// (контракт 2.1): адрес /trading/GLEN обязан вести к одному активу.
    /// Вариант сохраняет первые три знака основного — по ним человек
    /// узнаёт актив, — а четвёртый берёт из полного написания названия,
    /// потом из алфавита.
    /// </summary>
    public static IEnumerable<string> Candidates(string assetName)
    {
        var primary = Build(assetName);
        yield return primary;

        var stem = primary[..Math.Min(3, primary.Length)];
        var letters = string.Concat(SplitWords(assetName).Select(Transliterate))
            .Concat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        var seen = new HashSet<string>(StringComparer.Ordinal) { primary };

        foreach (var letter in letters)
        {
            var candidate = stem + letter;

            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        // 36 вариантов на одну основу исчерпать нельзя при пределе в 8
        // активов, но перебор обязан быть конечным и всегда что-то вернуть.
        for (var i = 0; ; i++)
        {
            var candidate = $"{stem[..Math.Min(2, stem.Length)]}{i:00}";

            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string[] SplitWords(string assetName) =>
        (assetName ?? string.Empty).Split([' ', '-', '_', '«', '»', '"', '.', ','], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Тикер из названия: латиница, 3–4 знака, uppercase (DESIGN.md 10).
    /// Кириллические две буквы вида «ГЭ» не читаются как биржевой тикер,
    /// поэтому название сначала транслитерируется.
    /// </summary>
    public static string Build(string assetName)
    {
        const string Fallback = "ABST";

        if (string.IsNullOrWhiteSpace(assetName))
        {
            return Fallback;
        }

        var words = SplitWords(assetName)
            .Select(Transliterate)
            .Where(word => word.Length > 0)
            .ToArray();

        if (words.Length == 0)
        {
            return Fallback;
        }

        // Одно слово — первые четыре знака: «Газпром» → GAZP.
        // Несколько слов — от каждого первая буква и следующая за ней согласная,
        // так тикер сохраняет след обоих слов: «Гелиос Энерго» → GLEN.
        var symbol = words.Length == 1
            ? words[0]
            : string.Concat(words.Take(4).Select(word => Initials(word, words.Length >= 3 ? 1 : 2)));

        if (symbol.Length > 4)
        {
            symbol = symbol[..4];
        }

        // Коротких тикеров не бывает: добираем знаки из полного написания.
        if (symbol.Length < 3)
        {
            var all = string.Concat(words);
            symbol = all.Length >= 3 ? all[..Math.Min(4, all.Length)] : all;
        }

        return symbol.Length == 0 ? Fallback : symbol;
    }

    /// <summary>Первая буква слова плюс следующие согласные, не длиннее <paramref name="count"/>.</summary>
    private static string Initials(string word, int count)
    {
        var taken = word[..1];

        foreach (var letter in word.Skip(1))
        {
            if (taken.Length >= count)
            {
                break;
            }

            if (!Vowels.Contains(letter))
            {
                taken += letter;
            }
        }

        // Слово из одних гласных («Аэро» → AERO) отдаёт то, что есть.
        return taken.Length >= count ? taken : word[..Math.Min(count, word.Length)];
    }

    private const string Vowels = "AEIOUY";

    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D", ['Е'] = "E",
        ['Ё'] = "E", ['Ж'] = "ZH", ['З'] = "Z", ['И'] = "I", ['Й'] = "I", ['К'] = "K",
        ['Л'] = "L", ['М'] = "M", ['Н'] = "N", ['О'] = "O", ['П'] = "P", ['Р'] = "R",
        ['С'] = "S", ['Т'] = "T", ['У'] = "U", ['Ф'] = "F", ['Х'] = "H", ['Ц'] = "C",
        ['Ч'] = "CH", ['Ш'] = "SH", ['Щ'] = "SCH", ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "",
        ['Э'] = "E", ['Ю'] = "YU", ['Я'] = "YA"
    };

    private static string Transliterate(string word)
    {
        var result = new System.Text.StringBuilder(word.Length);

        foreach (var symbol in word.ToUpperInvariant())
        {
            if (Cyrillic.TryGetValue(symbol, out var latin))
            {
                result.Append(latin);
            }
            else if (symbol is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                result.Append(symbol);
            }
        }

        return result.ToString();
    }
}
