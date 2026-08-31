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

/// <summary>
/// Актив в сессии один (DESIGN.md 16), и живёт он ровно столько же, сколько
/// торговый прогон, — то есть сколько singleton <see cref="ABStock.Application.Simulation.ISimulationRunner"/>.
/// Отсюда singleton и здесь, тем же рассуждением, что у <see cref="ISessionNewsFeed"/>.
///
/// Scoped переживал переходы по ссылкам между интерактивными страницами:
/// enhanced navigation не перезагружает документ и не рвёт контур. Но не
/// переживал перезагрузку страницы, вход по прямому адресу и переход со
/// статически отрисованной приветственной — а симуляция при этом продолжала
/// идти. «Торги» в свежем контуре не находили актива, подставляли
/// демонстрационную заглушку и перезапускали ею прогон: кнопка на титульном
/// экране обещала «Вернуться к торгам · Гелиос Энерго», а на экране торгов
/// оказывался «КвантЭнерго».
///
/// Отсюда требование к состоянию: singleton читают несколько контуров сразу,
/// поэтому вместо четырёх изменяемых свойств здесь один снимок, который
/// заменяется целиком. Читатель берёт его одним обращением и не может
/// увидеть черновик от одной правки вместе с профилем от другой.
/// </summary>
public sealed class ActiveAssetContext : IActiveAssetContext
{
    private readonly Lock _sync = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public ActiveAssetDraft? Draft => _snapshot.Draft;

    public AssetProfile? Profile => _snapshot.Profile;

    public DateTimeOffset? UpdatedAt => _snapshot.UpdatedAt;

    public int Revision => _snapshot.Revision;

    public ActiveAssetView GetView()
    {
        // Один снимок на весь метод. Четыре отдельных чтения полей пришлись бы
        // на разные состояния, и вид склеил бы черновик одной правки с
        // профилем другой — актив с именем от нового описания и факторами от
        // старого.
        var snapshot = _snapshot;

        var draft = snapshot.Draft ?? ActiveAssetDefaults.DemoDraft;
        var profile = snapshot.Profile ?? ActiveAssetDefaults.BuildProfile(draft);

        return new ActiveAssetView(
            draft,
            profile,
            ActiveAssetDefaults.BuildSymbol(draft.Name),
            snapshot.Draft is null,
            profile.Source,
            snapshot.UpdatedAt ?? DateTimeOffset.Now);
    }

    /// <summary>Черновик без профиля: описание изменилось, профиль устарел.</summary>
    public void SetDraft(ActiveAssetDraft draft) => Replace(draft, profile: null);

    public void SetProfile(ActiveAssetDraft draft, AssetProfile profile) => Replace(draft, profile);

    public void Clear() => Replace(draft: null, profile: null);

    private void Replace(ActiveAssetDraft? draft, AssetProfile? profile)
    {
        lock (_sync)
        {
            _snapshot = new Snapshot(draft, profile, DateTimeOffset.Now, _snapshot.Revision + 1);
        }
    }

    /// <summary>Состояние целиком. Заменяется, а не правится по полю.</summary>
    private sealed record Snapshot(
        ActiveAssetDraft? Draft,
        AssetProfile? Profile,
        DateTimeOffset? UpdatedAt,
        int Revision)
    {
        public static readonly Snapshot Empty = new(null, null, null, 0);
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

        // Тот же масштаб, что у генератора: середина шкалы плюс отклонение
        // потенциала роста от 50. Иначе демо-актив на «Торгах» и «Новостях»
        // показывал бы чувствительность по другой линейке.
        var newsSensitivity = Math.Clamp(
            0.62m + (Math.Clamp(draft.GrowthPotential, 0, 100) - 50) / 400m,
            0.45m,
            0.95m);
        var keywords = new[]
        {
            draft.Name,
            draft.Industry,
            // Русское название типа, а не идентификатор перечисления: «Stock»
            // в чипах читался как чужое слово в русском интерфейсе.
            AgentDisplay.GetAssetTypeLabel(draft.AssetType),
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
