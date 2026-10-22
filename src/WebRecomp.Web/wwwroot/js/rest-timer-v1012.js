(() => {
    "use strict";

    // TEST v1012 compatibility slot. The old rest countdown is intentionally
    // gone; this file now owns the lightweight live-workout controls that sit
    // beside the authoritative workout count-up timer.
    if (!window.location.pathname.toLowerCase().startsWith("/detailedworkout")) {
        return;
    }

    const params = new URLSearchParams(window.location.search);
    const timerHost = document.querySelector("[data-live-workout-start]");
    const sessionId = timerHost?.dataset.liveWorkoutSessionId || "";

    if (!timerHost || !sessionId) {
        return;
    }

    const workoutForm = document.getElementById("wi-detailed-workout-form");
    const exerciseList = document.getElementById("wi-exercise-list");
    const typeInput = document.querySelector('input[name="DraftWorkoutTypeCode"]');
    const editCompletedInput = document.querySelector('input[name="EditCompleted"]');

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

    const workoutTypeCode = (typeInput?.value || "").trim().toUpperCase();
    const cardioSession = CARDIO_CODES.has(workoutTypeCode);
    const editingCompleted =
        String(editCompletedInput?.value || "").trim().toLowerCase() === "true";

    const ensureStyles = () => {
        if (document.getElementById("wi-end-workout-v1012-style")) return;

        const style = document.createElement("style");
        style.id = "wi-end-workout-v1012-style";
        style.textContent = `
            .wi-live-elapsed strong[data-live-workout-elapsed] {
                display: block;
                margin-top: .08rem;
                font-size: clamp(2.15rem, 5.4vw, 3.25rem) !important;
                line-height: 1 !important;
                letter-spacing: .035em;
                font-variant-numeric: tabular-nums;
            }
            .ts-live-banner-v1012__time {
                font-size: 1.2rem !important;
                line-height: 1 !important;
            }
            .wi-live-history-toolbar-v1012 {
                display: flex;
                flex-wrap: wrap;
                align-items: center;
                gap: .45rem;
                margin-top: .55rem;
            }
            .wi-live-history-toolbar-v1012[hidden] {
                display: none !important;
            }
            .wi-live-history-note-v1012,
            .wi-live-autosave-status-v1012 {
                font-size: .78rem;
                opacity: .78;
            }
            .wi-live-autosave-status-v1012.is-saving {
                opacity: 1;
            }
            .wi-live-autosave-status-v1012.is-error {
                opacity: 1;
                color: #fecaca;
            }
            .wi-live-page-minimal input.wi-live-entered-v1012 {
                border-color: rgba(103, 232, 249, .55) !important;
                background: rgba(14, 116, 144, .12) !important;
                transition: background .16s ease, border-color .16s ease, box-shadow .16s ease;
            }
            .wi-live-page-minimal input.wi-live-entered-v1012.wi-live-saved-v1012 {
                border-color: rgba(134, 239, 172, .48) !important;
                background: rgba(22, 101, 52, .13) !important;
                box-shadow: inset 0 0 0 1px rgba(134, 239, 172, .08);
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

    // ---------------------------------------------------------------------
    // Screen wake lock: keep an active workout visible on supported phones.
    // The browser/OS can still revoke the lock (for example, low battery), so
    // we reacquire whenever this page returns to the foreground.
    // ---------------------------------------------------------------------
    let wakeLockSentinel = null;
    let wakeLockPending = false;
    let wakePageActive = true;
    let lastWakeAttempt = -Infinity;
    let wakeStatus = null;
    let wakeRetry = null;

    const showWakeStatus = (text, canRetry = false) => {
        if (!wakeStatus) {
            const controls = document.createElement("div");
            controls.style.cssText = "display:flex;align-items:center;flex-wrap:wrap;gap:.5rem;margin-top:.5rem;font-size:.8rem";
            wakeStatus = document.createElement("span");
            wakeStatus.setAttribute("role", "status");
            wakeRetry = document.createElement("button");
            wakeRetry.type = "button";
            wakeRetry.className = "button";
            wakeRetry.textContent = "Keep screen awake";
            wakeRetry.addEventListener("click", () => void requestWakeLock(true));
            controls.append(wakeStatus, wakeRetry);
            timerHost.insertAdjacentElement("afterend", controls);
        }
        wakeStatus.textContent = text;
        wakeRetry.hidden = !canRetry;
    };

    const requestWakeLock = async (fromGesture = false) => {
        if (editingCompleted
            || !wakePageActive
            || document.visibilityState !== "visible"
            || wakeLockPending
            || (wakeLockSentinel && !wakeLockSentinel.released)) {
            return;
        }

        if (!("wakeLock" in navigator)) {
            showWakeStatus("Keep the screen on using your device settings.");
            return;
        }
        // Retry transient releases without repeatedly hammering an OS denial.
        if (!fromGesture && Date.now() - lastWakeAttempt < 15000) return;
        lastWakeAttempt = Date.now();
        wakeLockPending = true;
        showWakeStatus("Keeping screen awake…");
        try {
            const lock = await navigator.wakeLock.request("screen");
            if (!wakePageActive || document.visibilityState !== "visible") {
                await lock.release();
                return;
            }
            wakeLockSentinel = lock;
            showWakeStatus("Screen awake");

            lock.addEventListener(
                "release",
                () => {
                    if (wakeLockSentinel !== lock) return;
                    wakeLockSentinel = null;
                    showWakeStatus("Screen may sleep.", true);
                },
                { once: true });
        }
        catch {
            showWakeStatus("Screen may sleep.", true);
        }
        finally {
            wakeLockPending = false;
        }
    };

    const releaseWakeLock = async () => {
        const current = wakeLockSentinel;
        wakeLockSentinel = null;

        if (!current) return;

        try {
            await current.release();
        }
        catch {
            // Already released by the browser/OS.
        }
    };

    if (!editingCompleted) {
        void requestWakeLock();

        document.addEventListener("visibilitychange", () => {
            if (!document.hidden) {
                lastWakeAttempt = -Infinity;
                void requestWakeLock();
            } else void releaseWakeLock();
        });

        window.addEventListener("focus", () => void requestWakeLock(), { passive: true });
        window.addEventListener("pageshow", () => {
            wakePageActive = true;
            lastWakeAttempt = -Infinity;
            void requestWakeLock();
        }, { passive: true });
        window.addEventListener("pagehide", () => {
            wakePageActive = false;
            void releaseWakeLock();
        }, { passive: true });
        document.addEventListener("pointerdown", () => void requestWakeLock(true), { passive: true });
        document.addEventListener("keydown", () => void requestWakeLock(true));
        window.setInterval(() => void requestWakeLock(), 15000);
    }

    // ---------------------------------------------------------------------
    // Live strength-workout persistence.
    // ---------------------------------------------------------------------
    let toolbar = null;
    let autosaveStatus = null;
    let autosaveTimer = null;
    let autosaveInFlight = false;
    let autosaveQueued = false;
    let structureDirty = false;
    let autosaveDisabled = false;
    let autosaveAbortController = null;
    const dirtyFields = new Set();

    const ensureToolbar = () => {
        if (toolbar) return toolbar;

        toolbar = document.createElement("div");
        toolbar.className = "wi-live-history-toolbar-v1012";
        toolbar.id = "wi-live-history-toolbar-v1012";
        toolbar.hidden = false;

        autosaveStatus = document.createElement("span");
        autosaveStatus.className = "wi-live-autosave-status-v1012";
        autosaveStatus.setAttribute("aria-live", "polite");
        toolbar.appendChild(autosaveStatus);

        timerHost.insertAdjacentElement("afterend", toolbar);
        return toolbar;
    };

    const setAutosaveStatus = (text, state = "") => {
        const host = ensureToolbar();
        host.hidden = false;

        autosaveStatus.textContent = text || "";
        autosaveStatus.classList.toggle("is-saving", state === "saving");
        autosaveStatus.classList.toggle("is-error", state === "error");
    };

    const trackedInput = element =>
        element instanceof HTMLInputElement
        && (
            element.matches('input[name="WorkoutName"]')
            || (
                element.type !== "hidden"
                && element.name.startsWith("Exercises[")
            )
        );

    const markEntered = input => {
        if (!(input instanceof HTMLInputElement)) return;

        const hasValue = input.value.trim() !== "";
        input.classList.toggle("wi-live-entered-v1012", hasValue);
        input.classList.remove("wi-live-saved-v1012");

        if (hasValue) dirtyFields.add(input);
        else dirtyFields.delete(input);
    };

    const stopAutosave = () => {
        autosaveDisabled = true;
        window.clearTimeout(autosaveTimer);
        autosaveTimer = null;
        autosaveAbortController?.abort();
        autosaveAbortController = null;
    };

    const runAutosave = async () => {
        if (autosaveDisabled
            || cardioSession
            || editingCompleted
            || !(workoutForm instanceof HTMLFormElement)) {
            return;
        }

        if (autosaveInFlight) {
            autosaveQueued = true;
            return;
        }

        if (dirtyFields.size === 0 && !structureDirty) return;

        if (!workoutForm.checkValidity()) {
            setAutosaveStatus("Waiting for a valid entry…");
            return;
        }

        autosaveInFlight = true;
        autosaveQueued = false;

        const fieldsBeingSaved = [...dirtyFields];
        dirtyFields.clear();
        structureDirty = false;
        setAutosaveStatus("Saving…", "saving");

        const formData = new FormData(workoutForm);
        formData.set("WorkoutSessionId", sessionId);
        formData.set("pausedSeconds", "0");

        autosaveAbortController = new AbortController();

        try {
            const response = await fetch(
                "/TrainingSession/Autosave",
                {
                    method: "POST",
                    credentials: "same-origin",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    body: formData,
                    signal: autosaveAbortController.signal
                });

            if (!response.ok) {
                throw new Error(`Autosave failed (${response.status}).`);
            }

            fieldsBeingSaved.forEach(input => {
                if (input.isConnected
                    && input.value.trim() !== "") {
                    input.classList.add("wi-live-saved-v1012");
                }
            });

            const savedAt = new Date();
            setAutosaveStatus(
                `Saved ${savedAt.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}`);
        }
        catch (error) {
            if (error?.name !== "AbortError") {
                fieldsBeingSaved.forEach(input => {
                    if (input.isConnected) dirtyFields.add(input);
                });
                structureDirty = true;
                setAutosaveStatus("Not saved — will retry", "error");
            }
        }
        finally {
            autosaveInFlight = false;
            autosaveAbortController = null;

            if (!autosaveDisabled
                && (autosaveQueued || dirtyFields.size > 0 || structureDirty)) {
                autosaveQueued = false;
                window.clearTimeout(autosaveTimer);
                autosaveTimer = window.setTimeout(runAutosave, 350);
            }
        }
    };

    const scheduleAutosave = (delay = 700) => {
        if (autosaveDisabled || cardioSession || editingCompleted) return;
        window.clearTimeout(autosaveTimer);
        autosaveTimer = window.setTimeout(runAutosave, delay);
    };

    if (!cardioSession
        && !editingCompleted
        && workoutForm instanceof HTMLFormElement) {
        ensureToolbar();

        workoutForm.addEventListener("input", event => {
            if (!trackedInput(event.target)) return;
            markEntered(event.target);
            scheduleAutosave();
        });

        workoutForm.addEventListener("change", event => {
            if (!trackedInput(event.target)) return;
            markEntered(event.target);
            scheduleAutosave(250);
        });

        if (exerciseList) {
            const structureObserver = new MutationObserver(() => {
                structureDirty = true;
                scheduleAutosave(500);
            });

            structureObserver.observe(
                exerciseList,
                {
                    childList: true,
                    subtree: true
                });
        }
    }

    // ---------------------------------------------------------------------
    // Prior workout helper. It only appears when a completed workout with the
    // same template/system identity exists, and it fills matching exercises
    // without changing today's exercise selection.
    // ---------------------------------------------------------------------
    let previousWorkout = null;

    const formatWorkoutDate = isoDate => {
        if (!isoDate) return "last workout";
        const parsed = new Date(`${isoDate}T12:00:00`);
        if (!Number.isFinite(parsed.getTime())) return isoDate;
        return parsed.toLocaleDateString([], { month: "short", day: "numeric" });
    };

    const findCard = exerciseId =>
        [...document.querySelectorAll("[data-exercise-card]")]
            .find(card =>
                String(card.dataset.exerciseId || "").toLowerCase()
                === String(exerciseId || "").toLowerCase());

    const applyPreviousWorkout = () => {
        if (!previousWorkout?.exercises?.length) return;

        let changedCount = 0;

        previousWorkout.exercises.forEach(previous => {
            const card = findCard(previous.exerciseId);
            if (!card) return;

            const setsInput = card.querySelector("[data-set-count]");
            if (setsInput instanceof HTMLInputElement
                && Number(previous.sets) > 0) {
                setsInput.value = String(previous.sets);
                markEntered(setsInput);
                setsInput.dispatchEvent(new Event("input", { bubbles: true }));
                changedCount += 1;
            }

            const repInputs = [...card.querySelectorAll("[data-set-rep]")];
            const priorReps = Array.isArray(previous.setReps)
                ? previous.setReps
                : [];

            repInputs.forEach((input, index) => {
                const previousRep = Number(priorReps[index]);
                if (!(input instanceof HTMLInputElement)
                    || !Number.isFinite(previousRep)
                    || previousRep <= 0) {
                    return;
                }

                input.value = String(previousRep);
                markEntered(input);
                input.dispatchEvent(new Event("input", { bubbles: true }));
                changedCount += 1;
            });

            const weightInput = card.querySelector('[data-field="MaxWeight"]');
            if (weightInput instanceof HTMLInputElement) {
                weightInput.value = previous.maxWeight == null
                    ? ""
                    : String(Math.round(Number(previous.maxWeight)));
                markEntered(weightInput);
                weightInput.dispatchEvent(new Event("input", { bubbles: true }));
                changedCount += 1;
            }
        });

        if (changedCount > 0) {
            structureDirty = true;
            setAutosaveStatus(
                `Loaded ${formatWorkoutDate(previousWorkout.workoutDate)} — saving…`,
                "saving");
            scheduleAutosave(100);
        }
    };

    const loadPreviousWorkoutOption = async () => {
        if (cardioSession
            || editingCompleted
            || !(workoutForm instanceof HTMLFormElement)) {
            return;
        }

        try {
            const response = await fetch(
                `/TrainingSession/LastWorkout?workoutSessionId=${encodeURIComponent(sessionId)}`,
                {
                    credentials: "same-origin",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

            if (!response.ok) return;

            const data = await response.json();
            if (!data?.available || !Array.isArray(data.exercises) || data.exercises.length === 0) {
                return;
            }

            previousWorkout = data;
            const host = ensureToolbar();

            if (document.getElementById("wi-use-last-workout-v1012")) return;

            const button = document.createElement("button");
            button.type = "button";
            button.id = "wi-use-last-workout-v1012";
            button.className = "button";
            button.textContent = "Use Last Workout";
            button.addEventListener("click", applyPreviousWorkout);

            const note = document.createElement("span");
            note.className = "wi-live-history-note-v1012";
            note.textContent = `Last: ${formatWorkoutDate(data.workoutDate)}`;

            host.insertBefore(button, autosaveStatus);
            host.insertBefore(note, autosaveStatus);
        }
        catch {
            // History helper is optional; live entry remains fully usable.
        }
    };

    void loadPreviousWorkoutOption();

    // ---------------------------------------------------------------------
    // End / discard menu.
    // ---------------------------------------------------------------------
    let bypassStopPrompt = false;
    let modal = null;

    const getStopButton = () =>
        document.querySelector('[name="stopWorkout"]');

    const getCardioForm = () =>
        document.querySelector("#wi-cardio-live-v1012 form");

    const isCardio = () =>
        Boolean(document.getElementById("wi-cardio-live-v1012"))
        || cardioSession;

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
        stopAutosave();
        void releaseWakeLock();

        const cardioForm = getCardioForm();
        if (cardioForm instanceof HTMLFormElement) {
            cardioForm.requestSubmit();
            return;
        }

        const stopButton = getStopButton();
        const form = document.getElementById("wi-detailed-workout-form");

        if (!(stopButton instanceof HTMLButtonElement)
            || !(form instanceof HTMLFormElement)) {
            return;
        }

        bypassStopPrompt = true;
        form.requestSubmit(stopButton);
    };

    const submitDiscard = () => {
        const token = getToken();
        if (!token) return;

        closeMenu();
        stopAutosave();
        void releaseWakeLock();

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

