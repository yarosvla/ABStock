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
            $"Профиль {GetAssetTypeLabel(request.AssetType).ToLowerInvariant()} позволяет использовать сценарии роста через симуляцию"
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
        var keywords = new List<string>
        {
            name,
            industry,
            GetAssetTypeLabel(assetType)
        };

        if (includeGovernmentSupport)
        {
            keywords.Add("господдержка");
        }

        // Грубый фильтр, пока описание разбирает не модель: слишком короткие
        // и служебные слова («какой», «текст», «написан») попадали в чипы и
        // читались как характеристика актива. Когда придёт модель, фильтр
        // останется безвредным — она таких слов не вернёт.
        keywords.AddRange(description
            .Split([' ', ',', '.', ':', ';', '(', ')', '\n', '\r', '-', '—', '«', '»', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim())
            .Where(word => word.Length >= MinKeywordLength)
            .Where(word => word.All(char.IsLetter))
            .Where(word => !StopWords.Contains(word))
            .Take(6));

        return keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Короче — уже не характеристика актива, а связка.</summary>
    private const int MinKeywordLength = 6;

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
        "часть", "части", "время", "период", "компания", "компании"
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
