namespace ABStock.UI.Services;

/// <summary>
/// Точка линейного ряда для lightweight-charts. Время — секунды Unix:
/// библиотека принимает только их и требует строго возрастающих значений.
/// </summary>
public sealed record ChartPoint(long Time, decimal Value);

/// <summary>
/// Подготовка рядов к отрисовке: прореживание и приведение шага по времени.
/// Живёт отдельно от страниц, потому что нужна и детальной агента, и списку
/// агентов: две копии этих трёх методов разъехались бы так же, как разъехались
/// три копии крошек.
/// </summary>
public static class ChartSeries
{
    /// <summary>
    /// Разредить плотный ряд до примерно maxPoints равномерным шагом.
    /// Последняя точка сохраняется всегда: на графике это текущее значение,
    /// и потерять его нельзя ни при каком шаге.
    /// </summary>
    public static List<ChartPoint> Downsample(List<ChartPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return points;
        }

        var step = (int)Math.Ceiling(points.Count / (double)maxPoints);
        var result = new List<ChartPoint>(maxPoints + 1);
        for (var i = 0; i < points.Count; i += step)
        {
            result.Add(points[i]);
        }

        if (result[^1].Time != points[^1].Time)
        {
            result.Add(points[^1]);
        }

        return result;
    }

    /// <summary>
    /// Свести ряд с секундной дискретностью к закрытиям выбранного интервала:
    /// в каждом интервале остаётся последнее значение.
    /// </summary>
    public static List<ChartPoint> BucketByInterval(List<ChartPoint> points, int intervalSec)
    {
        if (intervalSec <= 1 || points.Count == 0)
        {
            return points;
        }

        var result = new List<ChartPoint>();
        var currentBucket = points[0].Time / intervalSec;
        var last = points[0];

        foreach (var point in points)
        {
            var bucket = point.Time / intervalSec;
            if (bucket != currentBucket)
            {
                result.Add(last);
                currentBucket = bucket;
            }

            last = point;
        }

        result.Add(last);
        return result;
    }

    /// <summary>
    /// Оставить по одной точке на секунду — последнюю. lightweight-charts
    /// требует строго возрастающих и неповторяющихся ключей времени.
    /// </summary>
    public static List<ChartPoint> DedupeBySecond(List<ChartPoint> points)
    {
        var result = new List<ChartPoint>(points.Count);
        long? lastTime = null;

        foreach (var point in points)
        {
            if (lastTime == point.Time && result.Count > 0)
            {
                result[^1] = point;
            }
            else
            {
                result.Add(point);
                lastTime = point.Time;
            }
        }

        return result;
    }
}
