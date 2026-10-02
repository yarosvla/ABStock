using ABStock.Application.Assets;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Активы сессии глазами интерфейса. Сами активы, тикеры, параметры формы,
/// архив и стартовая цена — каталог бэкенда (<see cref="IAssetCatalog"/>);
/// реестр добавляет то, что нужно только экранам: правило тикера, предел в
/// 8 активов, черновик формы и событие для рынка.
///
/// Страница больше не спрашивает «какой актив», она спрашивает «какие активы»
/// и отдельно — какой выбран в этой вкладке (<see cref="ISelectedAsset"/>).
/// </summary>
public interface IAssetRegistry
{
    /// <summary>Предел активов в сессии: 8 строк «Активов» помещаются на 1280 без прокрутки.</summary>
    const int Limit = 8;

    /// <summary>Действующие активы в порядке создания — один порядок на всех экранах.</summary>
    IReadOnlyList<SessionAsset> Assets { get; }

    bool IsFull { get; }

    /// <summary>Черновик формы «Нового актива»: переживает уход со страницы и возврат.</summary>
    AssetDraft? Draft { get; }

    int Revision { get; }

    event Action? Changed;

    /// <summary>Создан актив. На него подписан рынок: идущие торги принимают актив сразу.</summary>
    event Action<SessionAsset>? Added;

    SessionAsset? Find(string? symbol);

    SessionAsset? Find(Guid assetId);

    /// <summary>
    /// Тикер, который получит актив с таким названием: свободный, а не просто
    /// собранный из имени. Форма показывает его до создания.
    /// </summary>
    string SuggestSymbol(string name, string? exceptSymbol = null);

    SessionAsset Add(AssetDraft draft, AssetProfile profile);

    /// <summary>
    /// Описание изменилось — профиль собран заново. Актив тот же: тикер,
    /// идентификатор и история прогонов остаются.
    /// </summary>
    SessionAsset Update(string symbol, AssetDraft draft, AssetProfile profile);

    /// <summary>Архив вместо удаления: прошлые прогоны ссылаются на актив.</summary>
    void Archive(string symbol);

    void SetDraft(AssetDraft? draft);
}

/// <summary>
/// Singleton: активы принадлежат сессии, а не вкладке, и их читают несколько
/// контуров сразу. Поэтому состояние — один снимок, который заменяется
/// целиком: читатель не может увидеть список от одной правки и черновик от
/// другой.
/// </summary>
public sealed class AssetRegistry : IAssetRegistry
{
    private readonly IAssetCatalog _catalog;
    private readonly Lock _sync = new();
    private volatile Snapshot? _snapshot;

