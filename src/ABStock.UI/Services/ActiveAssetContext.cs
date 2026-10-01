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
/// Отсюда singleton и здесь, тем же рассуждением, что у <see cref="ISessionEvents"/>.
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
        // Векторов у демо-факторов нет: их досчитает сопоставитель новостей,
        // так что демо-актив на «Новостях» разбирается той же моделью.
        var factors = new List<AssetFactor>
        {
            new($"Спрос в секторе «{draft.Industry}»", true, 0.7m, []),
            new("Технологическая специализация", true, 0.6m, []),
            new("Капиталоемкость проектов", false, 0.6m, []),
            new("Зависимость от темпа внедрения", false, 0.5m, []),
            new("Регуляторный риск", false, 0.5m, []),
            new("Операционные задержки", false, 0.4m, [])
        };

        if (draft.IncludeGovernmentSupport)
        {
            factors.Insert(0, new("Государственная поддержка", true, 0.8m, []));
        }
        else
        {
            factors.Add(new("Ограниченная институциональная поддержка", false, 0.5m, []));
        }

        if (draft.GrowthPotential >= 75)
        {
            factors.Add(new("Высокий потенциал роста", true, 0.6m, []));
        }

        // Тот же масштаб, что у запасного генератора: середина шкалы плюс
        // отклонение потенциала роста от 50. Иначе демо-актив на «Торгах» и
        // «Новостях» показывал бы чувствительность по другой линейке.
        var newsSensitivity = Math.Clamp(
            0.62m + (Math.Clamp(draft.GrowthPotential, 0, 100) - 50) / 400m,
            0.45m,
            0.95m);

        return new AssetProfile(
            draft.Name,
            draft.AssetType,
            draft.Description,
            factors,
            newsSensitivity)
        {
            // Демо-профиль собирается здесь же, без языковой модели.
            Source = ProfileSource.Fallback
        };
    }

    /// <summary>Тикер из названия — правило одно на систему, см. <see cref="AssetSymbols.Build"/>.</summary>
    public static string BuildSymbol(string assetName) => AssetSymbols.Build(assetName);
}
