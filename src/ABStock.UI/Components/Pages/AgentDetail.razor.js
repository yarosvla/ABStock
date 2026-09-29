import {
    ColorType,
    CrosshairMode,
    LineStyle,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";
import { addVolumeSeries, toVolumePoints } from "/js/chart-volume.js";
import {
    lastPriceSeriesOptions,
    onThemeChange,
    readChartTheme,
    syncLastPriceLine
} from "/js/chart-theme.js";

const charts = new WeakMap();

const priceFormatter = new Intl.NumberFormat("ru-RU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
});

const quantityFormatter = new Intl.NumberFormat("ru-RU", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1
});

// Предел ширины свечи. Без него fitContent() растягивает несколько свечей на
// треть панели, и график перестаёт читаться как свечной. При нехватке данных
// ряд прижимается вправо, слева остаётся пустота — так делают реальные
// терминалы, и это не дефект (DESIGN.md 11).
const MAX_BAR_SPACING_PX = 14;

function fitWithBarLimit(chart, barCount, widthPx) {
    const timeScale = chart.timeScale();

    // Считаем сами, а не читаем barSpacing у библиотеки после fitContent:
    // так решение не зависит от того, успела ли она пересчитать раскладку.
    if (barCount * MAX_BAR_SPACING_PX >= widthPx) {
        timeScale.fitContent();
        return;
    }

    timeScale.applyOptions({ barSpacing: MAX_BAR_SPACING_PX });
    timeScale.scrollToRealTime();
}

// lightweight-charts трактует время как UTC. Сдвигаем метку на локальное
// смещение один раз, при нормализации, — тогда ось совпадает с часами
// приложения (DESIGN.md 11).
function toLocalChartTime(utcSeconds) {
    const seconds = Number(utcSeconds);
    if (!Number.isFinite(seconds)) {
        return utcSeconds;
    }

    return seconds - new Date(seconds * 1000).getTimezoneOffset() * 60;
}

/**
 * Токены детальной. Сетка и кромки здесь — --line-1/--line-2, а фитиль
 * полным цветом свечи: так детальная рисовалась и до переноса в токены, и
 * графит от переноса не меняется.
 */
function readTokens() {
    const theme = readChartTheme();

    return {
        ...theme,
        grid: theme.line1,
        border: theme.line2,
        up: theme.upFill,
        down: theme.downFill
    };
}

function getCanvasSize(element, fallbackHeight) {
    const rect = element.getBoundingClientRect();
    return {
        width: Math.max(1, Math.round(rect.width || 640)),
        height: Math.max(1, Math.round(rect.height || fallbackHeight))
    };
}

/** Цвета холста — всё, что меняется вместе с темой. */
function themeOptions(tokens) {
    const crosshairLine = {
        color: tokens.crosshair,
        style: LineStyle.Dashed,
        width: 1,
        labelBackgroundColor: tokens.labelBackground
    };

    return {
        layout: { textColor: tokens.text3 },
        grid: {
            vertLines: { color: tokens.grid, style: LineStyle.Solid },
            horzLines: { color: tokens.grid, style: LineStyle.Solid }
        },
        crosshair: { vertLine: crosshairLine, horzLine: { ...crosshairLine } },
        rightPriceScale: { borderColor: tokens.border },
        timeScale: { borderColor: tokens.border }
    };
}

function candleOptions(tokens) {
    return {
        upColor: tokens.up,
        downColor: tokens.down,
        wickUpColor: tokens.up,
        wickDownColor: tokens.down,
        ...lastPriceSeriesOptions(tokens)
    };
}

function syncPriceLine(bundle) {
    const last = bundle.candles.length > 0 ? bundle.candles[bundle.candles.length - 1] : null;
    syncLastPriceLine(bundle, bundle.series, bundle.tokens, Number(last?.close));
}

