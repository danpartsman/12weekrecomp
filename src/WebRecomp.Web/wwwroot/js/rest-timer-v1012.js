(() => {
    "use strict";

    // TEST v1012 compatibility slot.
    // The separate rest countdown was removed by design. The existing loader
    // still requests this file, so it now owns only the small live-workout
    // controls that belong beside the authoritative workout count-up timer.
    if (!window.location.pathname.toLowerCase().startsWith("/detailedworkout")) {
        return;
    }

    const params = new URLSearchParams(window.location.search);
    const timerHost = document.querySelector("[data-live-workout-start]");
    const sessionId = timerHost?.dataset.liveWorkoutSessionId || "";

    if (!timerHost || !sessionId) {
        return;
    }

    const ensureStyles = () => {
        if (document.getElementById("wi-end-workout-v1012-style")) return;

        const style = document.createElement("style");
        style.id = "wi-end-workout-v1012-style";
        style.textContent = `
            .wi-live-elapsed strong[data-live-workout-elapsed] {
                display: block;
                margin-top: .08rem;
                font-size: clamp(2rem, 4.8vw, 3rem) !important;
                line-height: 1 !important;
                letter-spacing: .035em;
                font-variant-numeric: tabular-nums;
            }
            .ts-live-banner-v1012__time {
                font-size: 1.15rem !important;
                line-height: 1 !important;
            }
            .wi-end-menu-v1012 {
                position: fixed;
                inset: 0;
                z-index: 65000;
                display: grid;
                place-items: center;
                padding: 1rem;
                background: rgba(2, 6, 12, .78);
                backdrop-filter: blur(8px);
                -webkit-backdrop-filter: blur(8px);
            }
            .wi-end-menu-v1012[hidden] { display: none !important; }
            .wi-end-menu-v1012__panel {
                width: min(94vw, 470px);
                padding: 1.25rem;
                border: 1px solid rgba(103, 232, 249, .28);
                border-radius: 1rem;
                background: linear-gradient(160deg, #0c1726, #07101b);
                box-shadow: 0 28px 90px rgba(0, 0, 0, .55);
            }
            .wi-end-menu-v1012__kicker {
                margin: 0 0 .3rem;
                color: #67e8f9;
                font-size: .7rem;
                font-weight: 900;
                letter-spacing: .12em;
                text-transform: uppercase;
            }
            .wi-end-menu-v1012 h2 {
                margin: 0;
                font-size: clamp(1.7rem, 5vw, 2.35rem);
            }
            .wi-end-menu-v1012 p {
                margin: .65rem 0 1rem;
                color: #cbd5e1;
                line-height: 1.45;
            }
            .wi-end-menu-v1012__actions {
                display: grid;
                gap: .6rem;
            }
            .wi-end-menu-v1012__actions .button,
            .wi-end-menu-v1012__actions button {
                width: 100%;
                min-height: 48px;
            }
            .wi-end-menu-v1012__discard {
                border: 1px solid rgba(248, 113, 113, .62) !important;
                background: rgba(127, 29, 29, .22) !important;
                color: #fecaca !important;
            }
            .wi-end-menu-v1012__discard:hover {
                background: rgba(127, 29, 29, .38) !important;
            }
        `;
        document.head.appendChild(style);
    };

    const htmlEscape = value => String(value || "").replace(
        /[&<>"']/g,
        character => ({
            "&": "&amp;",
            "<": "&lt;",
            ">": "&gt;",
            '"': "&quot;",
            "'": "&#39;"
        })[character]);

    let bypassStopPrompt = false;
    let modal = null;

    const getStopButton = () =>
        document.querySelector('[name="stopWorkout"]');

    const getCardioForm = () =>
        document.querySelector("#wi-cardio-live-v1012 form");

    const isCardio = () =>
        Boolean(document.getElementById("wi-cardio-live-v1012"));

    const getToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

    const cleanEndMenuFlag = () => {
        if (params.get("showEndMenu") !== "1") return;
        const clean = new URL(window.location.href);
        clean.searchParams.delete("showEndMenu");
        window.history.replaceState({}, "", clean);
    };

    const closeMenu = () => {
        if (modal) modal.hidden = true;
    };

    const submitStop = () => {
        closeMenu();

        const cardioForm = getCardioForm();
        if (cardioForm instanceof HTMLFormElement) {
            cardioForm.requestSubmit();
            return;
        }

        const stopButton = getStopButton();
        const workoutForm = document.getElementById("wi-detailed-workout-form");

        if (!(stopButton instanceof HTMLButtonElement)
            || !(workoutForm instanceof HTMLFormElement)) {
            return;
        }

        bypassStopPrompt = true;
        workoutForm.requestSubmit(stopButton);
    };

    const submitDiscard = () => {
        const token = getToken();
        if (!token) return;

        closeMenu();

        const form = document.createElement("form");
        form.method = "post";
        form.action = "/TrainingSession/Discard";
        form.hidden = true;
        form.innerHTML = `
            <input type="hidden" name="__RequestVerificationToken" value="${htmlEscape(token)}" />
            <input type="hidden" name="workoutSessionId" value="${htmlEscape(sessionId)}" />
        `;
        document.body.appendChild(form);
        form.submit();
    };

    const ensureModal = () => {
        if (modal) return modal;

        modal = document.createElement("section");
        modal.className = "wi-end-menu-v1012";
        modal.hidden = true;
        modal.setAttribute("role", "dialog");
        modal.setAttribute("aria-modal", "true");
        modal.setAttribute("aria-labelledby", "wi-end-menu-title-v1012");
        modal.innerHTML = `
            <div class="wi-end-menu-v1012__panel">
                <div class="wi-end-menu-v1012__kicker">WORKOUT IN PROGRESS</div>
                <h2 id="wi-end-menu-title-v1012">End Workout?</h2>
                <p>Stop and review this workout, discard the unfinished workout completely, or keep training.</p>
                <div class="wi-end-menu-v1012__actions">
                    <button type="button" class="button primary" data-wi-end-stop>Stop Workout</button>
                    <button type="button" class="button wi-end-menu-v1012__discard" data-wi-end-discard>Discard Workout</button>
                    <button type="button" class="button" data-wi-end-cancel>Cancel</button>
                </div>
            </div>
        `;

        modal.querySelector("[data-wi-end-stop]")?.addEventListener("click", submitStop);
        modal.querySelector("[data-wi-end-discard]")?.addEventListener("click", submitDiscard);
        modal.querySelector("[data-wi-end-cancel]")?.addEventListener("click", closeMenu);
        modal.addEventListener("click", event => {
            if (event.target === modal) closeMenu();
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && !modal.hidden) closeMenu();
        });

        document.body.appendChild(modal);
        return modal;
    };

    const openMenu = () => {
        const dialog = ensureModal();
        const title = dialog.querySelector("#wi-end-menu-title-v1012");
        const stop = dialog.querySelector("[data-wi-end-stop]");
        const paragraph = dialog.querySelector("p");

        if (isCardio()) {
            if (title) title.textContent = "End Cardio?";
            if (stop) stop.textContent = "End Cardio";
            if (paragraph) paragraph.textContent = "End and save this cardio segment, discard the unfinished segment completely, or keep training.";
        }
        else {
            if (title) title.textContent = "End Workout?";
            if (stop) stop.textContent = "Stop Workout";
            if (paragraph) paragraph.textContent = "Stop and review this workout, discard the unfinished workout completely, or keep training.";
        }

        dialog.hidden = false;
        stop?.focus();
    };

    const bindStopButton = () => {
        const stopButton = getStopButton();
        if (!(stopButton instanceof HTMLButtonElement)
            || stopButton.dataset.wiEndMenuBound === "1") {
            return;
        }

        stopButton.dataset.wiEndMenuBound = "1";
        stopButton.addEventListener("click", event => {
            if (bypassStopPrompt) {
                bypassStopPrompt = false;
                return;
            }

            event.preventDefault();
            event.stopImmediatePropagation();
            openMenu();
        }, true);
    };

    ensureStyles();
    bindStopButton();

    // Live-workout code can rewrite the action row as values change. Rebind
    // without touching the workout's authoritative server timer.
    const observer = new MutationObserver(bindStopButton);
    observer.observe(document.body, { childList: true, subtree: true });

    if (params.get("showEndMenu") === "1") {
        cleanEndMenuFlag();
        window.setTimeout(openMenu, 250);
    }
})();
