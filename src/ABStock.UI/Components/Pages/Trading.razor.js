import {
    ColorType,
    CrosshairMode,
    LineStyle,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";
import { addVolumeSeries, toVolumePoint } from "/js/chart-volume.js";

const FOLLOW_THRESHOLD_PX = 24;

// Высота ценовой метки: соседняя метка шкалы, попавшая в этот зазор,
// скрывается, чтобы не наезжать на метку текущей цены.
const PRICE_LABEL_GUARD_PX = 14;

// Предел ширины свечи. Без него fitContent() растягивает несколько свечей
// на треть панели, и график перестаёт читаться как свечной. При нехватке
// данных ряд прижимается вправо, слева остаётся пустота — так делают
// реальные терминалы, и это не дефект (DESIGN.md 11).
const MAX_BAR_SPACING_PX = 14;

const controllers = new WeakMap();

const timeframeConfig = {
    "10s": { barSpacing: 13, minVisibleBars: 18, timeVisible: true, secondsVisible: true },
    "30s": { barSpacing: 13, minVisibleBars: 18, timeVisible: true, secondsVisible: true },
    "1m": { barSpacing: 12, minVisibleBars: 20, timeVisible: true, secondsVisible: false },
    "5m": { barSpacing: 11, minVisibleBars: 22, timeVisible: true, secondsVisible: false },
    "15m": { barSpacing: 10, minVisibleBars: 24, timeVisible: true, secondsVisible: false },
    "1h": { barSpacing: 9, minVisibleBars: 24, timeVisible: true, secondsVisible: false }
};

const defaultTimeframe = timeframeConfig["30s"];
const priceFormatter = new Intl.NumberFormat("ru-RU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
});
// lightweight-charts трактует любой timestamp как UTC и подписывает ось в UTC.
// Чтобы ось совпадала с часами приложения, сдвигаем метку на локальное смещение
// один раз — при нормализации точки. После сдвига значение «уже локальное»,
// поэтому и форматтер подписи работает в UTC, иначе сдвиг применился бы дважды.
const timeFormatter = new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    timeZone: "UTC"
});

function toLocalChartTime(utcSeconds) {
    const seconds = Number(utcSeconds);
    if (!Number.isFinite(seconds)) {
        return utcSeconds;
    }

    // getTimezoneOffset отдаёт минуты, которые надо прибавить к локальному
    // времени, чтобы получить UTC — значит вычитаем их, чтобы получить локальное.
    return seconds - new Date(seconds * 1000).getTimezoneOffset() * 60;
}

function getTimeframeOptions(timeframe) {
    return timeframeConfig[timeframe] ?? defaultTimeframe;
}

function getController(root) {
    return root ? controllers.get(root) ?? null : null;
}

function getCanvasSize(element, fallbackWidth = 320, fallbackHeight = 332) {
    const rect = element.getBoundingClientRect();
    return {
        width: Math.max(1, Math.round(rect.width || fallbackWidth)),
        height: Math.max(1, Math.round(rect.height || fallbackHeight))
    };
}

function normalizePayload(payload) {
    if (!payload) {
        return null;
    }

    return {
        action: payload.action ?? payload.Action ?? "setData",
        timeframe: payload.timeframe ?? payload.Timeframe ?? "30s",
        forceScrollToRealtime: payload.forceScrollToRealtime ?? payload.ForceScrollToRealtime ?? false,
        data: payload.data ?? payload.Data ?? null,
        markers: payload.markers ?? payload.Markers ?? null,
        candle: payload.candle ?? payload.Candle ?? null
    };
}

function normalizePoint(point) {
    if (!point) {
        return null;
    }

    // Цвет свечи задаётся темой серии (раздел 11), а не полями точки.
    return {
        time: toLocalChartTime(point.time ?? point.Time),
        open: point.open ?? point.Open,
        high: point.high ?? point.High,
        low: point.low ?? point.Low,
        close: point.close ?? point.Close,
        volume: point.volume ?? point.Volume ?? 0
    };
}

function toLegendText(value) {
    if (value === null || value === undefined || Number.isNaN(value)) {
        return "-";
    }

    return priceFormatter.format(Number(value));
}

function formatTimestamp(timestamp) {
    if (timestamp === null || timestamp === undefined) {
        return "Нет данных";
    }

    const milliseconds = Number(timestamp) * 1000;
    if (!Number.isFinite(milliseconds)) {
        return "Нет данных";
    }

    return timeFormatter.format(new Date(milliseconds));
}

