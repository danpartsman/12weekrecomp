(() => {
    "use strict";

    if (!location.pathname.toLowerCase().startsWith("/detailedworkout")) {
        return;
    }

    const timerHost =
        document.querySelector("[data-live-workout-start]");

    const typeInput =
        document.querySelector('input[name="DraftWorkoutTypeCode"]');

    if (!timerHost || !(typeInput instanceof HTMLInputElement)) {
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

    const workoutTypeCode =
        typeInput.value.trim().toUpperCase();

    if (CARDIO_CODES.has(workoutTypeCode)) {
        return;
    }

    const sessionId =
        timerHost.dataset.liveWorkoutSessionId || "";

    if (!sessionId) {
        return;
    }

    const storageKey =
        `12wr:rest-timer:v1012:${sessionId}`;

    const style =
        document.createElement("style");

    style.textContent = `
        .wi-rest-timer-v1012 {
            display: grid;
            gap: .45rem;
            margin-top: .55rem;
            padding: .6rem .72rem;
            width: min(100%, 420px);
            border: 1px solid rgba(148, 163, 184, .24);
            border-radius: .8rem;
            background: rgba(15, 23, 42, .45);
        }
        .wi-rest-timer-v1012__top {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            gap: .75rem;
        }
        .wi-rest-timer-v1012__top span {
            font-size: .76rem;
            text-transform: uppercase;
            letter-spacing: .08em;
            opacity: .72;
        }
        .wi-rest-timer-v1012__time {
            font-size: 1.15rem;
            font-weight: 900;
            font-variant-numeric: tabular-nums;
        }
        .wi-rest-timer-v1012.is-running .wi-rest-timer-v1012__time {
            color: #67e8f9;
        }
        .wi-rest-timer-v1012.is-done .wi-rest-timer-v1012__time {
            color: #86efac;
        }
        .wi-rest-timer-v1012__actions {
            display: flex;
            flex-wrap: wrap;
            gap: .35rem;
        }
        .wi-rest-timer-v1012__actions button {
            min-width: 58px;
        }
    `;

    document.head.appendChild(style);

    const panel =
        document.createElement("section");

    panel.className =
        "wi-rest-timer-v1012";

    panel.setAttribute(
        "aria-label",
        "Rest timer");

    panel.innerHTML = `
        <div class="wi-rest-timer-v1012__top">
            <span>Rest timer</span>
            <strong class="wi-rest-timer-v1012__time" data-rest-output>01:30</strong>
        </div>
        <div class="wi-rest-timer-v1012__actions">
            <button type="button" class="button" data-rest-seconds="60">60s</button>
            <button type="button" class="button" data-rest-seconds="90">90s</button>
            <button type="button" class="button" data-rest-seconds="120">120s</button>
            <button type="button" class="button primary" data-rest-start>Start</button>
            <button type="button" class="button" data-rest-reset>Reset</button>
        </div>
    `;

    timerHost.insertAdjacentElement(
        "afterend",
        panel);

    const output =
        panel.querySelector("[data-rest-output]");

    const startButton =
        panel.querySelector("[data-rest-start]");

    const resetButton =
        panel.querySelector("[data-rest-reset]");

    let selectedSeconds = 90;
    let targetAtMs = null;
    let completedSignalShown = false;

    const readState = () => {
        try {
            const raw =
                localStorage.getItem(storageKey);

            if (!raw) return;

            const parsed = JSON.parse(raw);
            const storedSeconds = Number(parsed?.selectedSeconds);
            const storedTarget = Number(parsed?.targetAtMs);

            if (Number.isFinite(storedSeconds)
                && storedSeconds >= 5
                && storedSeconds <= 900) {
                selectedSeconds = storedSeconds;
            }

            if (Number.isFinite(storedTarget)
                && storedTarget > 0) {
                targetAtMs = storedTarget;
            }
        }
        catch {
            // Rest timing remains usable without browser storage.
        }
    };

    const writeState = () => {
        try {
            localStorage.setItem(
                storageKey,
                JSON.stringify({
                    selectedSeconds,
                    targetAtMs
                }));
        }
        catch {
            // No-op when browser storage is unavailable.
        }
    };

    const format = seconds => {
        const safe = Math.max(0, Math.ceil(seconds));
        const minutes = Math.floor(safe / 60);
        const remainder = safe % 60;
        return `${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`;
    };

    const tick = () => {
        if (!(output instanceof HTMLElement)) return;

        if (!targetAtMs) {
            panel.classList.remove("is-running", "is-done");
            output.textContent = format(selectedSeconds);
            return;
        }

        const remainingMs =
            targetAtMs - Date.now();

        if (remainingMs <= 0) {
            output.textContent = "00:00";
            panel.classList.remove("is-running");
            panel.classList.add("is-done");

            if (!completedSignalShown) {
                completedSignalShown = true;
                if (navigator.vibrate) {
                    try { navigator.vibrate([120, 80, 120]); }
                    catch { /* vibration is optional */ }
                }
            }
            return;
        }

        panel.classList.add("is-running");
        panel.classList.remove("is-done");
        output.textContent =
            format(remainingMs / 1000);
    };

    panel
        .querySelectorAll("[data-rest-seconds]")
        .forEach(button => {
            button.addEventListener("click", () => {
                const requested =
                    Number(button.dataset.restSeconds);

                if (!Number.isFinite(requested)) return;

                selectedSeconds = requested;
                targetAtMs = null;
                completedSignalShown = false;
                writeState();
                tick();
            });
        });

    startButton?.addEventListener("click", () => {
        targetAtMs =
            Date.now() + (selectedSeconds * 1000);
        completedSignalShown = false;
        writeState();
        tick();
    });

    resetButton?.addEventListener("click", () => {
        targetAtMs = null;
        completedSignalShown = false;
        writeState();
        tick();
    });

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) tick();
    });

    window.addEventListener("focus", tick, { passive: true });
    window.addEventListener("pageshow", tick, { passive: true });

    readState();
    tick();
    window.setInterval(tick, 250);
})();
