/**
 * Цвета графиков из токенов — общий код «Торгов» и детальной агента.
 *
 * lightweight-charts рисует на холсте и CSS не видит: цвет ему отдают
 * строкой. Раньше строки были литералами графита прямо в скриптах, и белая
 * тема (раздел 19) их бы не достала. Теперь источник один —
 * design-system.css и design-system-light.css, а скрипты читают значения
 * через getComputedStyle и перечитывают их при смене темы.
 */

import { LineStyle } from "/lib/lightweight-charts/lightweight-charts.standalone.production.mjs";

/** Событие загрузочного скрипта App.razor: тема на <html> уже сменилась. */
export const THEME_CHANGE_EVENT = "abstock:themechange";

export function readChartTheme() {
    const root = getComputedStyle(document.documentElement);
    const token = name => root.getPropertyValue(name).trim();

    // auto — «по направлению свечи», как у остальных столбиков: в графите
    // текущий столбик объёма ничем не выделен.
    const volumeCurrent = token("--volume-current");
    const stoppedOpacity = Number.parseFloat(token("--chart-stopped-opacity"));

    return {
        text1: token("--text-1"),
        text3: token("--text-3"),
        line1: token("--line-1"),
        line2: token("--line-2"),
        labelBackground: token("--gph-600"),
        grid: token("--chart-grid"),
        crosshair: token("--chart-crosshair"),
        priceLine: token("--chart-price-line"),
        upFill: token("--up-fill"),
        downFill: token("--down-fill"),
        wickUp: token("--chart-wick-up"),
        wickDown: token("--chart-wick-down"),
        upVolume: token("--up-volume"),
        downVolume: token("--down-volume"),
        volumeCurrent: volumeCurrent === "" || volumeCurrent === "auto" ? null : volumeCurrent,
        stoppedOpacity: Number.isFinite(stoppedOpacity) ? stoppedOpacity : 1,
        lastPriceByCandle: token("--chart-last-price") === "candle",
        sellMarkerSquare: token("--chart-sell-marker") === "square",
        agent: {
            trend: { on: token("--agent-trend"), off: token("--agent-trend-dim"), mark: token("--agent-trend-mark") },
            counter: { on: token("--agent-counter"), off: token("--agent-counter-dim"), mark: token("--agent-counter-mark") },
            mm: { on: token("--agent-mm"), off: token("--agent-mm-dim"), mark: token("--agent-mm-mark") },
            news: { on: token("--agent-news"), off: token("--agent-news-dim"), mark: token("--agent-news-mark") }
        },
        /** Цвет из имени токена («--agent-news») или готовая строка как есть. */
        resolve: value => typeof value === "string" && value.startsWith("--") ? token(value) : value
    };
}

/**
 * Тот же цвет с дополнительной непрозрачностью. Нужен свечам при
 * остановленных торгах: у серии нет общего opacity, только цвета.
 * Понимает #RGB, #RRGGBB и rgb()/rgba() — ровно то, что лежит в токенах.
 */
export function withAlpha(color, alpha) {
    if (!(alpha < 1) || typeof color !== "string") {
        return color;
    }

    const value = color.trim();
    let r, g, b, a = 1;

    const hex = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value);
    if (hex) {
        const digits = hex[1].length === 3
            ? hex[1].split("").map(d => d + d).join("")
            : hex[1];
        r = parseInt(digits.slice(0, 2), 16);
        g = parseInt(digits.slice(2, 4), 16);
        b = parseInt(digits.slice(4, 6), 16);
    } else {
        const rgb = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/i.exec(value);
        if (!rgb) {
            return color;
        }

        [r, g, b] = [rgb[1], rgb[2], rgb[3]].map(Number);
        a = rgb[4] === undefined ? 1 : Number(rgb[4]);
    }

    return `rgba(${r}, ${g}, ${b}, ${+(a * alpha).toFixed(3)})`;
}

/**
 * Линия и плашка последней цены. Библиотека красит их ОДНИМ цветом —
 * priceLineColor, а пустая строка значит «цвет последней свечи». Графиту
 * нужен общий цвет пунктира (так было всегда). Белой теме — плашка по
 * направлению свечи и пунктир --chart-price-line: встроенную линию
 * выключаем, плашка остаётся, а пунктир рисуем отдельной ценовой линией
 * без своей подписи (syncLastPriceLine).
 */
export function lastPriceSeriesOptions(theme) {
    return theme.lastPriceByCandle
        ? { priceLineVisible: false, priceLineColor: "" }
        : { priceLineVisible: true, priceLineColor: theme.priceLine };
}

/** Держит отдельный пунктир последней цены в state.priceLine. */
export function syncLastPriceLine(state, series, theme, lastClose) {
    if (!theme.lastPriceByCandle || !Number.isFinite(lastClose)) {
        if (state.priceLine) {
            series.removePriceLine(state.priceLine);
            state.priceLine = null;
        }
        return;
    }

    const options = {
        price: lastClose,
        color: theme.priceLine,
        lineWidth: 1,
        lineStyle: LineStyle.Dashed,
        axisLabelVisible: false
    };

    if (state.priceLine) {
        state.priceLine.applyOptions(options);
    } else {
        state.priceLine = series.createPriceLine(options);
    }
}

/** Подписка на смену темы; возвращает функцию отписки. */
export function onThemeChange(handler) {
    window.addEventListener(THEME_CHANGE_EVENT, handler);
    return () => window.removeEventListener(THEME_CHANGE_EVENT, handler);
}
