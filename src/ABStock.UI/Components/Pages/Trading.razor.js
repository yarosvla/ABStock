import {
    ColorType,
    CrosshairMode,
    LineStyle,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";

const FOLLOW_THRESHOLD_PX = 24;
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
const timeFormatter = new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
});

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
        candle: payload.candle ?? payload.Candle ?? null
    };
}

function normalizePoint(point) {
    if (!point) {
        return null;
    }

    return {
        time: point.time ?? point.Time,
        open: point.open ?? point.Open,
        high: point.high ?? point.High,
        low: point.low ?? point.Low,
        close: point.close ?? point.Close,
        color: point.color ?? point.Color,
        borderColor: point.borderColor ?? point.BorderColor,
        wickColor: point.wickColor ?? point.WickColor
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
        refreshLegend(controller);
        return;
    }

    const seriesPoint = param.seriesData?.get(controller.series);
    if (!seriesPoint || param.time === undefined || param.time === null) {
        refreshLegend(controller);
        return;
    }

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
    updateEmptyState(controller);
    refreshLegend(controller);

    requestAnimationFrame(() => {
        ensureViewport(controller, { forceScrollToRealtime, fitContent });
    });
}

function upsertPoint(controller, point) {
    const lastPoint = controller.data.length > 0 ? controller.data[controller.data.length - 1] : null;

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
    const chart = createChart(surface, {
        width: initialSize.width,
        height: initialSize.height,
        autoSize: false,
        layout: {
            background: { type: ColorType.Solid, color: "#0d1524" },
            textColor: "#8ea0ba",
            fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
            attributionLogo: false
        },
        grid: {
            vertLines: {
                color: "rgba(142, 160, 186, 0.09)",
                style: LineStyle.Solid
            },
            horzLines: {
                color: "rgba(142, 160, 186, 0.11)",
                style: LineStyle.Solid
            }
        },
        crosshair: {
            mode: CrosshairMode.MagnetOHLC,
            vertLine: {
                color: "rgba(85, 199, 255, 0.36)",
                style: LineStyle.Dashed,
                width: 1,
                labelBackgroundColor: "#18263d"
            },
            horzLine: {
                color: "rgba(124, 92, 255, 0.34)",
                style: LineStyle.Dashed,
                width: 1,
                labelBackgroundColor: "#18263d"
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
            borderVisible: false,
            scaleMargins: {
                top: 0.06,
                bottom: 0.02
            }
        },
        leftPriceScale: {
            visible: false
        },
        timeScale: {
            borderVisible: false,
            rightOffset: 0,
            lockVisibleTimeRangeOnResize: true,
            rightBarStaysOnScroll: false,
            shiftVisibleRangeOnNewBar: false,
            fixLeftEdge: false,
            allowShiftVisibleRangeOnWhitespaceReplacement: true
        }
    });

    const series = chart.addCandlestickSeries({
        upColor: "#36d98a",
        downColor: "#ff6b5f",
        borderUpColor: "#36d98a",
        borderDownColor: "#ff6b5f",
        wickUpColor: "#65f1ab",
        wickDownColor: "#ff8a80",
        priceLineVisible: false,
        lastValueVisible: true
    });

    const controller = {
        root,
        chart,
        series,
        surface,
        legend: root.querySelector(".chart-hover-legend"),
        emptyState: root.querySelector(".chart-empty-state"),
        liveButton: root.querySelector(".chart-live-reset"),
        currentTimeframe: "30s",
        barSpacing: defaultTimeframe.barSpacing,
        data: [],
        followRealtime: true,
        hasViewport: false,
        resizeObserver: null,
        visibleRangeHandler: null,
        crosshairHandler: null,
        liveButtonHandler: null
    };

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
