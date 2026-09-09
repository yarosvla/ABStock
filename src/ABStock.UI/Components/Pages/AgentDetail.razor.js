import {
    ColorType,
    CrosshairMode,
    LineStyle,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";
import { addVolumeSeries, toVolumePoint } from "/js/chart-volume.js";

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

function readTokens() {
    const root = getComputedStyle(document.documentElement);
    const token = name => root.getPropertyValue(name).trim();

    return {
        text3: token("--text-3"),
        text1: token("--text-1"),
        grid: token("--line-1"),
        border: token("--line-2"),
        up: token("--up-500"),
        down: token("--down-500"),
        agent: {
            trend: { on: token("--agent-trend"), off: token("--agent-trend-dim") },
            counter: { on: token("--agent-counter"), off: token("--agent-counter-dim") },
            mm: { on: token("--agent-mm"), off: token("--agent-mm-dim") },
            news: { on: token("--agent-news"), off: token("--agent-news-dim") }
        }
    };
}

function getCanvasSize(element, fallbackHeight) {
    const rect = element.getBoundingClientRect();
    return {
        width: Math.max(1, Math.round(rect.width || 640)),
        height: Math.max(1, Math.round(rect.height || fallbackHeight))
    };
}

/** Общая тема раздела 11: та же, что на «Торгах». */
function baseOptions(tokens, size) {
    return {
        width: size.width,
        height: size.height,
        autoSize: false,
        layout: {
            background: { type: ColorType.Solid, color: "transparent" },
            textColor: tokens.text3,
            fontFamily: "'JetBrains Mono', ui-monospace, 'SF Mono', monospace",
            fontSize: 11,
            attributionLogo: false
        },
        localization: {
            locale: "ru-RU",
            priceFormatter: value => priceFormatter.format(value)
        },
        grid: {
            vertLines: { color: tokens.grid, style: LineStyle.Solid },
            horzLines: { color: tokens.grid, style: LineStyle.Solid }
        },
        crosshair: {
            mode: CrosshairMode.Magnet,
            vertLine: {
                color: "rgba(255, 255, 255, 0.28)",
                style: LineStyle.Dashed,
                width: 1,
                labelBackgroundColor: "#292C30"
            },
            horzLine: {
                color: "rgba(255, 255, 255, 0.28)",
                style: LineStyle.Dashed,
                width: 1,
                labelBackgroundColor: "#292C30"
            }
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
            upColor: tokens.up,
            downColor: tokens.down,
            borderVisible: false,
            wickUpColor: tokens.up,
            wickDownColor: tokens.down,
            priceLineVisible: true,
            priceLineColor: "rgba(255, 255, 255, 0.45)",
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
            activeIndex: -1,
            tone: tokens.agent.trend,
            resizeObserver: attachResize(chart, element)
        };

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
    bundle.tone = tokens.agent[payload?.tone ?? payload?.Tone] ?? tokens.agent.trend;
    bundle.trades = normalizeTrades(payload?.trades ?? payload?.Trades);

    const candles = normalizeCandles(payload?.candles ?? payload?.Candles);
    bundle.series.setData(candles);
    bundle.volumeSeries.setData(candles.map(toVolumePoint));
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
        charts.set(element, bundle);
    }

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
    bundle.chart.remove();
    charts.delete(element);
}
