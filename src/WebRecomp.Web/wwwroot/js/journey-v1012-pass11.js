(() => {
    "use strict";

    const body = document.body;
    const path = location.pathname.toLowerCase();

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const apply = () => {
        const coach = document.querySelector(".recomp-dashboard .journey-coach-p8");
        const bg = coach?.querySelector(":scope > .journey-coach-p8__bg");
        if (!coach || !bg) return false;
        if (coach.classList.contains("journey-coach-pass11")) return true;

        if (bg.tagName === "IMG") {
            bg.src = "/images/coach/marisol-card-background-v1012.webp";
            bg.removeAttribute("srcset");
        }

        coach.classList.add("journey-coach-pass11");
        return true;
    };

    const initialize = () => {
        let attempts = 0;
        const tryApply = () => {
            attempts += 1;
            if (apply() || attempts >= 30) return;
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