/** Общая тема раздела 11: та же, что на «Торгах». */
function baseOptions(tokens, size) {
    const colors = themeOptions(tokens);

    return {
        width: size.width,
        height: size.height,
        autoSize: false,
        layout: {
            background: { type: ColorType.Solid, color: "transparent" },
            textColor: colors.layout.textColor,
            fontFamily: "'JetBrains Mono', ui-monospace, 'SF Mono', monospace",
            fontSize: 11,
            attributionLogo: false
        },
        localization: {
            locale: "ru-RU",
            priceFormatter: value => priceFormatter.format(value)
        },
        grid: colors.grid,
        crosshair: {
            mode: CrosshairMode.Magnet,
            ...colors.crosshair
        },
        rightPriceScale: {
            borderVisible: true,
            borderColor: tokens.border,
            scaleMargins: { top: 0.12, bottom: 0.12 }
        },
        leftPriceScale: { visible: false },
        timeScale: {
            borderVisible: true,
            borderColor: tokens.border,
            timeVisible: true,
            secondsVisible: true
        }
    };
}

function attachResize(chart, element) {
    const observer = new ResizeObserver(entries => {
        const entry = entries.find(item => item.target === element) ?? entries[0];
        if (!entry) {
            return;
        }

        chart.resize(
            Math.max(1, Math.round(entry.contentRect.width)),
            Math.max(1, Math.round(entry.contentRect.height)));
    });
    observer.observe(element);
    return observer;
}

function normalizeCandles(candles) {
    if (!Array.isArray(candles)) {
        return [];
    }

    return candles
        .map(c => ({
            time: toLocalChartTime(c.time ?? c.Time),
            open: Number(c.open ?? c.Open),
            high: Number(c.high ?? c.High),
            low: Number(c.low ?? c.Low),
            close: Number(c.close ?? c.Close),
            volume: Number(c.volume ?? c.Volume ?? 0)
        }))
        .filter(c => Number.isFinite(c.time) && Number.isFinite(c.close))
        .sort((a, b) => a.time - b.time);
}

function normalizeLine(points) {
    if (!Array.isArray(points)) {
        return [];
    }

    return points
        .map(p => ({
            time: toLocalChartTime(p.time ?? p.Time),
            value: Number(p.value ?? p.Value)
        }))
        .filter(p => Number.isFinite(p.time) && Number.isFinite(p.value))
        .sort((a, b) => a.time - b.time);
}

function normalizeTrades(trades) {
    if (!Array.isArray(trades)) {
        return [];
    }

    return trades
        .map(t => ({
            index: Number(t.index ?? t.Index),
            time: toLocalChartTime(t.time ?? t.Time),
            isBuy: Boolean(t.isBuy ?? t.IsBuy),
            price: Number(t.price ?? t.Price),
            quantity: Number(t.quantity ?? t.Quantity)
        }))
        .filter(t => Number.isFinite(t.time))
        .sort((a, b) => a.time - b.time);
}

/**
 * Маркер на сделку, один к одному: связка «сделка ↔ маркер» — главное
 * действие страницы, и агрегировать сделки в корзины здесь нельзя, иначе
 * число маркеров перестанет совпадать с числом строк списка (раздел 10.1).
 *
 * Покупка — заливка цветом агента, продажа — приглушённый вариант того же
 * цвета: сторона различается насыщенностью, а не рыночным зелёным и красным,
 * потому что маркеры принадлежат агенту (раздел 11).
 */
function buildMarkers(bundle) {
    const { trades, activeIndex, tone } = bundle;

    return trades.map(trade => {
        const isActive = trade.index === activeIndex;

        return {
            time: trade.time,
            position: trade.isBuy ? "belowBar" : "aboveBar",
            color: isActive ? bundle.tokens.text1 : (trade.isBuy ? tone.on : tone.off),
            shape: "circle",
            size: isActive ? 2 : 1,
            text: isActive
                ? `${trade.isBuy ? "покупка" : "продажа"} ${quantityFormatter.format(trade.quantity)} по ${priceFormatter.format(trade.price)}`
                : ""
        };
    });
}

