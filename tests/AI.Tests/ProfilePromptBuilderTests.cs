using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Tests;

/// <summary>
/// Промпт — raw-строка <c>$$"""</c>: подстановка в ней пишется двойными
/// фигурными скобками. С одинарными в модель уходил текст «{request.Name}»,
/// и GPT сочинял профиль про случайную компанию.
/// </summary>
public class ProfilePromptBuilderTests
{
    [Fact]
    public void Prompt_contains_asset_context()
    {
        var prompt = new ProfilePromptBuilder().BuildPrompt(new AssetProfileRequest
        {
            AssetType = AssetType.Stock,
            Name = "Гелиос Энерго",
            Description = "Региональный энергетический холдинг.",
            Industry = "Энергетика"
        });

        Assert.Contains("Name: Гелиос Энерго", prompt);
        Assert.Contains("Description: Региональный энергетический холдинг.", prompt);
        Assert.Contains("Industry: Энергетика", prompt);
        Assert.DoesNotContain("{request.", prompt);
    }
}
