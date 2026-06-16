import {
    ColorType,
    CrosshairMode,
    LineStyle,
    LineType,
    createChart
} from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";

const charts = new WeakMap();

const baseLayout = {
    background: { type: ColorType.Solid, color: "#0d1524" },
    textColor: "#8ea0ba",
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
    attributionLogo: false
};

const baseGrid = {
    vertLines: { color: "rgba(142, 160, 186, 0.09)", style: LineStyle.Solid },
    horzLines: { color: "rgba(142, 160, 186, 0.11)", style: LineStyle.Solid }
};

function getCanvasSize(element) {
    const rect = element.getBoundingClientRect();
    return {
        width: Math.max(1, Math.round(rect.width || 320)),
        height: Math.max(1, Math.round(rect.height || 280))
    };
}

function createBaseChart(element) {
    const size = getCanvasSize(element);
    const chart = createChart(element, {
        width: size.width,
        height: size.height,
        autoSize: false,
        layout: baseLayout,
        grid: baseGrid,
        crosshair: {
            mode: CrosshairMode.Normal,
            vertLine: { color: "rgba(85, 199, 255, 0.36)", style: LineStyle.Dashed, width: 1, labelBackgroundColor: "#18263d" },
            horzLine: { color: "rgba(124, 92, 255, 0.34)", style: LineStyle.Dashed, width: 1, labelBackgroundColor: "#18263d" }
        },
        rightPriceScale: {
            borderVisible: false,
            scaleMargins: { top: 0.18, bottom: 0.12 }
        },
        leftPriceScale: { visible: false },
        timeScale: {
            borderVisible: false,
            timeVisible: true,
            secondsVisible: true,
            rightOffset: 2,
            lockVisibleTimeRangeOnResize: true
        }
    });

    const resizeObserver = new ResizeObserver(entries => {
        const relevant = entries.find(entry => entry.target === element) ?? entries[0];
        if (!relevant) {
            return;
        }

        const width = Math.max(1, Math.round(relevant.contentRect.width));
        const height = Math.max(1, Math.round(relevant.contentRect.height));
        chart.resize(width, height);
    });
    resizeObserver.observe(element);

    return { chart, resizeObserver, series: null };
}

function normalizeLinePoints(points) {
    if (!Array.isArray(points)) {
        return [];
    }

    return points
        .map(point => ({
            time: point.time ?? point.Time,
            value: point.value ?? point.Value
        }))
        .filter(point => point.time !== null && point.time !== undefined
            && point.value !== null && point.value !== undefined)
        .map(point => ({ time: Number(point.time), value: Number(point.value) }));
}

function normalizeMarkers(markers) {
    if (!Array.isArray(markers)) {
        return [];
    }

    return markers
        .map(marker => ({
            time: Number(marker.time ?? marker.Time),
            position: marker.position ?? marker.Position ?? "belowBar",
            color: marker.color ?? marker.Color ?? "#8ea0ba",
            shape: marker.shape ?? marker.Shape ?? "circle",
            text: marker.text ?? marker.Text ?? ""
        }))
        .filter(marker => Number.isFinite(marker.time))
        .sort((a, b) => a.time - b.time);
}

function ensureChart(element, color) {
    let bundle = charts.get(element);
    if (!bundle) {
        bundle = createBaseChart(element);
        charts.set(element, bundle);
    }

    if (!bundle.series) {
        const rgba = (alpha) => color.replace("rgb(", "rgba(").replace(")", `, ${alpha})`);
        bundle.series = bundle.chart.addAreaSeries({
            lineColor: color,
            lineWidth: 2,
            lineType: LineType.Curved,
            topColor: rgba(0.24),
            bottomColor: rgba(0.0),
            crosshairMarkerVisible: true,
            crosshairMarkerRadius: 4,
            priceLineVisible: false,
            lastValueVisible: true
        });
    }

    return bundle;
}

export function renderEquity(element, points) {
    if (!element) {
        return;
    }

    const bundle = ensureChart(element, "rgb(85, 199, 255)");
    const data = normalizeLinePoints(points);
    bundle.series.setData(data);
    if (data.length > 0) {
        bundle.chart.timeScale().fitContent();
    }
}

export function renderPrice(element, points, markers) {
    if (!element) {
        return;
    }

    const bundle = ensureChart(element, "rgb(124, 92, 255)");
    const data = normalizeLinePoints(points);
    bundle.series.setData(data);
    bundle.series.setMarkers(normalizeMarkers(markers));
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
