(() => {
    "use strict";

    const mobile = () => window.innerWidth <= 650;
    const panels = document.querySelectorAll("[data-draggable-panel]");
    panels.forEach(panel => {
        if (panel.dataset.dragReady === "true") return;
        const handle = panel.querySelector("[data-draggable-handle]");
        if (!handle) return;
        panel.dataset.dragReady = "true";
        handle.title = "Drag to move";
        handle.tabIndex = 0;
        const key = `12wr:panel-position:v1:${panel.dataset.draggablePanel}`;

        const clamp = (left, top) => ({
            left: Math.max(8, Math.min(left, window.innerWidth - panel.offsetWidth - 8)),
            top: Math.max(8, Math.min(top, window.innerHeight - panel.offsetHeight - 8))
        });
        const place = (left, top) => {
            if (mobile()) return;
            const position = clamp(left, top);
            panel.style.position = "fixed";
            panel.style.left = `${position.left}px`;
            panel.style.top = `${position.top}px`;
            panel.style.right = "auto";
            panel.style.bottom = "auto";
            panel.style.margin = "0";
        };
        const save = () => {
            try {
                const rect = panel.getBoundingClientRect();
                localStorage.setItem(key, JSON.stringify({ left: rect.left, top: rect.top }));
            } catch { /* Dragging still works without browser storage. */ }
        };
        const restore = () => {
            if (mobile()) {
                ["position", "left", "top", "right", "bottom", "margin"].forEach(name =>
                    panel.style.removeProperty(name));
                return;
            }
            try {
                const stored = JSON.parse(localStorage.getItem(key) || "null");
                if (Number.isFinite(stored?.left) && Number.isFinite(stored?.top)) {
                    place(stored.left, stored.top);
                }
            } catch { /* Use the default position. */ }
        };

        let drag = null;
        handle.addEventListener("pointerdown", event => {
            if (mobile() || event.button !== 0) return;
            const rect = panel.getBoundingClientRect();
            drag = { x: event.clientX, y: event.clientY, left: rect.left, top: rect.top };
            handle.setPointerCapture(event.pointerId);
            event.preventDefault();
        });
        handle.addEventListener("pointermove", event => {
            if (!drag) return;
            place(drag.left + event.clientX - drag.x, drag.top + event.clientY - drag.y);
        });
        const finish = () => {
            if (!drag) return;
            drag = null;
            save();
        };
        handle.addEventListener("pointerup", finish);
        handle.addEventListener("pointercancel", finish);
        handle.addEventListener("keydown", event => {
            if (mobile() || !["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(event.key)) return;
            event.preventDefault();
            const rect = panel.getBoundingClientRect();
            const step = event.shiftKey ? 25 : 10;
            const left = rect.left + (event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0);
            const top = rect.top + (event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0);
            place(left, top);
            save();
        });
        window.addEventListener("resize", restore, { passive: true });
        restore();
    });
})();