function setLegendValues(controller, candle) {
    const legend = controller.legend;
    if (!legend) {
        return;
    }

    const timeElement = legend.querySelector('[data-role="time"]');
    const openElement = legend.querySelector('[data-role="open"]');
    const highElement = legend.querySelector('[data-role="high"]');
    const lowElement = legend.querySelector('[data-role="low"]');
    const closeElement = legend.querySelector('[data-role="close"]');

    if (!candle) {
        timeElement.textContent = "Нет данных";
        openElement.textContent = "-";
        highElement.textContent = "-";
        lowElement.textContent = "-";
        closeElement.textContent = "-";
        return;
    }

    timeElement.textContent = formatTimestamp(candle.time);
    openElement.textContent = toLegendText(candle.open);
    highElement.textContent = toLegendText(candle.high);
    lowElement.textContent = toLegendText(candle.low);
    closeElement.textContent = toLegendText(candle.close);
}

// Ширина «мёртвой зоны» вокруг текущей цены в ценовых единицах: столько,
// сколько занимает PRICE_LABEL_GUARD_PX пикселей на текущем масштабе шкалы.
function recomputeLabelGuard(controller) {
    const lastPoint = controller.data.length > 0 ? controller.data[controller.data.length - 1] : null;

    if (!lastPoint || lastPoint.close === null || lastPoint.close === undefined) {
        controller.labelGuard = null;
        return;
    }

    const price = Number(lastPoint.close);
    const coordinate = controller.series.priceToCoordinate(price);

    if (coordinate === null || coordinate === undefined) {
        controller.labelGuard = null;
        return;
    }

    const neighbour = controller.series.coordinateToPrice(coordinate - PRICE_LABEL_GUARD_PX);

    if (neighbour === null || neighbour === undefined) {
        controller.labelGuard = null;
        return;
    }

    controller.labelGuard = { price, epsilon: Math.abs(Number(neighbour) - price) };
}

function setCrosshairActive(controller, active) {
    controller.crosshairActive = active;
    controller.legend?.classList.toggle("is-active", active);
}

function updateEmptyState(controller) {
    if (!controller.emptyState) {
        return;
    }

    controller.emptyState.hidden = controller.data.length > 0;
}

function refreshLegend(controller) {
    const latestPoint = controller.data.length > 0 ? controller.data[controller.data.length - 1] : null;
    setLegendValues(controller, latestPoint);
}

function handleCrosshairMove(controller, param) {
    if (!param) {
        setCrosshairActive(controller, false);
        refreshLegend(controller);
        return;
    }

    const seriesPoint = param.seriesData?.get(controller.series);
    if (!seriesPoint || param.time === undefined || param.time === null) {
        setCrosshairActive(controller, false);
        refreshLegend(controller);
        return;
    }

    setCrosshairActive(controller, true);

    setLegendValues(controller, {
        time: typeof param.time === "number" ? param.time : seriesPoint.time,
        open: seriesPoint.open,
        high: seriesPoint.high,
        low: seriesPoint.low,
        close: seriesPoint.close
    });
}

function applyTimeframeOptions(controller, timeframe) {
    const options = getTimeframeOptions(timeframe);
    controller.currentTimeframe = timeframe;
    controller.barSpacing = options.barSpacing;

    controller.chart.applyOptions({
        timeScale: {
            barSpacing: options.barSpacing,
            minBarSpacing: Math.max(6, options.barSpacing - 3),
            rightOffset: getDynamicRightOffset(controller.data.length, options),
            timeVisible: options.timeVisible,
            secondsVisible: options.secondsVisible
        }
    });
}

function getDynamicRightOffset(count, options) {
    if (count <= 1) {
        return 3;
    }

    if (count < options.minVisibleBars) {
        return Math.max(2, (options.minVisibleBars - count) * 0.22 + 1.6);
    }

    return 2;
}

function handleVisibleRangeChange(controller, range) {
    if (!range || controller.data.length === 0) {
        controller.followRealtime = true;
        toggleLiveReset(controller);
        return;
    }

    const lastIndex = controller.data.length - 1;
    const thresholdBars = FOLLOW_THRESHOLD_PX / Math.max(controller.barSpacing, 1);
    controller.followRealtime = lastIndex - range.to <= thresholdBars;
    toggleLiveReset(controller);
}

// fitContent() подбирает barSpacing под весь диапазон и на малом числе баров
// выдаёт огромные тела. Возвращаем ширину в предел, сохраняя правый край.
function clampBarSpacing(controller) {
    const timeScale = controller.chart.timeScale();
    const current = timeScale.options().barSpacing;

    if (typeof current === "number" && current > MAX_BAR_SPACING_PX) {
        timeScale.applyOptions({ barSpacing: MAX_BAR_SPACING_PX });
        controller.barSpacing = MAX_BAR_SPACING_PX;
    }
}

function toggleLiveReset(controller) {
    if (!controller.liveButton) {
        return;
    }

    controller.liveButton.hidden = controller.data.length === 0 || controller.followRealtime;
}