    public AssetRegistry(IAssetCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<SessionAsset> Assets => Current.Assets;

    public bool IsFull => Current.Assets.Count >= IAssetRegistry.Limit;

    public AssetDraft? Draft => Current.Draft;

    public int Revision => Current.Revision;

    public event Action? Changed;

    public event Action<SessionAsset>? Added;

    public SessionAsset? Find(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol)
            ? null
            : Current.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase));

    public SessionAsset? Find(Guid assetId) =>
        Current.Assets.FirstOrDefault(asset => asset.Id == assetId);

    public string SuggestSymbol(string name, string? exceptSymbol = null)
    {
        lock (_sync)
        {
            return FreeSymbolLocked(Current, name, exceptSymbol);
        }
    }

    public SessionAsset Add(AssetDraft draft, AssetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(profile);

        SessionAsset added;

        lock (_sync)
        {
            var current = Current;

            if (current.Assets.Count >= IAssetRegistry.Limit)
            {
                throw new InvalidOperationException($"В сессии уже {IAssetRegistry.Limit} активов — это предел.");
            }

            // Стартовую цену считает бэкенд: в запросе её нет намеренно.
            var asset = _catalog.Create(new CreateAssetRequest(Named(draft, profile))
            {
                Ticker = FreeSymbolLocked(current, draft.Name, exceptSymbol: null),
                Industry = draft.Industry,
                IncludeGovernmentSupport = draft.IncludeGovernmentSupport,
                GrowthPotential = GrowthOf(draft)
            });

            // Черновик формы отдан активу: следующий «Новый актив» — с чистого листа.
            _snapshot = Rebuild(current.Revision + 1, draft: null);
            added = Find(asset.AssetId)!;
        }

        Changed?.Invoke();
        Added?.Invoke(added);
        return added;
    }

    public SessionAsset Update(string symbol, AssetDraft draft, AssetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(profile);

        SessionAsset updated;

        lock (_sync)
        {
            var current = Current;
            var previous = current.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Актива {symbol} в сессии нет.");

            // Тикер не пересобирается из нового названия: по нему актив знают
            // адрес, свитчер и закладки.
            _catalog.Update(previous.Id, new UpdateAssetRequest(
                Named(draft, profile),
                previous.Symbol,
                draft.Industry,
                draft.IncludeGovernmentSupport,
                GrowthOf(draft)));

            _snapshot = Rebuild(current.Revision + 1, current.Draft);
            updated = Find(previous.Id)!;
        }

        Changed?.Invoke();
        return updated;
    }

    public void Archive(string symbol)
    {
        lock (_sync)
        {
            var current = Current;
            var asset = current.Assets.FirstOrDefault(item =>
                string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                return;
            }

            // Слот освобождается, тикер — нет: каталог держит тикер архивного
            // актива, и прошлый прогон «GLEN» не спутать с новым активом.
            _catalog.Archive(asset.Id);
            _snapshot = Rebuild(current.Revision + 1, current.Draft);
        }

        Changed?.Invoke();
    }

    public void SetDraft(AssetDraft? draft)
    {
        lock (_sync)
        {
            _snapshot = Current with { Draft = draft, Revision = Current.Revision + 1 };
        }

        Changed?.Invoke();
    }

    private Snapshot Current
    {
        get
        {
            if (_snapshot is { } snapshot)
            {
                return snapshot;
            }

            lock (_sync)
            {
                if (_snapshot is null)
                {
                    RenameGeneratedTickers();
                    _snapshot = Rebuild(revision: 0, draft: null);
                }

                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Активы, созданные до того, как каталог начал хранить тикер, получили
    /// при миграции служебный «AS…» из идентификатора. В адресе и на экранах
    /// такой тикер нечитаем, поэтому при первом чтении действующие активы
    /// получают тикер по правилу интерфейса — в порядке создания.
    /// </summary>
    private void RenameGeneratedTickers()
    {
        var all = _catalog.GetAll(includeArchived: true);
        var taken = all.Select(asset => asset.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in all.Where(IsGenerated).Where(asset => !asset.IsArchived).OrderBy(asset => asset.CreatedAt))
        {
            var ticker = AssetSymbols.Candidates(asset.Name).First(candidate => !taken.Contains(candidate));
            _catalog.Update(asset.AssetId, new UpdateAssetRequest(
                asset.Profile, ticker, asset.Industry, asset.IncludeGovernmentSupport, asset.GrowthPotential));
            taken.Add(ticker);
        }

        static bool IsGenerated(Asset asset) =>
            string.Equals(asset.Ticker, AssetFactory.GenerateTicker(asset.AssetId), StringComparison.OrdinalIgnoreCase);
    }

    private Snapshot Rebuild(int revision, AssetDraft? draft)
    {
        // Архивные — отдельно: их тикеры заняты, но строк у них нет.
        var all = _catalog.GetAll(includeArchived: true);

        var assets = all
            .Where(asset => !asset.IsArchived)
            .OrderBy(asset => asset.CreatedAt)
            .Select(asset => new SessionAsset(asset))
            .ToArray();

        var taken = all.Select(asset => asset.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new Snapshot(assets, taken, draft, revision);
    }

    private static string FreeSymbolLocked(Snapshot current, string name, string? exceptSymbol) =>
        AssetSymbols.Candidates(name).First(candidate =>
            string.Equals(candidate, exceptSymbol, StringComparison.OrdinalIgnoreCase)
            || !current.TakenSymbols.Contains(candidate));

    /// <summary>
    /// Название и описание — те, что ввёл человек: профиль модели может
    /// вернуть их переформулированными, а актив называется так, как его назвали.
    /// </summary>
    private static AssetProfile Named(AssetDraft draft, AssetProfile profile) =>
        profile with
        {
            Name = draft.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(draft.Description) ? profile.Description : draft.Description.Trim()
        };

    /// <summary>Ноль в форме — незаполненное поле: нулевой потенциал роста смысла не имеет.</summary>
    private static int? GrowthOf(AssetDraft draft) => draft.GrowthPotential > 0 ? draft.GrowthPotential : null;

    /// <summary>Состояние целиком. Заменяется, а не правится по полю.</summary>
    private sealed record Snapshot(
        IReadOnlyList<SessionAsset> Assets,
        IReadOnlySet<string> TakenSymbols,
        AssetDraft? Draft,
        int Revision);
}

/// <summary>Актив сессии: актив каталога под именами, которыми его зовут экраны.</summary>
public sealed record SessionAsset(Asset Asset)
{
    public Guid Id => Asset.AssetId;

    public string Symbol => Asset.Ticker;

    public string Name => Asset.Name;

    public string Description => Asset.Description;

    public AssetType AssetType => Asset.AssetType;

    public AssetProfile Profile => Asset.Profile;

    public decimal StartPrice => Asset.StartPrice;

    public DateTimeOffset CreatedAt => Asset.CreatedAt;

    /// <summary>Пусто — актив создан до того, как каталог начал хранить отрасль.</summary>
    public string? Industry => string.IsNullOrWhiteSpace(Asset.Industry) ? null : Asset.Industry;

    public bool IncludeGovernmentSupport => Asset.IncludeGovernmentSupport;

    public int? GrowthPotential => Asset.GrowthPotential;
}

/// <summary>То, что человек ввёл в форму «Нового актива».</summary>
public sealed record AssetDraft(
    string Name,
    string Description,
    AssetType AssetType,
    string Industry,
    bool IncludeGovernmentSupport,
    int GrowthPotential);
