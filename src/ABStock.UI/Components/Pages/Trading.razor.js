const FOLLOW_THRESHOLD = 24;

function updateFollowMode(element) {
    const maxScrollLeft = Math.max(0, element.scrollWidth - element.clientWidth);
    element.dataset.followRightEdge = maxScrollLeft - element.scrollLeft <= FOLLOW_THRESHOLD ? "true" : "false";
}

function stopDragging(element, state) {
    if (!state.dragging) {
        return;
    }

    state.dragging = false;
    element.classList.remove("is-dragging");

    if (state.pointerId !== null && element.hasPointerCapture?.(state.pointerId)) {
        element.releasePointerCapture(state.pointerId);
    }

    state.pointerId = null;
    updateFollowMode(element);
}

export function register(element) {
    if (!element || element.dataset.chartRegistered === "true") {
        return;
    }

    element.dataset.chartRegistered = "true";
    element.dataset.followRightEdge = "true";

    const state = {
        dragging: false,
        pointerId: null,
        startX: 0,
        startScrollLeft: 0
    };

    element.addEventListener("scroll", () => {
        if (!state.dragging) {
            updateFollowMode(element);
        }
    }, { passive: true });

    element.addEventListener("pointerdown", event => {
        if (event.pointerType === "mouse" && event.button !== 0) {
            return;
        }

        state.dragging = true;
        state.pointerId = event.pointerId;
        state.startX = event.clientX;
        state.startScrollLeft = element.scrollLeft;
        element.classList.add("is-dragging");
        element.setPointerCapture?.(event.pointerId);
    });

    element.addEventListener("pointermove", event => {
        if (!state.dragging || event.pointerId !== state.pointerId) {
            return;
        }

        const deltaX = event.clientX - state.startX;
        element.scrollLeft = state.startScrollLeft - deltaX;
        updateFollowMode(element);
    });

    element.addEventListener("pointerup", event => stopDragging(element, state));
    element.addEventListener("pointercancel", event => stopDragging(element, state));
    element.addEventListener("lostpointercapture", () => stopDragging(element, state));

    queueMicrotask(() => sync(element, true));
}

export function sync(element, force = false) {
    if (!element) {
        return;
    }

    if (!force && element.dataset.followRightEdge === "false") {
        updateFollowMode(element);
        return;
    }

    const previousScrollBehavior = element.style.scrollBehavior;
    const scrollToEdge = () => {
        element.scrollLeft = Math.max(0, element.scrollWidth - element.clientWidth);
        element.dataset.followRightEdge = "true";
    };

    if (force) {
        element.style.scrollBehavior = "auto";
    }

    scrollToEdge();
    requestAnimationFrame(() => {
        scrollToEdge();
        if (force) {
            element.style.scrollBehavior = previousScrollBehavior;
        }
    });
}
