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
builder.Services.AddABStockAI();
builder.Services.AddABStockPersistence(
    builder.Configuration.GetConnectionString("ABStock") ?? "Data Source=abstock.db");
// Актив сессии — singleton, как и сама симуляция: актив в сессии один, и он
// принадлежит прогону, а не вкладке. Scoped переживал переходы по ссылкам, но
// не перезагрузку страницы, не вход по прямому адресу и не переход со
// статически отрисованной приветственной: «Торги» в свежем контуре не
// находили актива и перезапускали прогон демонстрационной заглушкой посреди
// сессии, которая торгует настоящим активом.
builder.Services.AddSingleton<IActiveAssetContext, ActiveAssetContext>();
// Настройки интерфейса — scoped, и это осознанно: источник истины лежит в
// localStorage браузера, а сервис лишь кэш на время жизни контура. Singleton
// раздавал бы всем открытым вкладкам чужой акцент, потому что настройки
// принадлежат браузеру, а не серверу.
builder.Services.AddScoped<IUserPreferences, UserPreferences>();
// Хронология новостей сессии — одна на весь продукт (DESIGN.md 13):
// её читают и «Новости», и левый рельс «Торгов». Singleton, как и сама
// симуляция: лента живёт ровно столько же, сколько прогон, чьи события
// показывает, и переживает перезагрузку страницы вместе с ним.
builder.Services.AddSingleton<ISessionNewsFeed, SessionNewsFeed>();
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
