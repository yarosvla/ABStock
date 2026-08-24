using ABStock.Shared;

namespace ABStock.UI.Services;

public interface IActiveAssetContext
{
    ActiveAssetDraft? Draft { get; }

    AssetProfile? Profile { get; }

    DateTimeOffset? UpdatedAt { get; }

    int Revision { get; }

    ActiveAssetView GetView();

    void SetDraft(ActiveAssetDraft draft);

    void SetProfile(ActiveAssetDraft draft, AssetProfile profile);

    void Clear();
}

public sealed class ActiveAssetContext : IActiveAssetContext
{
    public ActiveAssetDraft? Draft { get; private set; }

    public AssetProfile? Profile { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public int Revision { get; private set; }

    public ActiveAssetView GetView()
    {
        var draft = Draft ?? ActiveAssetDefaults.DemoDraft;
        var profile = Profile ?? ActiveAssetDefaults.BuildProfile(draft);

        return new ActiveAssetView(
            draft,
            profile,
            ActiveAssetDefaults.BuildSymbol(draft.Name),
            Draft is null,
            profile.Source,
            UpdatedAt ?? DateTimeOffset.Now);
    }

    public void SetDraft(ActiveAssetDraft draft)
    {
        Draft = draft;
        Profile = null;
        Touch();
    }

    public void SetProfile(ActiveAssetDraft draft, AssetProfile profile)
    {
        Draft = draft;
        Profile = profile;
        Touch();
    }

    public void Clear()
    {
        Draft = null;
        Profile = null;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.Now;
        Revision++;
    }
}

public sealed record ActiveAssetDraft(
    string Name,
    string Description,
    AssetType AssetType,
    string Industry,
    bool IncludeGovernmentSupport,
    int GrowthPotential);

/// <param name="IsFallback">
/// Актив ещё не создавался — на экранах показывается демо-пример.
/// Это НЕ то же самое, что <paramref name="ProfileSource"/>: демо-контекст
/// говорит о том, чей актив на экране, источник профиля — о том, разобрала
/// ли описание языковая модель.
/// </param>
/// <param name="ProfileSource">Чем собран профиль: моделью или запасным алгоритмом.</param>
public sealed record ActiveAssetView(
    ActiveAssetDraft Draft,
    AssetProfile Profile,
    string Symbol,
    bool IsFallback,
    ProfileSource ProfileSource,
    DateTimeOffset UpdatedAt);

public static class ActiveAssetDefaults
{
    public static readonly ActiveAssetDraft DemoDraft = new(
        "КвантЭнерго",
        "Инновационная энергетическая компания, разрабатывающая и внедряющая системы накопления энергии нового поколения на основе квантовых технологий.",
        AssetType.Stock,
        "Энергетика",
        true,
        85);

    public static AssetProfile BuildProfile(ActiveAssetDraft draft)
    {
        var positiveFactors = new List<string>
        {
            $"Спрос в секторе «{draft.Industry}»",
            "Технологическая специализация"
        };
        var negativeFactors = new List<string>
        {
            "Капиталоемкость проектов",
            "Зависимость от темпа внедрения"
        };
        var risks = new List<string>
        {
            "Регуляторный риск",
            "Операционные задержки"
        };

        if (draft.IncludeGovernmentSupport)
        {
            positiveFactors.Insert(0, "Государственная поддержка");
        }
        else
        {
            negativeFactors.Add("Ограниченная институциональная поддержка");
        }

        if (draft.GrowthPotential >= 75)
        {
            positiveFactors.Add("Высокий потенциал роста");
        }

        var newsSensitivity = Math.Clamp(0.45m + draft.GrowthPotential / 200m, 0.45m, 0.95m);
        var keywords = new[]
        {
            draft.Name,
            draft.Industry,
            draft.AssetType.ToString(),
            draft.IncludeGovernmentSupport ? "господдержка" : "рыночный спрос"
        };

        return new AssetProfile(
            draft.Name,
            draft.AssetType,
            draft.Description,
            positiveFactors,
            negativeFactors,
            risks,
            newsSensitivity,
            keywords)
        {
            // Демо-профиль собирается здесь же, без языковой модели.
            Source = ProfileSource.Fallback
        };
    }

    /// <summary>
    /// Тикер из названия: латиница, 3–4 знака, uppercase (DESIGN.md 10).
    /// Кириллические две буквы вида «ГЭ» не читаются как биржевой тикер,
    /// поэтому название сначала транслитерируется.
    /// </summary>
    public static string BuildSymbol(string assetName)
    {
        const string Fallback = "ABST";

        if (string.IsNullOrWhiteSpace(assetName))
        {
            return Fallback;
        }

        var words = assetName
            .Split([' ', '-', '_', '«', '»', '"', '.', ','], StringSplitOptions.RemoveEmptyEntries)
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
