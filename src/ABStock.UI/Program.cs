using ABStock.AI.Extensions;
using ABStock.Application.Extensions;
using ABStock.Persistence.Extensions;
using ABStock.UI.Components;
using ABStock.UI.Services;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using System.Globalization;

// Язык интерфейса — ru-RU: запятая как дробный разделитель (DESIGN.md 10).
// Групповой разделитель заменён на узкий неразрывный пробел (U+202F) вместо
// обычного неразрывного, которого требует раздел 10 для разрядов: 1 324 500.
var uiCulture = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
uiCulture.NumberFormat.NumberGroupSeparator = "\u202F";
uiCulture.NumberFormat.CurrencyGroupSeparator = "\u202F";
uiCulture.NumberFormat.PercentGroupSeparator = "\u202F";
CultureInfo.DefaultThreadCurrentCulture = uiCulture;
CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.
builder.Services.AddABStockApplication();
builder.Services.AddABStockAI(builder.Configuration);
builder.Services.AddABStockPersistence(
    builder.Configuration.GetConnectionString("ABStock") ?? "Data Source=abstock.db");
// Активы сессии — поверх каталога бэкенда, который их хранит. Singleton:
// активы принадлежат сессии, а не вкладке, и переживают перезагрузку
// страницы и вход по прямому адресу. Тикеры и параметры формы бэкенд пока не хранит — их держит реестр.
builder.Services.AddSingleton<IAssetRegistry, AssetRegistry>();
// Выбранный актив — scoped: это свойство вкладки (адрес и localStorage), а
// не сервера. Singleton переключал бы актив у всех открытых вкладок разом.
builder.Services.AddScoped<ISelectedAsset, SelectedAsset>();
// Рынки сессии и её хронология — singleton, как раннер, итог которого они
// держат после остановки торгов.
builder.Services.AddSingleton<ISessionMarkets, SessionMarkets>();
builder.Services.AddSingleton<ISessionEvents, SessionEvents>();
// Состав агентов — один на сессию и живёт в пульте «Активов».
builder.Services.AddSingleton<IAgentComposition, AgentComposition>();
// Ввод новости — scoped: состояния у него нет, а анализатор новостей
// зарегистрирован модулем AI со своим временем жизни.
builder.Services.AddScoped<INewsDesk, NewsDesk>();
// Настройки интерфейса — scoped, и это осознанно: источник истины лежит в
// localStorage браузера, а сервис лишь кэш на время жизни контура. Singleton
// раздавал бы всем открытым вкладкам чужой акцент, потому что настройки
// принадлежат браузеру, а не серверу.
builder.Services.AddScoped<IUserPreferences, UserPreferences>();
// Стоимость портфеля по типам агентов с начала прогона — тоже singleton и по
// той же причине. Читает её страница «Агенты».
builder.Services.AddSingleton<IAgentEquityHistory, AgentEquityHistory>();
// Лента уведомлений колокольчика — singleton по тем же двум причинам:
// показывает прогон, а прогон один на сервер, и пишет в неё тик.
builder.Services.AddSingleton<INotificationFeed, NotificationFeed>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// В историю портфеля пишет тик, а не страница, поэтому подписка на OnTick
// должна существовать до первого тика. Ленивое создание отдало бы сервису
// первый тик только после того, как кто-то откроет «Агентов», — начало
// сессии было бы потеряно, а 100 % отсчитывались бы от середины прогона.
_ = app.Services.GetRequiredService<IAgentEquityHistory>();

// Лента уведомлений — по той же причине: запуск торгов и первые переходы
// позиций через ноль случаются раньше, чем кто-нибудь откроет колокольчик.
_ = app.Services.GetRequiredService<INotificationFeed>();

// Рынки и хронология сессии — тоже до первого тика: иначе цена открытия,
// ряд спарклайна и строка «Торги запущены» начинались бы с того момента,
// когда кто-нибудь откроет «Активы».
_ = app.Services.GetRequiredService<ISessionMarkets>();
_ = app.Services.GetRequiredService<ISessionEvents>();
_ = app.Services.GetRequiredService<IAgentComposition>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
