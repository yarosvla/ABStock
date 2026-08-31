using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Контекст актива стал singleton-ом: актив принадлежит прогону, а не вкладке,
/// и обязан переживать перезагрузку страницы, вход по прямому адресу и переход
/// со статически отрисованной приветственной. Плата за это — несколько контуров
/// читают и правят один экземпляр, поэтому состояние заменяется целым снимком.
/// </summary>
public sealed class ActiveAssetContextTests
{
    [Fact]
    public void SetDraft_DropsProfile()
    {
        var context = new ActiveAssetContext();
        var draft = Draft("Гелиос Энерго");
        context.SetProfile(draft, ActiveAssetDefaults.BuildProfile(draft));

        // Описание изменилось — построенный по старому описанию профиль устарел.
        context.SetDraft(Draft("Северная Руда"));

        Assert.Null(context.Profile);
        Assert.Equal("Северная Руда", context.Draft?.Name);
    }

    [Fact]
    public void Clear_ReturnsToDemoContext()
    {
        var context = new ActiveAssetContext();
        var draft = Draft("Гелиос Энерго");
        context.SetProfile(draft, ActiveAssetDefaults.BuildProfile(draft));

        context.Clear();

        Assert.Null(context.Draft);
        Assert.Null(context.Profile);
        Assert.True(context.GetView().IsFallback);
    }

    [Fact]
    public void Revision_IncrementsOnEveryChange()
    {
        var context = new ActiveAssetContext();

        Assert.Equal(0, context.Revision);

        context.SetDraft(Draft("Гелиос Энерго"));
        Assert.Equal(1, context.Revision);

        context.SetProfile(Draft("Гелиос Энерго"), ActiveAssetDefaults.BuildProfile(Draft("Гелиос Энерго")));
        Assert.Equal(2, context.Revision);

        context.Clear();
        Assert.Equal(3, context.Revision);
    }

    /// <summary>
    /// Главная проверка общего экземпляра: вид не склеивает черновик одной
    /// правки с профилем другой. Инвариант — имя в профиле всегда совпадает с
    /// именем в черновике, и в заглушке тоже, потому что запасной профиль
    /// строится из того же черновика. Разъехавшиеся имена означают, что
    /// читатель поймал состояние на середине записи.
    /// </summary>
    [Fact]
    public async Task GetView_NeverMixesDraftAndProfileFromDifferentWrites()
    {
        var context = new ActiveAssetContext();
        var names = new[] { "Гелиос Энерго", "Северная Руда", "Волга Агро", "Ладога Порт" };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var writer = Task.Run(() =>
        {
            var index = 0;

            while (!cts.IsCancellationRequested)
            {
                var draft = Draft(names[index++ % names.Length]);

                // Три вида записи вперемешку — как их зовёт «Создание актива».
                context.SetDraft(draft);
                context.SetProfile(draft, ActiveAssetDefaults.BuildProfile(draft));
                context.Clear();
            }
        }, CancellationToken.None);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var view = context.GetView();
                Assert.Equal(view.Draft.Name, view.Profile.Name);
            }
        }, CancellationToken.None)).ToArray();

        await writer;
        await Task.WhenAll(readers);
    }

    private static ActiveAssetDraft Draft(string name) =>
        new(
            name,
            $"Описание актива «{name}» для проверки контекста.",
            AssetType.Stock,
            "Энергетика",
            true,
            80);
}