function resetToLive(controller) {
    controller.followRealtime = true;
    toggleLiveReset(controller);
    requestAnimationFrame(() => {
        controller.chart.timeScale().scrollToRealTime();
    });
}

function ensureViewport(controller, { forceScrollToRealtime = false, fitContent = false } = {}) {
    const options = getTimeframeOptions(controller.currentTimeframe);
    const count = controller.data.length;
    const timeScale = controller.chart.timeScale();

    if (count === 0) {
        toggleLiveReset(controller);
        return;
    }

    timeScale.applyOptions({
        rightOffset: getDynamicRightOffset(count, options)
    });

    if (fitContent || !controller.hasViewport) {
        controller.hasViewport = true;
        timeScale.fitContent();
        clampBarSpacing(controller);

        if (forceScrollToRealtime || controller.followRealtime) {
            requestAnimationFrame(() => timeScale.scrollToRealTime());
        }

        toggleLiveReset(controller);
        return;
    }

    if (forceScrollToRealtime || controller.followRealtime) {
        timeScale.scrollToRealTime();
    }

    toggleLiveReset(controller);
}

function replaceData(controller, data, forceScrollToRealtime, fitContent) {
    controller.data = data;
    controller.series.setData(data);
    controller.volumeSeries.setData(data.map(toVolumePoint));
    updateEmptyState(controller);
    refreshLegend(controller);

    requestAnimationFrame(() => {
        ensureViewport(controller, { forceScrollToRealtime, fitContent });
        recomputeLabelGuard(controller);
    });
}

function upsertPoint(controller, point) {
    const lastPoint = controller.data.length > 0 ? controller.data[controller.data.length - 1] : null;

    controller.volumeSeries.update(toVolumePoint(point));

    if (lastPoint && lastPoint.time === point.time) {
        controller.data[controller.data.length - 1] = point;
        return;
    }

    controller.data.push(point);
}

function updateLatestPoint(controller, point, forceScrollToRealtime) {
    upsertPoint(controller, point);
    controller.series.update(point);
    updateEmptyState(controller);
    refreshLegend(controller);

    requestAnimationFrame(() => {
        ensureViewport(controller, { forceScrollToRealtime });
        recomputeLabelGuard(controller);
    });
}

