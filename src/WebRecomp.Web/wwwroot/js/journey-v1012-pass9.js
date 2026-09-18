(() => {
    "use strict";

    const body = document.body;
    const path = location.pathname.toLowerCase();

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const apply = () => {
        const coach = document.querySelector(".recomp-dashboard .journey-coach-p8");
        if (!coach) return false;
        if (coach.classList.contains("journey-coach-pass9")) return true;

        const bg = coach.querySelector(":scope > .journey-coach-p8__bg");
        const panel = coach.querySelector(":scope > .journey-coach-p8__panel");
        const header = panel?.querySelector(":scope > .journey-coach-p8__header");
        const lead = panel?.querySelector(":scope > .journey-coach-p8__lead");

        if (!bg || !panel || !header || !lead) return false;

        /* The earlier band asset was corrupt in TEST. Use the known-good Marisol image. */
        if (bg.tagName === "IMG") {
            bg.src = "/images/coach/marisol-coach-avatar.webp";
            bg.removeAttribute("srcset");
        }

        const message = document.createElement("div");
        message.className = "journey-coach-p9__message";
        message.setAttribute("aria-label", "Coach Marisol recommendation");
        message.append(header, lead);

        coach.appendChild(message);
        coach.classList.add("journey-coach-pass9");
        return true;
    };

    const initialize = () => {
        let attempts = 0;
        const tryApply = () => {
            attempts += 1;
            if (apply() || attempts >= 24) return;
            window.setTimeout(tryApply, 100);
        };
        tryApply();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once:true });
    } else {
        initialize();
    }
})();
