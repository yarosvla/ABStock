using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class AssetProfileService : IAssetProfileService
{
    public AssetProfile CreateProfile(AssetProfileRequest request)
    {
        var normalizedName = string.IsNullOrWhiteSpace(request.Name)
            ? "Новый актив"
            : request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? "Описание не предоставлено."
            : request.Description.Trim();
        var normalizedIndustry = string.IsNullOrWhiteSpace(request.Industry)
            ? GetIndustryByAssetType(request.AssetType)
            : request.Industry.Trim();

        var positiveFactors = new List<string>
        {
            $"Спрос в секторе «{normalizedIndustry}»",
            $"Профиль {GetAssetTypeGenitive(request.AssetType)} позволяет использовать сценарии роста через симуляцию"
        };
        var negativeFactors = new List<string>
        {
            "Требуется подтверждение спроса на реальных объёмах",
            "Темп масштабирования зависит от рыночной ликвидности"
        };
        var risks = new List<string>
        {
            "Регуляторный риск",
            "Операционные задержки"
        };

        if (request.IncludeGovernmentSupport)
        {
            positiveFactors.Insert(0, "Государственная поддержка и институциональный спрос");
        }
        else
        {
            negativeFactors.Add("Ограниченная внешняя поддержка");
        }

        // Поле необязательное: если пользователь его не заполнил, вывода
        // о потенциале роста нет — ни в плюс, ни в минус.
        if (request.GrowthPotential is { } growthPotential)
        {
            if (growthPotential >= 70)
            {
                positiveFactors.Add("Высокий потенциал роста в среднесрочном горизонте");
            }
            else if (growthPotential >= 40)
            {
                positiveFactors.Add("Умеренный потенциал роста при стабильном новостном фоне");
            }
            else
            {
                risks.Add("Ограниченный апсайд при текущих вводных");
            }
        }

        switch (request.AssetType)
        {
            case AssetType.Stock:
                positiveFactors.Add("Корпоративные новости быстро отражаются в цене");
                risks.Add("Давление на мультипликаторы и стоимость капитала");
                break;
            case AssetType.Bond:
                positiveFactors.Add("Предсказуемый денежный поток и понятный риск-профиль");
                negativeFactors.Add("Чувствительность к ставкам и кредитному спреду");
                break;
            case AssetType.Commodity:
                positiveFactors.Add("Прямая зависимость от циклов спроса и предложения");
                risks.Add("Высокая чувствительность к глобальной волатильности");
                break;
            case AssetType.Crypto:
                positiveFactors.Add("Повышенная реакция на рыночный импульс и новости");
                risks.Add("Повышенная волатильность и тонкий стакан");
                break;
        }

        var keywords = BuildKeywords(normalizedName, normalizedIndustry, normalizedDescription, request.AssetType, request.IncludeGovernmentSupport);
        var newsSensitivity = CalculateNewsSensitivity(request.AssetType, normalizedIndustry, request.IncludeGovernmentSupport, request.GrowthPotential);

        return new AssetProfile(
            Name: normalizedName,
            AssetType: request.AssetType,
            Description: normalizedDescription,
            PositiveFactors: positiveFactors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NegativeFactors: negativeFactors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Risks: risks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NewsSensitivity: newsSensitivity,
            Keywords: keywords
        )
        {
            // Разбор описания сегодня один — свой. Когда сюда придёт вызов
            // языковой модели, при неудаче здесь встанет ProfileSource.Fallback,
            // и страница покажет запасной профиль без правок интерфейса.
            Source = ProfileSource.Ai
        };
    }

    /// <summary>
    /// Чувствительность к новостям в диапазоне 0,45–0,95, размеченном зонами
    /// шкалы: низкая до 0,60, средняя до 0,75, высокая дальше.
    ///
    /// Тип актива задаёт положение на шкале, остальные вводные его сдвигают.
    /// Сдвиги намеренно небольшие: раньше потенциал роста давал до +0,40 и
    /// в одиночку доводил почти любой актив до потолка 0,95 — шкала показывала
    /// максимум всегда и ничего не различала. Потенциал роста считается
    /// отклонением от середины (50), поэтому средний потенциал не двигает
    /// значение, низкий тянет вниз, высокий вверх.
    /// </summary>
    private static decimal CalculateNewsSensitivity(
        AssetType assetType,
        string industry,
        bool includeGovernmentSupport,
        int? growthPotential)
    {
        var sensitivity = assetType switch
        {
            AssetType.Stock => 0.62m,
            AssetType.Bond => 0.50m,
            AssetType.Commodity => 0.58m,
            AssetType.Crypto => 0.78m,
            _ => 0.60m
        };

        if (industry.Contains("Энерг", StringComparison.OrdinalIgnoreCase) ||
            industry.Contains("Тех", StringComparison.OrdinalIgnoreCase))
        {
            sensitivity += 0.04m;
        }

        if (includeGovernmentSupport)
        {
            sensitivity += 0.03m;
        }

        if (growthPotential is { } potential)
        {
            sensitivity += (Math.Clamp(potential, 0, 100) - 50) / 400m;
        }

        // Диапазон подписан на экране «Создание актива»: значение вне него
        // сделало бы подпись неправдой.
        return Math.Clamp(sensitivity, 0.45m, 0.95m);
    }

    private static IReadOnlyList<string> BuildKeywords(
        string name,
        string industry,
        string description,
        AssetType assetType,
        bool includeGovernmentSupport)
    {
        // Тип актива в ключевые слова НЕ идёт. Он уже стоит чипом в шапке
        // профиля и строкой в панели «Исходное описание», и третье появление —
        // дубль по разделу 9.0 («метрика живёт ровно в одном месте экрана»).
        // Сопоставлению новостей он тоже не помогает: «Акция» не встречается в
        // тексте новостей про энергетику. Пункты 71 и 85 docs/ui-backlog.md.
        var keywords = new List<string>
        {
            name,
            industry
        };

        if (includeGovernmentSupport)
        {
            keywords.Add("господдержка");
        }

        // Грубый фильтр, пока описание разбирает не модель. Когда придёт
        // модель, фильтр останется безвредным — она таких слов не вернёт.
        //
        // Регистр опускается: «Энергетическая» в начале описания начинается с
        // прописной не потому, что это имя собственное, а потому что это
        // начало предложения. В чипе рядом со строчными оно читалось как
        // название.
        keywords.AddRange(description
            .Split([' ', ',', '.', ':', ';', '(', ')', '\n', '\r', '-', '—', '«', '»', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim().ToLowerInvariant())
            .Where(word => word.Length >= MinKeywordLength)
            .Where(word => word.All(char.IsLetter))
            .Where(word => !StopWords.Contains(word))
            .Where(word => !LooksLikeVerb(word))
            .Take(6));

        return keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Короче — уже не характеристика актива, а связка.
    ///
    /// Порог опущен с шести до пяти: на шести не проходили «тариф» и «тепло»,
    /// а в артборде «Создания актива» они стоят чипами. Ниже пяти не опускаю —
    /// туда сразу попадают «года», «этом», «свои», и стоп-лист пришлось бы
    /// растить быстрее, чем он ловит. Четырёхбуквенные слова вроде «сети»
    /// в ключевые не попадут: это записанное ограничение, а не недосмотр.
    /// </summary>
    private const int MinKeywordLength = 5;

    /// <summary>
    /// Похоже ли слово на глагол в личной форме: «строит», «обслуживает»,
    /// «растёт», «развивает». Такие слова описывают действие, а не признак
    /// актива, и в чипе читались как характеристика.
    ///
    /// Фильтр по окончанию, а не по словарю, и он ошибается: «бюджет»,
    /// «кредит», «дефицит» — существительные с теми же окончаниями. Поэтому
    /// исключения перечислены явно. Это цена морфологии без модели, и она
    /// меньше, чем «обслуживает» в списке характеристик актива.
    /// </summary>
    private static bool LooksLikeVerb(string word) =>
        !VerbLikeNouns.Contains(word) &&
        (word.EndsWith("ет", StringComparison.Ordinal)
         || word.EndsWith("ёт", StringComparison.Ordinal)
         || word.EndsWith("ит", StringComparison.Ordinal)
         || word.EndsWith("ют", StringComparison.Ordinal)
         || word.EndsWith("ят", StringComparison.Ordinal)
         || word.EndsWith("ует", StringComparison.Ordinal)
         || word.EndsWith("ают", StringComparison.Ordinal));

    /// <summary>Существительные с глагольными окончаниями — исключения фильтра.</summary>
    private static readonly HashSet<string> VerbLikeNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "бюджет", "кредит", "дефицит", "депозит", "пакет", "билет", "момент",
        "процент", "лимит", "аудит", "актив", "проект", "объект", "субъект"
    };

    /// <summary>
    /// Служебные и оценочные слова, которые в описании встречаются часто,
    /// а об активе не говорят ничего.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "который", "которая", "которое", "которые", "которых", "которым",
        "поэтому", "однако", "также", "потому", "значит", "например",
        "необходимо", "является", "являются", "будет", "может", "можно", "нужно",
        "около", "между", "после", "перед", "через", "чтобы", "когда",
        "больше", "меньше", "очень", "почти", "всего", "только", "именно",
        "написан", "написано", "написать", "описание", "описания",
        "данный", "данные", "данных", "такой", "такая", "такие", "такое",
        "общий", "общая", "общее", "общей", "общего", "общих",
        "часть", "части", "время", "период", "компания", "компании",
        // Пятибуквенные, впущенные снижением порога: без них снижение
        // обменяло бы «обслуживает» на «этом» и «свои».
        "этом", "этой", "этих", "своих", "своей", "своим", "наших", "нашей",
        "года", "году", "годах", "более", "менее", "около", "также",
        "любой", "любые", "каждый", "каждая", "какой", "какая", "какие",
        "новый", "новая", "новые", "новых", "текст", "здесь", "тогда"
    };

    /// <summary>
    /// Тип актива в родительном падеже — для оборота «Профиль акции».
    /// Раньше сюда подставлялся именительный, и на экране стояло «Профиль
    /// акция позволяет…». Падежи таблицей, а не правилом: типов четыре, это
    /// закрытый набор.
    /// </summary>
    private static string GetAssetTypeGenitive(AssetType assetType) =>
        assetType switch
        {
            AssetType.Stock => "акции",
            AssetType.Bond => "облигации",
            AssetType.Commodity => "товара",
            AssetType.Crypto => "криптовалюты",
            _ => GetAssetTypeLabel(assetType).ToLowerInvariant()
        };

    private static string GetAssetTypeLabel(AssetType assetType) =>
        assetType switch
        {
            AssetType.Stock => "Акция",
            AssetType.Bond => "Облигация",
            AssetType.Commodity => "Товар",
            AssetType.Crypto => "Криптовалюта",
            _ => assetType.ToString()
        };

    private static string GetIndustryByAssetType(AssetType assetType) =>
        assetType switch
        {
            AssetType.Stock => "Энергетика",
            AssetType.Bond => "Финансы",
            AssetType.Commodity => "Сырьё",
            AssetType.Crypto => "Технологии",
            _ => "Рынок"
        };
}
