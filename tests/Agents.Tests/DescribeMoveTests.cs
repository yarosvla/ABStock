using ABStock.Agents;
using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

/// <summary>
/// DESIGN.md 10: текст никогда не показывает «A → A». Движение цены часто
/// происходит в третьем знаке, а цена печатается с двумя.
/// </summary>
public sealed class DescribeMoveTests
{
    // DescribeMove защищённый — дотягиваемся через наследника-пробу.
    private sealed class Probe : TrendFollowingAgent
    {
        public Probe() : base(1000m) { }
        public static string Call(decimal from, decimal to) => Describe(from, to);
        private static string Describe(decimal from, decimal to) =>
            (string)typeof(AgentBase)
                .GetMethod("DescribeMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [from, to])!;
    }

    [Fact]
    public void DifferentAfterRounding_ShowsArrow()
    {
        // Разделитель зависит от культуры процесса, поэтому сверяем форму,
        // а не буквальную строку: в приложении культура ru-RU (Program.cs).
        var text = Probe.Call(124.97m, 125.28m);

        Assert.Contains("→", text);
        Assert.Contains(124.97m.ToString("F2"), text);
        Assert.Contains(125.28m.ToString("F2"), text);
    }

    [Theory]
    [InlineData(124.971, 124.974)]
    [InlineData(124.9700, 124.9749)]
    [InlineData(100.000, 100.0004)]
    public void SameAfterRounding_NeverPrintsAToA(decimal from, decimal to)
    {
        var text = Probe.Call(from, to);
        var shown = Math.Round(to, 2, MidpointRounding.AwayFromZero).ToString("F2");

        Assert.DoesNotContain($"{shown} → {shown}", text);
        Assert.DoesNotContain("→", text);
    }

    [Fact]
    public void IdenticalPrices_SaysNoChange() =>
        Assert.Contains("без изменения", Probe.Call(124.97m, 124.97m));
}
