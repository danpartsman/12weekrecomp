(() => {
    "use strict";

    const root = document.querySelector("[data-marketing-page]");
    if (!root) return;

    const sessionKey = "12wr.marketing.session.simple";

    function newSessionId() {
        if (window.crypto && crypto.randomUUID) {
            return crypto.randomUUID();
        }

        const bytes = new Uint8Array(16);

        if (window.crypto && crypto.getRandomValues) {
            crypto.getRandomValues(bytes);
        }

        return Array.from(
            bytes,
            value => value.toString(16).padStart(2, "0")
        ).join("");
    }

    let sessionId = localStorage.getItem(sessionKey);

    if (!sessionId || sessionId.length > 64) {
        sessionId = newSessionId();
        localStorage.setItem(sessionKey, sessionId);
    }

    function track(eventType) {
        const payload = JSON.stringify({
            sessionId,
            variant: "U",
            eventType,
            pagePath: window.location.pathname
        });

        try {
            const blob =
                new Blob(
                    [payload],
                    { type: "application/json" }
                );

            if (
                navigator.sendBeacon
                && navigator.sendBeacon("/marketing/track", blob)
            ) {
                return;
            }
        }
        catch (_) {}

        try {
            fetch(
                "/marketing/track",
                {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: payload,
                    keepalive: true,
                    credentials: "same-origin"
                }
            ).catch(() => {});
        }
        catch (_) {}
    }

    const page =
        root.dataset.marketingPage;

    if (page === "welcome") {
        track("WELCOME_VIEW");
    }

    if (page === "evidence") {
        track("EVIDENCE_VIEW");
    }

    document
        .querySelectorAll("[data-track]")
        .forEach(link => {
            link.addEventListener(
                "click",
                () => track(link.dataset.track),
                { capture: true }
            );
        });
})();
