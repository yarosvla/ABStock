using ABStock.Application.Assets;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Активы сессии глазами интерфейса. Сами активы, их порядок и хранение —
/// каталог бэкенда (<see cref="IAssetCatalog"/>); реестр добавляет то, чего в
/// <see cref="Asset"/> пока нет, а экраны без этого не работают: тикер,
/// параметры формы, архив и предел в 8 активов.
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
    /// Описание изменилось — профиль собран заново. Тикер прежний; актив в
    /// каталоге бэкенда новый, потому что править актив бэкенд не умеет.
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
///
/// Тикер, параметры формы и архив живут в памяти процесса — бэкенд их не
/// хранит. После перезапуска тикеры собираются заново из названий в порядке
/// создания и выходят теми же; параметры формы теряются, и профиль актива
/// показывает вместо них прочерк. Это требование к контракту, а не решение.
/// </summary>
public sealed class AssetRegistry : IAssetRegistry
{
    private readonly IAssetCatalog _catalog;
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, AssetDraft> _drafts = [];
    private readonly HashSet<Guid> _archived = [];
    // Тикер, закреплённый за активом. Заполняется при загрузке и при
    // создании; тикер замещённого (Update) актива переходит к замене.
    private readonly Dictionary<Guid, string> _symbols = [];
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
            return FreeSymbolLocked(name, exceptSymbol);
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

            var symbol = FreeSymbolLocked(draft.Name, exceptSymbol: null);
            var asset = CreateInCatalog(draft, profile);
            _symbols[asset.AssetId] = symbol;
            _drafts[asset.AssetId] = draft;

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
            // адрес, свитчер и закладки. Прежний актив уходит в архив — его
            // прогоны остаются в истории под его же идентификатором.
            var asset = CreateInCatalog(draft, profile);
            _archived.Add(previous.Id);
            _symbols[asset.AssetId] = previous.Symbol;
            _drafts[asset.AssetId] = draft;
            _snapshot = Rebuild(current.Revision + 1, current.Draft);
            updated = Find(asset.AssetId)!;
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

            // Слот освобождается, тикер — нет: иначе прошлый прогон «GLEN» и
            // новый актив «GLEN» стали бы неотличимы.
            _archived.Add(asset.Id);
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

    /// <summary>
    /// Первое обращение читает каталог бэкенда: активы прошлых запусков
    /// сервера получают тикеры в том же порядке создания, что и в первый раз.
    /// </summary>
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
                return _snapshot ??= Rebuild(revision: 0, draft: null);
            }
        }
    }

    private Asset CreateInCatalog(AssetDraft draft, AssetProfile profile)
    {
        // Название и описание — те, что ввёл человек: профиль модели может
        // вернуть их переформулированными, а актив называется так, как его
        // назвали. Стартовую цену бэкенд пока не считает — её считает
        // интерфейс (StartPrices), и это тоже требование к контракту.
        var named = profile with
        {
            Name = draft.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(profile.Description) ? draft.Description.Trim() : profile.Description
        };

        return _catalog.Create(new CreateAssetRequest(named, StartPrices.Calculate(draft, profile)));
    }

    private Snapshot Rebuild(int revision, AssetDraft? draft)
    {
        var assets = new List<SessionAsset>();

        foreach (var asset in _catalog.GetAll().OrderBy(asset => asset.CreatedAt))
        {
            if (!_symbols.TryGetValue(asset.AssetId, out var symbol))
            {
                symbol = AssetSymbols.Candidates(asset.Name).First(candidate => !_symbols.ContainsValue(candidate));
                _symbols[asset.AssetId] = symbol;
            }

            if (_archived.Contains(asset.AssetId))
            {
                continue;
            }

            assets.Add(new SessionAsset(asset, symbol, _drafts.GetValueOrDefault(asset.AssetId)));
        }

        return new Snapshot(assets, draft, revision);
    }

    private string FreeSymbolLocked(string name, string? exceptSymbol)
    {
        _ = Current;
        // Занятые тикеры — и действующих, и архивных активов (см. Archive).
        var taken = _symbols.Values
            .Where(symbol => !string.Equals(symbol, exceptSymbol, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return AssetSymbols.Candidates(name).First(candidate => !taken.Contains(candidate));
    }

    /// <summary>Состояние целиком. Заменяется, а не правится по полю.</summary>
    private sealed record Snapshot(IReadOnlyList<SessionAsset> Assets, AssetDraft? Draft, int Revision);
}

/// <summary>
/// Актив сессии: актив бэкенда плюс то, что знает о нём интерфейс.
/// </summary>
/// <param name="Draft">
/// То, что человек ввёл в форму. Null у актива, созданного до перезапуска
/// сервера: бэкенд параметров формы не хранит.
/// </param>
public sealed record SessionAsset(Asset Asset, string Symbol, AssetDraft? Draft)
{
    public Guid Id => Asset.AssetId;

    public string Name => Asset.Name;

    public string Description => Draft?.Description ?? Asset.Description;

    public AssetType AssetType => Asset.AssetType;

    public AssetProfile Profile => Asset.Profile;

    public decimal StartPrice => Asset.StartPrice;

    public DateTimeOffset CreatedAt => Asset.CreatedAt;
}

/// <summary>То, что человек ввёл в форму «Нового актива».</summary>
public sealed record AssetDraft(
    string Name,
    string Description,
    AssetType AssetType,
    string Industry,
    bool IncludeGovernmentSupport,
    int GrowthPotential);