export function register(root) {
    if (!root || controllers.has(root)) {
        return;
    }

    const surface = root.querySelector(".chart-surface");
    if (!surface) {
        return;
    }

    const initialSize = getCanvasSize(surface);

    // Замыкание на контроллер: форматтер шкалы создаётся раньше самого контроллера.
    const scope = { controller: null };

    const axisPriceFormatter = value => {
        const controller = scope.controller;
        const guard = controller?.labelGuard;

        // Пока активен кроссхейр, метки шкалы не прячем: подпись кроссхейра
        // сама забирает на себя внимание, а её blanking читался бы как баг.
        if (guard && !controller.crosshairActive &&
            value !== guard.price &&
            Math.abs(value - guard.price) < guard.epsilon) {
            return "";
        }

        return priceFormatter.format(value);
    };

    const chart = createChart(surface, {
        width: initialSize.width,
        height: initialSize.height,
        autoSize: false,
        layout: {
            background: { type: ColorType.Solid, color: "transparent" },
            textColor: "#7C8288",
            fontFamily: "'JetBrains Mono', ui-monospace, 'SF Mono', monospace",
            fontSize: 11,
            attributionLogo: false
        },
        localization: {
            locale: "ru-RU",
            priceFormatter: axisPriceFormatter
        },
        grid: {
            vertLines: {
                color: "rgba(255, 255, 255, 0.03)",
                style: LineStyle.Solid
            },
            horzLines: {
                color: "rgba(255, 255, 255, 0.03)",
                style: LineStyle.Solid
            }
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
        handleScroll: {
            mouseWheel: true,
            pressedMouseMove: true,
            horzTouchDrag: true,
            vertTouchDrag: false
        },
        handleScale: {
            axisPressedMouseMove: {
                time: false,
                price: false
            },
            mouseWheel: false,
            pinch: false
        },
        rightPriceScale: {
            borderVisible: true,
            borderColor: "rgba(255, 255, 255, 0.055)",
            scaleMargins: {
                top: 0.12,
                bottom: 0.12
            }
        },
        leftPriceScale: {
            visible: false
        },
        timeScale: {
            borderVisible: true,
            borderColor: "rgba(255, 255, 255, 0.055)",
            timeVisible: true,
            secondsVisible: true,
            rightOffset: 0,
            lockVisibleTimeRangeOnResize: true,
            rightBarStaysOnScroll: false,
            shiftVisibleRangeOnNewBar: false,
            fixLeftEdge: false,
            allowShiftVisibleRangeOnWhitespaceReplacement: true
        }
    });

    const series = chart.addCandlestickSeries({
        upColor: "#3FA37A",
        downColor: "#D2555F",
        borderVisible: false,
        wickUpColor: "rgba(63, 163, 122, 0.55)",
        wickDownColor: "rgba(210, 85, 95, 0.55)",
        // Линия текущей цены — пунктир с ценовой меткой на шкале.
        priceLineVisible: true,
        priceLineColor: "rgba(255, 255, 255, 0.45)",
        priceLineStyle: LineStyle.Dashed,
        priceLineWidth: 1,
        lastValueVisible: true
    });

    const volumeSeries = addVolumeSeries(chart);

    const controller = {
        root,
        chart,
        series,
        volumeSeries,
        surface,
        legend: root.querySelector(".chart-hover-legend"),
        emptyState: root.querySelector(".chart-empty-state"),
        liveButton: root.querySelector(".chart-live-reset"),
        currentTimeframe: "30s",
        barSpacing: defaultTimeframe.barSpacing,
        data: [],
        followRealtime: true,
        hasViewport: false,
        crosshairActive: false,
        labelGuard: null,
        resizeObserver: null,
        visibleRangeHandler: null,
        crosshairHandler: null,
        liveButtonHandler: null
    };

    scope.controller = controller;

    applyTimeframeOptions(controller, controller.currentTimeframe);
    refreshLegend(controller);
    updateEmptyState(controller);

    controller.visibleRangeHandler = range => handleVisibleRangeChange(controller, range);
    controller.crosshairHandler = param => handleCrosshairMove(controller, param);
    controller.liveButtonHandler = event => {
        event.preventDefault();
        resetToLive(controller);
    };

    chart.timeScale().subscribeVisibleLogicalRangeChange(controller.visibleRangeHandler);
    chart.subscribeCrosshairMove(controller.crosshairHandler);
    controller.liveButton?.addEventListener("click", controller.liveButtonHandler);

    controller.resizeObserver = new ResizeObserver(entries => {
        const relevant = entries.find(entry => entry.target === surface) ?? entries[0];
        if (!relevant) {
            return;
        }

        const width = Math.max(1, Math.round(relevant.contentRect.width));
        const height = Math.max(1, Math.round(relevant.contentRect.height));
        controller.chart.resize(width, height);
        ensureViewport(controller, { forceScrollToRealtime: false });
        recomputeLabelGuard(controller);
    });

    controller.resizeObserver.observe(surface);
    toggleLiveReset(controller);
    controllers.set(root, controller);
}

export function sync(root, payload) {
    const controller = getController(root);
    const normalized = normalizePayload(payload);

    if (!controller || !normalized) {
        return;
    }

    const timeframeChanged = controller.currentTimeframe !== normalized.timeframe;
    applyTimeframeOptions(controller, normalized.timeframe);

    // Момент новости и сделки новостного агента. Библиотека умеет ставить
    // значок у свечи, но не вертикальную линию через холст: артборд 02 рисует
    // пунктир, и это расхождение записано в бэклог осознанно — рисовать
    // собственный слой поверх боевого графика значит завести второй график,
    // который разъедется с первым при первом же зуме.
    if (Array.isArray(normalized.markers)) {
        controller.series.setMarkers(normalized.markers.map(marker => ({
            time: toLocalChartTime(marker.time ?? marker.Time),
            position: marker.position ?? marker.Position,
            shape: marker.shape ?? marker.Shape,
            color: marker.color ?? marker.Color,
            text: marker.text ?? marker.Text ?? ""
        })));
    }

    if (normalized.action === "update" && normalized.candle) {
        const point = normalizePoint(normalized.candle);
        if (!point) {
            return;
        }

        updateLatestPoint(controller, point, normalized.forceScrollToRealtime);
        return;
    }

    const data = Array.isArray(normalized.data)
        ? normalized.data.map(normalizePoint).filter(Boolean)
        : [];

    replaceData(
        controller,
        data,
        normalized.forceScrollToRealtime,
        timeframeChanged || !controller.hasViewport || normalized.forceScrollToRealtime
    );
}

export function dispose(root) {
    const controller = getController(root);
    if (!controller) {
        return;
    }

    controller.chart.timeScale().unsubscribeVisibleLogicalRangeChange(controller.visibleRangeHandler);
    controller.chart.unsubscribeCrosshairMove(controller.crosshairHandler);
    controller.liveButton?.removeEventListener("click", controller.liveButtonHandler);
    controller.resizeObserver?.disconnect();
    controller.chart.remove();
    controllers.delete(root);
}
