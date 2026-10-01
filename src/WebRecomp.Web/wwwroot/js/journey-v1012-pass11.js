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
        enableMobilePrescriptionExpansion(coach);
        return true;
    };

    const enableMobilePrescriptionExpansion = (coach) => {
        let panel = coach.querySelector(".journey-coach-p8__panel");
        if (!panel || panel.dataset.mobileExpandReady === "true") return;

        // Defensive cleanup for markup produced by the prior swipe experiment.
        const oldViewport = panel.parentElement;
        if (oldViewport?.classList.contains("journey-coach-p8__mobile-scroll")) {
            oldViewport.parentNode.insertBefore(panel, oldViewport);
            oldViewport.remove();
        }

        panel.dataset.mobileExpandReady = "true";

        const hint = document.createElement("span");
        hint.className = "journey-coach-p8__expand-hint";
        hint.textContent = "Tap to enlarge";
        coach.appendChild(hint);

        panel.addEventListener("click", () => {
            if (!window.matchMedia("(max-width: 680px)").matches) return;

            const expanded = coach.classList.toggle("is-prescription-expanded");
            hint.textContent = expanded ? "Tap to restore" : "Tap to enlarge";
        });

        window.addEventListener("resize", () => {
            if (!window.matchMedia("(max-width: 680px)").matches) {
                coach.classList.remove("is-prescription-expanded");
                hint.textContent = "Tap to enlarge";
            }
        }, { passive:true });
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
