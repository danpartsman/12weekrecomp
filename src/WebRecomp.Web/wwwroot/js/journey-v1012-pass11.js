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
        enableMobilePrescriptionDrag(coach);
        return true;
    };

    const enableMobilePrescriptionDrag = (coach) => {
        const panel = coach.querySelector(".journey-coach-p8__panel");
        if (!panel || panel.dataset.mobileDragReady === "true") return;

        panel.dataset.mobileDragReady = "true";

        let startX = 0;
        let startY = 0;
        let baseX = 0;
        let currentX = 0;
        let draggingHorizontal = false;
        let gestureDecided = false;

        const mobile = () => window.matchMedia("(max-width: 680px)").matches;
        const minX = () => {
            const overflow = Math.max(0, panel.scrollWidth - coach.clientWidth + 24);
            return -Math.min(overflow + 120, 320);
        };

        panel.addEventListener("touchstart", (event) => {
            if (!mobile() || event.touches.length !== 1) return;

            const touch = event.touches[0];
            startX = touch.clientX;
            startY = touch.clientY;
            baseX = currentX;
            draggingHorizontal = false;
            gestureDecided = false;
            panel.style.transition = "none";
        }, { passive:true });

        panel.addEventListener("touchmove", (event) => {
            if (!mobile() || event.touches.length !== 1) return;

            const touch = event.touches[0];
            const dx = touch.clientX - startX;
            const dy = touch.clientY - startY;

            if (!gestureDecided && (Math.abs(dx) > 8 || Math.abs(dy) > 8)) {
                draggingHorizontal = Math.abs(dx) > Math.abs(dy);
                gestureDecided = true;
            }

            if (!draggingHorizontal) return;

            event.preventDefault();

            currentX = Math.max(minX(), Math.min(0, baseX + dx));
            panel.style.transform = `translate3d(${currentX}px,0,0)`;
        }, { passive:false });

        const finish = () => {
            if (!mobile()) return;
            panel.style.transition = "transform 120ms ease-out";
        };

        panel.addEventListener("touchend", finish, { passive:true });
        panel.addEventListener("touchcancel", finish, { passive:true });

        window.addEventListener("resize", () => {
            if (!mobile()) {
                currentX = 0;
                panel.style.transform = "";
                panel.style.transition = "";
            } else {
                currentX = Math.max(minX(), Math.min(0, currentX));
                panel.style.transform = `translate3d(${currentX}px,0,0)`;
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
