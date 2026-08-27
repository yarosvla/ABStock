import {
    ColorType,
    CrosshairMode,
    LineStyle,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";

const charts = new WeakMap();

// Порядок создания задаёт порядок отрисовки: трендовая линия рисуется
// последней и потому лежит поверх остальных, как на макете.
const TYPES = ["news", "mm", "counter", "trend"];

const percentFormatter = new Intl.NumberFormat("ru-RU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
});

// lightweight-charts подписывает ось в UTC. Чтобы ось совпадала с часами
// приложения, метку сдвигаем на локальное смещение один раз — при
// нормализации точки (тот же приём, что на «Торгах»). Своего форматтера
// времени здесь нет намеренно: он перекрыл бы secondsVisible, которым
// подписи оси подстраиваются под длину сессии.
function toLocalChartTime(utcSeconds) {
    const seconds = Number(utcSeconds);
    if (!Number.isFinite(seconds)) {
        return utcSeconds;
    }

    return seconds - new Date(seconds * 1000).getTimezoneOffset() * 60;
}

/**
 * Цвета типов берём готовыми парами из design-system.css: обычный и
 * приглушённый. Подсветка становится подстановкой строки — ни разбора hex,
 * ни вычисления прозрачности на стороне графика.
 */
function readPalette() {
    const root = getComputedStyle(document.documentElement);
    const token = name => root.getPropertyValue(name).trim();

    return {
        trend: { on: token("--agent-trend"), off: token("--agent-trend-dim") },
        counter: { on: token("--agent-counter"), off: token("--agent-counter-dim") },
        mm: { on: token("--agent-mm"), off: token("--agent-mm-dim") },
        news: { on: token("--agent-news"), off: token("--agent-news-dim") },
        grid: token("--line-1"),
        baseline: token("--line-2"),
        axisText: token("--text-3")
    };
}

function getCanvasSize(element) {
    const rect = element.getBoundingClientRect();
    return {
        width: Math.max(1, Math.round(rect.width || 640)),
        height: Math.max(1, Math.round(rect.height || 150))
    };
}

function normalizePoints(points) {
    if (!Array.isArray(points)) {
        return [];
    }

    return points
        .map(point => ({
            time: toLocalChartTime(point.time ?? point.Time),
            value: Number(point.value ?? point.Value)
        }))
        .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value))
        .sort((a, b) => a.time - b.time);
}

function createBundle(element) {
    const palette = readPalette();
    const size = getCanvasSize(element);

    const chart = createChart(element, {
        width: size.width,
        height: size.height,
        autoSize: false,
        layout: {
            background: { type: ColorType.Solid, color: "transparent" },
            textColor: palette.axisText,
            fontFamily: "'JetBrains Mono', ui-monospace, 'SF Mono', monospace",
            fontSize: 11,
            attributionLogo: false
        },
        localization: {
            locale: "ru-RU",
            priceFormatter: value => percentFormatter.format(value)
        },
        grid: {
            vertLines: { visible: false },
            horzLines: { color: palette.grid, style: LineStyle.Solid }
        },
        crosshair: { mode: CrosshairMode.Hidden },
        handleScroll: false,
        handleScale: false,
        // Проценты стоят СЛЕВА: справа лежит легенда, и подписи оси слипались
        // бы с её строками.
        leftPriceScale: {
            visible: true,
            borderVisible: false,
            scaleMargins: { top: 0.14, bottom: 0.14 }
        },
        rightPriceScale: { visible: false },
        timeScale: {
            borderVisible: false,
            timeVisible: true,
            secondsVisible: false,
            // fixLeftEdge не ставим: он прибивает подпись к самой первой точке
            // поверх ближайшей регулярной, и слева получаются две метки друг
            // на друге. Прокрутка тут всё равно выключена, удерживать край
            // не от чего.
            fixLeftEdge: false,
            fixRightEdge: false
        }
    });

    const series = {};
    for (const type of TYPES) {
        series[type] = chart.addLineSeries({
            color: palette[type].on,
            // Линия без заливки под ней и без точек на изломах: заливка
            // превратила бы четыре ряда в кашу наложенных полупрозрачностей.
            lineWidth: 1.5,
            priceLineVisible: false,
            lastValueVisible: false,
            crosshairMarkerVisible: false,
            pointMarkersVisible: false
        });
    }

    // Уровень 100 % — тонкая линия цветом --line-2. Подписи у неё нет:
    // значение 100,00 на оси говорит то же самое, а текст рядом налезал бы
    // на него.
    series.trend.createPriceLine({
        price: 100,
        color: palette.baseline,
        lineWidth: 1,
        lineStyle: LineStyle.Solid,
        axisLabelVisible: false,
        title: ""
    });

    const resizeObserver = new ResizeObserver(entries => {
        const entry = entries.find(item => item.target === element) ?? entries[0];
        if (!entry) {
            return;
        }

        chart.resize(
            Math.max(1, Math.round(entry.contentRect.width)),
            Math.max(1, Math.round(entry.contentRect.height)));
    });
    resizeObserver.observe(element);

    return { chart, series, palette, resizeObserver, highlight: null };
}

export function render(element, payload) {
    if (!element) {
        return;
    }

    let bundle = charts.get(element);
    if (!bundle) {
        bundle = createBundle(element);
        charts.set(element, bundle);
    }

    const data = payload?.series ?? payload?.Series ?? {};
    let first = Infinity;
    let last = -Infinity;

    for (const type of TYPES) {
        const points = normalizePoints(data[type]);
        bundle.series[type].setData(points);

        if (points.length > 0) {
            first = Math.min(first, points[0].time);
            last = Math.max(last, points[points.length - 1].time);
        }
    }

    // На короткой сессии все подписи оси вида ЧЧ:ММ совпадают — ось
    // превращается в ряд одинаковых чисел. Секунды показываем ровно до тех
    // пор, пока минут не хватает, чтобы отличить метки друг от друга.
    const spanSec = Number.isFinite(first) ? last - first : 0;
    bundle.chart.timeScale().applyOptions({ secondsVisible: spanSec < 600 });

    bundle.chart.timeScale().fitContent();
    applyHighlight(bundle, bundle.highlight);
}

/**
 * Подсветка линии одного типа: остальные гаснут до приглушённого варианта.
 * Серии перерисовываются подстановкой цвета, а не пересозданием графика, —
 * иначе каждое движение мыши стоило бы полной отрисовки холста.
 */
export function setHighlight(element, type) {
    const bundle = charts.get(element);
    if (!bundle) {
        return;
    }

    bundle.highlight = TYPES.includes(type) ? type : null;
    applyHighlight(bundle, bundle.highlight);
}

function applyHighlight(bundle, highlight) {
    for (const type of TYPES) {
        const shade = highlight === null || highlight === type ? "on" : "off";
        bundle.series[type].applyOptions({ color: bundle.palette[type][shade] });
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
