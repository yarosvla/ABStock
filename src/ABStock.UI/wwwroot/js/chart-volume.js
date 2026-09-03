/**
 * Гистограмма объёма под свечами — общий код «Торгов» и детальной агента.
 *
 * Вынесено сюда не ради экономии строк, а потому что это один и тот же
 * график в двух местах: раздел 11 требует, чтобы цена на всех экранах
 * рисовалась одинаково, и разъехавшиеся доли холста или разные alpha у
 * столбиков означали бы, что один и тот же объём выглядит по-разному.
 */

// Те же цвета, что у свечи, с alpha 0.30 (раздел 11). Цвет столбика задаётся
// точкой, а не темой серии: он зависит от направления свечи, а у гистограммы
// нет понятия up/down.
export const VOLUME_UP = "rgba(63, 163, 122, 0.30)";
export const VOLUME_DOWN = "rgba(210, 85, 95, 0.30)";

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

/** Точка гистограммы из нормализованной свечи (время уже локальное). */
export function toVolumePoint(candle) {
    return {
        time: candle.time,
        value: candle.volume ?? 0,
        color: candle.close >= candle.open ? VOLUME_UP : VOLUME_DOWN
    };
}
