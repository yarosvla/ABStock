/**
 * Гистограмма объёма под свечами — общий код «Торгов» и детальной агента.
 *
 * Вынесено сюда не ради экономии строк, а потому что это один и тот же
 * график в двух местах: раздел 11 требует, чтобы цена на всех экранах
 * рисовалась одинаково, и разъехавшиеся доли холста или разные alpha у
 * столбиков означали бы, что один и тот же объём выглядит по-разному.
 */

/** Идентификатор собственной ценовой шкалы объёма. */
export const VOLUME_SCALE_ID = "volume";

/**
 * Заводит серию объёма на графике. Гистограмма занимает нижние ~16 % холста
 * и живёт на своей ценовой шкале: общая с ценой шкала сплющила бы свечи.
 */
export function addVolumeSeries(chart) {
    const series = chart.addHistogramSeries({
        priceScaleId: VOLUME_SCALE_ID,
        priceLineVisible: false,
        lastValueVisible: false,
        priceFormat: { type: "volume" }
    });

    chart.priceScale(VOLUME_SCALE_ID).applyOptions({
        scaleMargins: { top: 0.84, bottom: 0 },
        borderVisible: false
    });

    return series;
}

/**
 * Точка гистограммы из нормализованной свечи (время уже локальное).
 *
 * Цвет задаётся точкой, а не темой серии: у гистограммы нет понятия
 * up/down. В графите это цвета свечи с alpha 0.30 (раздел 11); в белой
 * теме оба токена нейтрально-серые, а текущий столбик выделен
 * --volume-current (раздел 19).
 */
export function toVolumePoint(candle, theme, isCurrent = false) {
    return {
        time: candle.time,
        value: candle.volume ?? 0,
        color: isCurrent && theme.volumeCurrent
            ? theme.volumeCurrent
            : candle.close >= candle.open ? theme.upVolume : theme.downVolume
    };
}

/** Весь ряд объёма: последний столбик — текущий. */
export function toVolumePoints(candles, theme) {
    const last = candles.length - 1;
    return candles.map((candle, index) => toVolumePoint(candle, theme, index === last));
}
