using ABStock.Shared;

namespace ABStock.AI.Internal;

/// <summary>
/// Сопоставление новости с профилем актива: какие пункты профиля она задела и
/// каким куском текста.
///
/// До этого здесь стоял <c>StubAspectMatcher</c>, возвращавший
/// <c>new AspectMatchResult(2, 0, 0, 0.5m)</c> — константу, не глядя на вход.
/// Выдуманы были не только формулировки: выдуманы и количества, и третий
/// множитель формулы силы влияния. Экран показывал «вес 2 совпавших пунктов
/// профиля из 11» для любой новости.
///
/// Языковой модели здесь нет, и разбор устроен на морфологии беднее некуда —
/// по общему началу слов. Русский флективный, и «сетей» против «сети»,
/// «тарифов» против «тариф», «аварии» против «авария» иначе не свести.
/// Правило нарочно грубое, но честное: оно смотрит на настоящий текст, а не
/// возвращает константу. Когда сюда придёт модель, поменяется реализация
/// <see cref="IAspectMatcher"/>, а не форма результата.
/// </summary>
internal sealed class ProfileAspectMatcher : IAspectMatcher
{
    /// <summary>Короче — связка, а не признак: «уже», «них», «его».</summary>
    private const int MinSignificantLength = 4;

    /// <summary>Общее начало короче — совпадение случайное: «сети» и «сектор».</summary>
    private const int MinCommonPrefix = 3;

    /// <summary>Доля общего начала от короткого слова, ниже которой это разные слова.</summary>
    private const decimal MinPrefixShare = 0.6m;

    /// <summary>
    /// Вес совпадения — третий множитель формулы <c>уверенность ×
    /// чувствительность актива × совпадение по теме</c>.
    ///
    /// Значения восстановлены из артбордов «Новостей», а не подобраны: там
    /// 3 совпадения дают 2,10 при силе влияния 1,41 = 0,86 × 0,78 × 2,10, а
    /// 4 совпадения — 2,30 при 1,63 = 0,91 × 0,78 × 2,30. Отсюда база 1,5 и
    /// шаг 0,2 на пункт.
    ///
    /// Края сходятся с разделом 10.1 независимо: при минимуме вход даёт
    /// ≈0,34, при потолке ≈2,26, а раздел заявляет диапазон ≈0,3–2,5.
    /// </summary>
    private const decimal ScoreBase = 1.5m;
    private const decimal ScorePerMatch = 0.2m;
    private const decimal ScoreCeiling = 2.5m;

    public AspectMatchResult Match(string newsText, AssetProfile profile)
    {
        var newsWords = Tokenize(newsText);

        if (newsWords.Count == 0)
        {
            return new AspectMatchResult(0, 0, 0, ScoreBase, []);
        }

        var matches = new List<NewsFactorMatch>();

        Collect(profile.PositiveFactors, NewsFactorKind.Positive);
        Collect(profile.NegativeFactors, NewsFactorKind.Negative);
        Collect(profile.Risks, NewsFactorKind.Risk);
        Collect(profile.Keywords, NewsFactorKind.Keyword);

        var positive = matches.Count(match => match.Kind == NewsFactorKind.Positive);
        var negative = matches.Count(match => match.Kind == NewsFactorKind.Negative);
        var risk = matches.Count(match => match.Kind == NewsFactorKind.Risk);

        // Ключевые слова в счётчики сторон не идут: они говорят «новость про
        // этот актив», а не «новость его хвалит». В вес — идут: попадание по
        // теме и есть третий множитель.
        var score = Math.Min(ScoreBase + ScorePerMatch * matches.Count, ScoreCeiling);

        return new AspectMatchResult(positive, negative, risk, score, matches);

        void Collect(IReadOnlyList<string> items, NewsFactorKind kind)
        {
            foreach (var item in items)
            {
                if (FindExcerpt(item, newsWords) is { } excerpt)
                {
                    matches.Add(new NewsFactorMatch(kind, item, excerpt));
                }
            }
        }
    }

    /// <summary>
    /// Фрагмент новости, на котором сработал пункт профиля, — или
    /// <see langword="null"/>, если не сработал.
    ///
    /// Фрагментом берётся не одно слово, а совпавшее слово с соседями: «ТЭЦ»
    /// само по себе не объясняет, чем именно новость задела профиль, а «третий
    /// блок ТЭЦ» объясняет.
    /// </summary>
    private static string? FindExcerpt(string profileItem, IReadOnlyList<string> newsWords)
    {
        foreach (var itemWord in Tokenize(profileItem))
        {
            for (var index = 0; index < newsWords.Count; index++)
            {
                if (!IsSameWord(itemWord, newsWords[index]))
                {
                    continue;
                }

                var from = Math.Max(0, index - 1);
                var to = Math.Min(newsWords.Count - 1, index + 1);
                return string.Join(' ', newsWords.Skip(from).Take(to - from + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// Одно ли это слово в разных формах. Общее начало не короче трёх знаков
    /// и не меньше 60 % короткого слова: «авария» и «аварии» дают 5 из 6,
    /// «сети» и «сектор» — 1 из 4 и не проходят.
    /// </summary>
    private static bool IsSameWord(string left, string right)
    {
        var shorter = Math.Min(left.Length, right.Length);
        var common = 0;

        while (common < shorter
               && char.ToLowerInvariant(left[common]) == char.ToLowerInvariant(right[common]))
        {
            common++;
        }

        return common >= MinCommonPrefix && (decimal)common / shorter >= MinPrefixShare;
    }

    private static IReadOnlyList<string> Tokenize(string text) =>
        text
            .Split(
                [' ', ',', '.', ':', ';', '(', ')', '\n', '\r', '\t', '—', '–', '«', '»', '"', '!', '?'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('-', '\''))
            .Where(word => word.Length >= MinSignificantLength && word.Any(char.IsLetter))
            .ToArray();
}
