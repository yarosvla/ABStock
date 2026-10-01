using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Спарклайны «Активов» рисуются на общей шкале (DESIGN.md 9.25): иначе
/// облигация выглядит такой же бурной, как акция.
/// </summary>
public sealed class SparklineScaleTests
{
    [Fact]
    public void Шкала_одна_на_всех_и_берёт_наибольшее_отклонение()
    {
        // KVAN ушёл на −2,27 %, BLTR — на 0,1 %: шкала по KVAN, вверх до 0,5.
        var half = SparklineScale.HalfRange(
        [
            (124.35m, [124.35m, 121.53m, 122.61m]),
            (98.40m, [98.40m, 98.50m])
        ]);

        Assert.Equal(2.5m, half);
    }

    [Fact]
    public void Шкала_не_бывает_уже_полупроцента() =>
        Assert.Equal(SparklineScale.MinHalfRange, SparklineScale.HalfRange([(100m, [100m, 100.01m])]));

    [Fact]
    public void Без_рядов_шкала_минимальная() =>
        Assert.Equal(SparklineScale.MinHalfRange, SparklineScale.HalfRange([]));

    [Fact]
    public void Нулевое_открытие_не_делит_на_ноль() =>
        Assert.Equal(SparklineScale.MinHalfRange, SparklineScale.HalfRange([(0m, [1m, 2m])]));
}
