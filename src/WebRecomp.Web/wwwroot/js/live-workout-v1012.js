(() => {
    "use strict";

    const path = window.location.pathname.toLowerCase();
    if (!path.startsWith("/detailedworkout")) return;

    if (!document.querySelector('script[data-12wr-rest-timer="v1012"]')) {
        const restTimerScript = document.createElement("script");
        restTimerScript.src = "/js/rest-timer-v1012.js?v=1012rest3";
        restTimerScript.dataset["12wrRestTimer"] = "v1012";
        document.head.appendChild(restTimerScript);
    }

    const params = new URLSearchParams(window.location.search);
    const mode = (params.get("mode") || "").trim().toLowerCase();

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

    const duplicateHeaderStart = document.querySelector(".wi-header-start-workout");
    if (duplicateHeaderStart) duplicateHeaderStart.hidden = true;

    if (mode === "start") {
        const commandShell = document.querySelector(".wi-workout-command-shell");
        if (commandShell) commandShell.hidden = true;
    }

    const timerHost = document.querySelector("[data-live-workout-start]");
    if (!timerHost) return;

    const output = timerHost.querySelector("[data-live-workout-elapsed]");
    const workoutForm = document.getElementById("wi-detailed-workout-form");
    const searchPanel = document.getElementById("wi-exercise-search");
    const searchInput = document.getElementById("wi-search-input");
    const pauseButton = document.querySelector("[data-workout-pause-toggle]");
    const pausedSecondsInput = document.querySelector("[data-workout-paused-seconds]");
    const typeInput = document.querySelector('input[name="DraftWorkoutTypeCode"]');
    const sessionId = timerHost.dataset.liveWorkoutSessionId || "";
    const started = Date.parse(timerHost.dataset.liveWorkoutStart || "");
    const workoutTypeCode = (typeInput?.value || "").trim().toUpperCase();
    const isCardio = CARDIO_CODES.has(workoutTypeCode);
    const timerStorageKey = sessionId ? `12wr:workout-timer:v2:${sessionId}` : null;

    let serverClockOffsetMs = 0;

    const formatElapsed = milliseconds => {
        const total = Math.max(0, Math.floor(milliseconds / 1000));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const seconds = total % 60;
        return `${String(hours).padStart(2, "0")}:`
            + `${String(minutes).padStart(2, "0")}:`
            + `${String(seconds).padStart(2, "0")}`;
    };

    // The server-side WorkoutSession timestamp is authoritative. Prime the
    // legacy page timer as "started" so its old controls cannot disable End,
    // but never use its localStorage pause total for final workout duration.
    if (timerStorageKey) {
        try {
            window.localStorage.setItem(
                timerStorageKey,
                JSON.stringify({
                    accumulatedPausedMs: 0,
                    pausedAtMs: null,
                    hasStarted: true
                }));
        }
        catch {
            // Browser storage is optional; the server timestamp still works.
        }
    }

    const renderServerTimer = () => {
        if (!(output instanceof HTMLElement) || !Number.isFinite(started)) return;
        output.textContent = formatElapsed(
            (Date.now() + serverClockOffsetMs) - started);
    };

    const enforceServerTimerUi = () => {
        if (pauseButton instanceof HTMLButtonElement) {
            pauseButton.hidden = true;
            pauseButton.disabled = true;
        }

        if (pausedSecondsInput instanceof HTMLInputElement) {
            pausedSecondsInput.value = "0";
        }

        const label = timerHost.querySelector("span");
        if (label) label.textContent = isCardio ? "Cardio time" : "Workout time";

        renderServerTimer();
    };

    // formdata fires after the legacy submit listener. This guarantees the
    // server receives zero paused seconds even if old timer code rewrites the
    // hidden field immediately before submission.
    if (workoutForm instanceof HTMLFormElement) {
        workoutForm.addEventListener(
            "formdata",
            event => event.formData.set("pausedSeconds", "0"));
    }

    const ensureStyles = () => {
        if (document.getElementById("live-workout-server-timer-v1012-style")) return;
        const style = document.createElement("style");
        style.id = "live-workout-server-timer-v1012-style";
        style.textContent = `
            .wi-segment-history-v1012 {
                display: flex;
                flex-wrap: wrap;
                gap: .45rem;
                margin-top: .45rem;
            }
            .wi-segment-history-v1012 span {
                display: inline-flex;
                gap: .35rem;
                align-items: center;
                padding: .28rem .48rem;
                border-radius: 999px;
                border: 1px solid rgba(148, 163, 184, .24);
                background: rgba(15, 23, 42, .5);
                font-size: .78rem;
            }
            .wi-cardio-live-v1012 {
                margin-top: 1rem;
                padding: 1rem;
                border: 1px solid rgba(34, 211, 238, .35);
                border-radius: .9rem;
                background: rgba(8, 15, 29, .56);
            }
            .wi-cardio-live-v1012 h2 { margin-top: 0; }
            .wi-cardio-live-v1012 form {
                display: grid;
                gap: .85rem;
            }
            .wi-cardio-live-v1012 label {
                display: grid;
                gap: .3rem;
                max-width: 220px;
            }
            .wi-cardio-live-v1012 .wi-cardio-note {
                opacity: .78;
                margin: 0;
            }
        `;
        document.head.appendChild(style);
    };

    const renderCompletedSegments = status => {
        const rows = Array.isArray(status?.completedSegments)
            ? status.completedSegments
            : [];

        if (!rows.length || document.getElementById("wi-segment-history-v1012")) return;

        const host = document.createElement("div");
        host.id = "wi-segment-history-v1012";
        host.className = "wi-segment-history-v1012";

        rows.forEach(segment => {
            const pill = document.createElement("span");
            const minutes = Number(segment.durationMin || 0);
            pill.textContent = `${segment.name || segment.workoutTypeCode || "Previous segment"} — ${formatElapsed(minutes * 60000)}`;
            host.appendChild(pill);
        });

        timerHost.insertAdjacentElement("afterend", host);
    };

    const applyStatus = status => {
        if (!status) return;

        const serverNow = Date.parse(status.serverNowUtc || "");
        if (Number.isFinite(serverNow)) {
            serverClockOffsetMs = serverNow - Date.now();
        }

        if (
            String(status.workoutSessionId || "").toLowerCase()
            === sessionId.toLowerCase()
        ) {
            renderCompletedSegments(status);
        }

        renderServerTimer();
    };

    const syncStatus = async () => {
        try {
            const response = await fetch(
                "/TrainingSession/Status",
                {
                    credentials: "same-origin",
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
            if (!response.ok) return;
            applyStatus(await response.json());
        }
        catch {
            // Continue from the persisted server start timestamp if offline.
        }
    };

    window.addEventListener(
        "12wr:training-session-status",
        event => applyStatus(event.detail));

    const addEditExercisesButton = () => {
        if (isCardio || !searchPanel || document.getElementById("wi-live-edit-exercises")) return;

        const editButton = document.createElement("button");
        editButton.type = "button";
        editButton.id = "wi-live-edit-exercises";
        editButton.className = "button wi-live-edit-exercises-button";
        editButton.textContent = "Edit Exercises";
        timerHost.insertAdjacentElement("afterend", editButton);

        editButton.addEventListener("click", () => {
            searchPanel.scrollIntoView({ behavior: "smooth", block: "start" });
            window.setTimeout(() => {
                if (searchInput instanceof HTMLInputElement) {
                    try { searchInput.focus({ preventScroll: true }); }
                    catch { searchInput.focus(); }
                }
            }, 320);
        });
    };

    const convertCardioPage = () => {
        if (!isCardio || !(workoutForm instanceof HTMLFormElement)) return;

        const workoutName =
            workoutForm.querySelector('.wi-name-field input')?.value
            || "Cardio";

        // Cardio is duration/intensity driven. Do not force users to fabricate
        // lifting exercises merely to satisfy the strength-editor workflow.
        workoutForm.hidden = true;

        if (document.getElementById("wi-cardio-live-v1012")) return;

        const token = workoutForm.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
        const panel = document.createElement("section");
        panel.id = "wi-cardio-live-v1012";
        panel.className = "wi-cardio-live-v1012";
        panel.innerHTML = `
            <h2>${workoutName.replace(/[&<>"']/g, character => ({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[character]))}</h2>
            <p class="wi-cardio-note">The cardio timer continues from the server timestamp even if you leave this page or use another app.</p>
            <form action="/TrainingSession/EndCardio" method="post">
                <input type="hidden" name="__RequestVerificationToken" value="${token.replace(/"/g, "&quot;")}" />
                <input type="hidden" name="workoutSessionId" value="${sessionId}" />
                <label>
                    <span>Session RPE (optional)</span>
                    <input type="number" name="sessionRpe" min="1" max="10" step="0.5" inputmode="decimal" placeholder="1-10" />
                </label>
                <button class="button primary" type="submit">End Cardio</button>
            </form>
        `;

        workoutForm.insertAdjacentElement("afterend", panel);
    };

    const handleEndPrompt = () => {
        if (params.get("endPrompt") !== "1") return;

        window.setTimeout(() => {
            if (!window.confirm(isCardio ? "End this cardio segment?" : "End this workout segment?")) {
                const clean = new URL(location.href);
                clean.searchParams.delete("endPrompt");
                history.replaceState({}, "", clean);
                return;
            }

            if (isCardio) {
                document.querySelector("#wi-cardio-live-v1012 form")?.requestSubmit();
                return;
            }

            const stopButton = document.querySelector('[name="stopWorkout"]');
            if (stopButton instanceof HTMLButtonElement) stopButton.click();
        }, 250);
    };

    ensureStyles();
    // Per-exercise Edit Exercise controls replace the old global Edit Exercises button.
    convertCardioPage();
    enforceServerTimerUi();
    void syncStatus();
    handleEndPrompt();

    // One authoritative server-derived timer renders once per second.
    window.setInterval(renderServerTimer, 1000);
    window.setInterval(() => void syncStatus(), 60000);

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) {
            enforceServerTimerUi();
            void syncStatus();
        }
    });

    window.addEventListener("focus", () => {
        enforceServerTimerUi();
        void syncStatus();
    }, { passive: true });

    window.addEventListener("pageshow", () => {
        enforceServerTimerUi();
        void syncStatus();
    }, { passive: true });
})();
