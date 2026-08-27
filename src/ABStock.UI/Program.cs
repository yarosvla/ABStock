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
builder.Services.AddScoped<IActiveAssetContext, ActiveAssetContext>();
// Хронология новостей сессии — одна на весь продукт (DESIGN.md 13):
// её читают и «Новости», и левый рельс «Торгов». Singleton, как и сама
// симуляция: лента живёт ровно столько же, сколько прогон, чьи события
// показывает, и переживает перезагрузку страницы вместе с ним.
builder.Services.AddSingleton<ISessionNewsFeed, SessionNewsFeed>();
// Стоимость портфеля по типам агентов с начала прогона — тоже singleton и по
// той же причине. Читает её страница «Агенты».
builder.Services.AddSingleton<IAgentEquityHistory, AgentEquityHistory>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddABStockSimulationDiagnostics();
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// В историю портфеля пишет тик, а не страница, поэтому подписка на OnTick
// должна существовать до первого тика. Ленивое создание отдало бы сервису
// первый тик только после того, как кто-то откроет «Агентов», — начало
// сессии было бы потеряно, а 100 % отсчитывались бы от середины прогона.
_ = app.Services.GetRequiredService<IAgentEquityHistory>();

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
