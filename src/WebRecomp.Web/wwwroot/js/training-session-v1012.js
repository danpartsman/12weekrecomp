(() => {
    "use strict";

    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview")) {
        return;
    }

    const CARDIO_CODES = new Set([
        "GENERAL_CARDIO",
        "EASY_CARDIO",
        "ZONE2_CARDIO",
        "WALKING",
        "RUNNING",
        "TREADMILL",
        "CYCLING",
        "ROWING",
        "ELLIPTICAL",
        "STAIR_CLIMBER",
        "SWIMMING"
    ]);

    const ensureStyles = () => {
        if (document.getElementById("training-session-v1012-style")) {
            return;
        }

        const style = document.createElement("style");
        style.id = "training-session-v1012-style";
        style.textContent = `
            .ts-live-banner-v1012 {
                position: sticky;
                top: 0;
                z-index: 49000;
                display: flex;
                align-items: center;
                justify-content: space-between;
                gap: .8rem;
                padding: .62rem .9rem;
                border-bottom: 1px solid rgba(34, 211, 238, .35);
                background: rgba(8, 15, 29, .96);
                backdrop-filter: blur(12px);
                box-shadow: 0 10px 30px rgba(0, 0, 0, .22);
            }
            .ts-live-banner-v1012__copy {
                min-width: 0;
                display: grid;
                gap: .08rem;
            }
            .ts-live-banner-v1012__copy small {
                font-size: .68rem;
                letter-spacing: .1em;
                text-transform: uppercase;
                opacity: .72;
            }
            .ts-live-banner-v1012__line {
                display: flex;
                gap: .55rem;
                align-items: baseline;
                min-width: 0;
            }
            .ts-live-banner-v1012__name {
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
                font-weight: 800;
            }
            .ts-live-banner-v1012__time {
                font-variant-numeric: tabular-nums;
                font-weight: 900;
                color: #67e8f9;
            }
            .ts-live-banner-v1012__actions {
                display: flex;
                gap: .45rem;
                flex: 0 0 auto;
            }
            .ts-live-banner-v1012__actions a {
                text-decoration: none;
            }
            .ts-filter-banner-v1012 {
                margin: 0 0 .85rem;
                padding: .7rem .85rem;
                border: 1px solid rgba(34, 211, 238, .28);
                border-radius: .8rem;
                background: rgba(15, 23, 42, .5);
            }
            .ts-filter-banner-v1012 strong {
                display: block;
                margin-bottom: .18rem;
            }
            @media (max-width: 640px) {
                .ts-live-banner-v1012 {
                    align-items: stretch;
                    flex-direction: column;
                }
                .ts-live-banner-v1012__actions {
                    width: 100%;
                }
                .ts-live-banner-v1012__actions a {
                    flex: 1 1 0;
                    text-align: center;
                }
            }
        `;
        document.head.appendChild(style);
    };

    const formatElapsed = milliseconds => {
        const total = Math.max(0, Math.floor(milliseconds / 1000));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const seconds = total % 60;

        return `${String(hours).padStart(2, "0")}:`
            + `${String(minutes).padStart(2, "0")}:`
            + `${String(seconds).padStart(2, "0")}`;
    };

    const preserveTrainingFilter = filter => {
        document
            .querySelectorAll(".wi-start-category-tabs a")
            .forEach(link => {
                try {
                    const url = new URL(link.href, location.origin);
                    url.searchParams.set("trainingFilter", filter);
                    link.href = url.toString();
                }
                catch {
                    // Leave the original link untouched if parsing fails.
                }
            });
    };

    const applyWorkoutFilter = () => {
        if (!location.pathname.toLowerCase().startsWith("/detailedworkout")) {
            return;
        }

        const params = new URLSearchParams(location.search);
        const mode = (params.get("mode") || "").toLowerCase();
        const filter = (params.get("trainingFilter") || "").toLowerCase();

        if (mode !== "start" || (filter !== "cardio" && filter !== "strength")) {
            return;
        }

        preserveTrainingFilter(filter);

        const content = document.querySelector(".wi-start-category-content");
        const heading = content?.querySelector(".wi-start-section-heading h2");
        const paragraph = content?.querySelector(".wi-start-section-heading p");

        if (content && !content.querySelector(".ts-filter-banner-v1012")) {
            const banner = document.createElement("div");
            banner.className = "ts-filter-banner-v1012";
            banner.innerHTML = filter === "cardio"
                ? "<strong>Start a cardio segment</strong><span>Select the cardio type you are actually doing. It will receive its own independent timer.</span>"
                : "<strong>Start a strength segment</strong><span>Select a strength workout. It will start a new independent segment timer.</span>";
            content.prepend(banner);
        }

        if (filter === "cardio") {
            if (heading) heading.textContent = "Choose your cardio type";
            if (paragraph) paragraph.textContent = "Each cardio choice is tracked as its own duration/intensity segment.";
        }
        else {
            if (heading) heading.textContent = "Choose your strength workout";
            if (paragraph) paragraph.textContent = "Cardio System Workouts are hidden while you choose the next strength segment.";
        }

        document
            .querySelectorAll("form.wi-clickable-workout-form")
            .forEach(form => {
                const codeInput = form.querySelector('input[name="systemCode"]');
                if (!(codeInput instanceof HTMLInputElement)) {
                    return;
                }

                const code = codeInput.value.trim().toUpperCase();
                const cardio = CARDIO_CODES.has(code);

                form.hidden = filter === "cardio" ? !cardio : cardio;
            });
    };

    const renderBanner = status => {
        if (!status?.active || !status.startedAtUtc) {
            return;
        }

        const params = new URLSearchParams(location.search);
        const currentSessionId = params.get("workoutSessionId");
        const onLiveWorkout =
            location.pathname.toLowerCase().startsWith("/detailedworkout")
            && currentSessionId
            && currentSessionId.toLowerCase()
                === String(status.workoutSessionId || "").toLowerCase();

        if (onLiveWorkout
            || location.pathname.toLowerCase().startsWith("/detailedworkout/review")
            || location.pathname.toLowerCase().startsWith("/detailedworkout/complete")) {
            return;
        }

        if (document.getElementById("ts-live-banner-v1012")) {
            return;
        }

        const started = Date.parse(status.startedAtUtc);
        const serverNow = Date.parse(status.serverNowUtc || "");
        const offset = Number.isFinite(serverNow)
            ? serverNow - Date.now()
            : 0;

        if (!Number.isFinite(started)) {
            return;
        }

        const banner = document.createElement("section");
        banner.id = "ts-live-banner-v1012";
        banner.className = "ts-live-banner-v1012";
        banner.setAttribute("aria-label", "Workout in progress");
        banner.innerHTML = `
            <div class="ts-live-banner-v1012__copy">
                <small>WORKOUT IN PROGRESS</small>
                <div class="ts-live-banner-v1012__line">
                    <span class="ts-live-banner-v1012__name"></span>
                    <span class="ts-live-banner-v1012__time">00:00:00</span>
                </div>
            </div>
            <div class="ts-live-banner-v1012__actions">
                <a class="button" data-ts-end>End</a>
                <a class="button primary" data-ts-continue>Continue</a>
            </div>
        `;

        const name = banner.querySelector(".ts-live-banner-v1012__name");
        const time = banner.querySelector(".ts-live-banner-v1012__time");
        const continueLink = banner.querySelector("[data-ts-continue]");
        const endLink = banner.querySelector("[data-ts-end]");

        name.textContent = status.workoutName || "Workout";
        continueLink.href = status.continueUrl || "/DetailedWorkout";
        endLink.href = status.endUrl || status.continueUrl || "/DetailedWorkout";

        const tick = () => {
            time.textContent = formatElapsed(
                (Date.now() + offset) - started);
        };

        tick();
        window.setInterval(tick, 1000);

        const topbar = document.querySelector(".topbar");
        if (topbar?.parentNode) {
            topbar.insertAdjacentElement("afterend", banner);
        }
        else {
            body.prepend(banner);
        }
    };

    const loadStatus = async () => {
        try {
            const response = await fetch(
                "/TrainingSession/Status",
                {
                    credentials: "same-origin",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

            if (!response.ok) {
                return;
            }

            const status = await response.json();
            window.__12wrTrainingSessionStatus = status;
            renderBanner(status);
            window.dispatchEvent(
                new CustomEvent(
                    "12wr:training-session-status",
                    { detail: status }));
        }
        catch {
            // The workout pages remain usable if the status banner cannot load.
        }
    };

    const initialize = () => {
        ensureStyles();
        applyWorkoutFilter();
        void loadStatus();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    }
    else {
        initialize();
    }
})();