export function renderPrice(element, payload, dotNetRef) {
    if (!element) {
        return;
    }

    const tokens = readTokens();
    let bundle = charts.get(element);

    if (!bundle) {
        const chart = createChart(element, baseOptions(tokens, getCanvasSize(element, 300)));

        const series = chart.addCandlestickSeries({
            ...candleOptions(tokens),
            borderVisible: false,
            priceLineStyle: LineStyle.Dashed,
            priceLineWidth: 1,
            lastValueVisible: true
        });

        const volumeSeries = addVolumeSeries(chart);

        bundle = {
            chart,
            series,
            volumeSeries,
            tokens,
            trades: [],
            candles: [],
            activeIndex: -1,
            toneKey: "trend",
            tone: tokens.agent.trend,
            resizeObserver: attachResize(chart, element)
        };

        // Смена темы перекрашивает готовый график без перезагрузки.
        bundle.themeUnsubscribe = onThemeChange(() => {
            bundle.tokens = readTokens();
            bundle.tone = bundle.tokens.agent[bundle.toneKey] ?? bundle.tokens.agent.trend;
            bundle.chart.applyOptions(themeOptions(bundle.tokens));
            bundle.series.applyOptions(candleOptions(bundle.tokens));
            bundle.volumeSeries.setData(toVolumePoints(bundle.candles, bundle.tokens));
            bundle.series.setMarkers(buildMarkers(bundle));
            syncPriceLine(bundle);
        });

        // Обратная сторона связки: клик по холсту выбирает ближайшую по времени
        // сделку, и строка в рельсе подсвечивается вслед за маркером.
        chart.subscribeClick(param => {
            if (!param?.time || bundle.trades.length === 0 || !bundle.dotNetRef) {
                return;
            }

            const clicked = Number(param.time);
            let nearest = bundle.trades[0];
            for (const trade of bundle.trades) {
                if (Math.abs(trade.time - clicked) < Math.abs(nearest.time - clicked)) {
                    nearest = trade;
                }
            }

            bundle.dotNetRef.invokeMethodAsync("SelectTradeFromChart", nearest.index);
        });

        charts.set(element, bundle);
    }

    bundle.dotNetRef = dotNetRef ?? bundle.dotNetRef;
    bundle.tokens = tokens;
    bundle.toneKey = payload?.tone ?? payload?.Tone ?? "trend";
    bundle.tone = tokens.agent[bundle.toneKey] ?? tokens.agent.trend;
    bundle.trades = normalizeTrades(payload?.trades ?? payload?.Trades);

    const candles = normalizeCandles(payload?.candles ?? payload?.Candles);
    bundle.candles = candles;
    bundle.series.setData(candles);
    bundle.volumeSeries.setData(toVolumePoints(candles, tokens));
    syncPriceLine(bundle);
    bundle.series.setMarkers(buildMarkers(bundle));

    if (candles.length > 0) {
        fitWithBarLimit(bundle.chart, candles.length, getCanvasSize(element, 300).width);
    }
}

/**
 * Смена выбранной сделки перерисовывает только маркеры: пересобирать ряд
 * свечей на каждое движение мыши по списку незачем.
 */
export function setActiveTrade(element, index) {
    const bundle = charts.get(element);
    if (!bundle) {
        return;
    }

    bundle.activeIndex = Number.isInteger(index) ? index : -1;
    bundle.series.setMarkers(buildMarkers(bundle));
}

export function renderEquity(element, points, toneKey) {
    if (!element) {
        return;
    }

    const tokens = readTokens();
    let bundle = charts.get(element);

    if (!bundle) {
        const chart = createChart(element, baseOptions(tokens, getCanvasSize(element, 150)));
        chart.applyOptions({ timeScale: { visible: false } });

        const series = chart.addLineSeries({
            lineWidth: 1.5,
            priceLineVisible: false,
            lastValueVisible: true,
            crosshairMarkerVisible: false
        });

        bundle = { chart, series, tokens, resizeObserver: attachResize(chart, element) };

        bundle.themeUnsubscribe = onThemeChange(() => {
            bundle.tokens = readTokens();
            bundle.chart.applyOptions(themeOptions(bundle.tokens));
            bundle.series.applyOptions({
                color: (bundle.tokens.agent[bundle.toneKey] ?? bundle.tokens.agent.trend).on
            });
        });

        charts.set(element, bundle);
    }

    bundle.toneKey = toneKey;
    const tone = tokens.agent[toneKey] ?? tokens.agent.trend;
    bundle.series.applyOptions({ color: tone.on });

    const data = normalizeLine(points);
    bundle.series.setData(data);

    if (data.length > 0) {
        bundle.chart.timeScale().fitContent();
    }
}

export function dispose(element) {
    const bundle = charts.get(element);
    if (!bundle) {
        return;
    }

    bundle.resizeObserver?.disconnect();
    bundle.themeUnsubscribe?.();
    bundle.chart.remove();
    charts.delete(element);
}
