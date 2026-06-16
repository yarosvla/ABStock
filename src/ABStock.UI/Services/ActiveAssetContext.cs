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

public sealed record ActiveAssetView(
    ActiveAssetDraft Draft,
    AssetProfile Profile,
    string Symbol,
    bool IsFallback,
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
            keywords);
    }

    public static string BuildSymbol(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return "ABST";
        }

        var letters = new string(assetName
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

        if (letters.Length <= 4)
        {
            return letters;
        }

        var words = assetName
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(4)
            .ToArray();

        return words.Length >= 2
            ? new string(words)
            : letters[..4];
    }
}
