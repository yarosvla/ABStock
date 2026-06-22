using ABStock.AI.Extensions;
using ABStock.Application.Extensions;
using ABStock.Persistence.Extensions;
using ABStock.UI.Components;
using ABStock.UI.Services;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

// Add services to the container.
builder.Services.AddABStockApplication();
builder.Services.AddABStockAI();
builder.Services.AddABStockPersistence(
    builder.Configuration.GetConnectionString("ABStock") ?? "Data Source=abstock.db");
builder.Services.AddScoped<IActiveAssetContext, ActiveAssetContext>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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
