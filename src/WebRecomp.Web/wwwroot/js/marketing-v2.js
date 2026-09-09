(() => {
    "use strict";

    const root = document.querySelector("[data-marketing-page]");
    if (!root) return;

    const variants = ["A", "B", "C", "D"];
    const variantKey = "12wr.marketing.variant.v2";
    const sessionKey = "12wr.marketing.session.v2";
    const assignedAtKey = "12wr.marketing.variant.assigned.v2";
    const assignmentLifetimeMs = 30 * 24 * 60 * 60 * 1000;

    const params = new URLSearchParams(window.location.search);
    const preview = params.get("preview") === "1";
    const requested = (params.get("variant") || "").toUpperCase();

    function newSessionId() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        const bytes = new Uint8Array(16);
        if (window.crypto && crypto.getRandomValues) crypto.getRandomValues(bytes);
        return Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
    }

    let sessionId = localStorage.getItem(sessionKey);
    if (!sessionId || sessionId.length > 64) {
        sessionId = newSessionId();
        localStorage.setItem(sessionKey, sessionId);
    }

    function chooseVariant() {
        if (preview && variants.includes(requested)) return requested;

        const stored = localStorage.getItem(variantKey);
        const assignedAt = Number(localStorage.getItem(assignedAtKey) || 0);

        if (variants.includes(stored) && Date.now() - assignedAt < assignmentLifetimeMs) {
            return stored;
        }

        let index = 0;
        if (window.crypto && crypto.getRandomValues) {
            const value = new Uint32Array(1);
            crypto.getRandomValues(value);
            index = value[0] % variants.length;
        } else {
            index = Math.floor(Math.random() * variants.length);
        }

        const selected = variants[index];
        localStorage.setItem(variantKey, selected);
        localStorage.setItem(assignedAtKey, String(Date.now()));
        return selected;
    }

    const variant = chooseVariant();
    document.documentElement.dataset.marketingVariant = variant;

    document.querySelectorAll("[data-variant]").forEach(section => {
        section.hidden = section.dataset.variant !== variant;
    });

    const switcher = document.querySelector("[data-preview-switch]");
    if (switcher && preview) switcher.hidden = false;

    function track(eventType) {
        const payload = JSON.stringify({
            sessionId,
            variant,
            eventType,
            pagePath: window.location.pathname
        });

        try {
            const blob = new Blob([payload], { type: "application/json" });
            if (navigator.sendBeacon && navigator.sendBeacon("/marketing/track", blob)) {
                return;
            }
        } catch (_) {}

        try {
            fetch("/marketing/track", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: payload,
                keepalive: true,
                credentials: "same-origin"
            }).catch(() => {});
        } catch (_) {}
    }

    const page = root.dataset.marketingPage;
    if (page === "welcome") track("WELCOME_VIEW");
    if (page === "evidence") track("EVIDENCE_VIEW");

    document.querySelectorAll("[data-track]").forEach(link => {
        link.addEventListener("click", () => track(link.dataset.track), { capture: true });
    });

    document.querySelectorAll('a[href^="/trial/start"]').forEach(link => {
        const url = new URL(link.getAttribute("href"), window.location.origin);
        url.searchParams.set("variant", variant);
        link.setAttribute("href", url.pathname + url.search);
    });
})();
