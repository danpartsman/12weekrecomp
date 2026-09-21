(() => {
    "use strict";

    const path =
        window.location.pathname.toLowerCase();

    if (!path.startsWith("/detailedworkout")) {
        return;
    }

    const params =
        new URLSearchParams(window.location.search);

    const mode =
        (params.get("mode") || "").trim().toLowerCase();

    // Keep one clear entry point into the workout launcher. The page-level
    // command card remains the primary Start Workout action; the duplicate
    // header action only added another hop to the same screen.
    const duplicateHeaderStart =
        document.querySelector(".wi-header-start-workout");

    if (duplicateHeaderStart) {
        duplicateHeaderStart.hidden = true;
    }

    // Once the user is already in the Start Workout launcher, the command
    // shell above it is redundant. Go directly to Create / My / System choices.
    if (mode === "start") {
        const commandShell =
            document.querySelector(".wi-workout-command-shell");

        if (commandShell) {
            commandShell.hidden = true;
        }
    }

    const timerHost =
        document.querySelector("[data-live-workout-start]");

    const startButton =
        document.querySelector("[data-workout-start-button]");

    if (!timerHost || !(startButton instanceof HTMLButtonElement)) {
        return;
    }

    const workoutForm =
        document.getElementById("wi-detailed-workout-form");

    const searchPanel =
        document.getElementById("wi-exercise-search");

    const searchInput =
        document.getElementById("wi-search-input");

    const sessionId =
        timerHost.dataset.liveWorkoutSessionId || "";

    const timerStorageKey =
        sessionId
            ? `12wr:workout-timer:v2:${sessionId}`
            : null;

    const normalizeLabels = () => {
        if (startButton.textContent.trim() !== "Start Timer") {
            startButton.textContent = "Start Timer";
        }

        const endButton =
            document.querySelector('[name="stopWorkout"]');

        if (
            endButton instanceof HTMLButtonElement
            && endButton.textContent.trim() !== "End Workout"
        ) {
            endButton.textContent = "End Workout";
        }
    };

    normalizeLabels();

    // Make exercise changes discoverable from the top of the live screen.
    if (
        searchPanel
        && !document.getElementById("wi-live-edit-exercises")
    ) {
        const editButton =
            document.createElement("button");

        editButton.type = "button";
        editButton.id = "wi-live-edit-exercises";
        editButton.className = "button wi-live-edit-exercises-button";
        editButton.textContent = "Edit Exercises";

        startButton.insertAdjacentElement(
            "beforebegin",
            editButton);

        editButton.addEventListener(
            "click",
            () => {
                searchPanel.scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });

                window.setTimeout(
                    () => {
                        if (searchInput instanceof HTMLInputElement) {
                            try {
                                searchInput.focus({ preventScroll: true });
                            }
                            catch {
                                searchInput.focus();
                            }
                        }
                    },
                    320);
            });
    }

    let workoutWakeLock = null;
    let keepScreenAwake = false;
    let wakeLockRequestInFlight = false;

    const timerHasStarted = () => {
        if (!timerStorageKey) {
            return false;
        }

        try {
            const raw =
                window.localStorage.getItem(timerStorageKey);

            if (!raw) {
                return false;
            }

            const parsed =
                JSON.parse(raw);

            return parsed?.hasStarted === true;
        }
        catch {
            return false;
        }
    };

    const requestWorkoutWakeLock = async () => {
        if (
            !keepScreenAwake
            || workoutWakeLock
            || wakeLockRequestInFlight
            || document.visibilityState !== "visible"
            || !("wakeLock" in navigator)
        ) {
            return;
        }

        wakeLockRequestInFlight = true;

        try {
            const sentinel =
                await navigator.wakeLock.request("screen");

            workoutWakeLock = sentinel;

            sentinel.addEventListener(
                "release",
                () => {
                    if (workoutWakeLock === sentinel) {
                        workoutWakeLock = null;
                    }
                },
                { once: true });
        }
        catch {
            // Wake Lock is an enhancement only. The timestamp-based workout
            // timer remains authoritative even when the browser or OS denies it.
            workoutWakeLock = null;
        }
        finally {
            wakeLockRequestInFlight = false;
        }
    };

    const releaseWorkoutWakeLock = async () => {
        keepScreenAwake = false;

        const sentinel =
            workoutWakeLock;

        workoutWakeLock = null;

        if (!sentinel) {
            return;
        }

        try {
            await sentinel.release();
        }
        catch {
            // The browser may already have released the lock while hidden.
        }
    };

    // If an already-started workout is resumed or the page reloads, restore
    // the keep-awake behavior from the same persisted timer state used by the
    // existing live timer.
    keepScreenAwake =
        timerHasStarted();

    if (keepScreenAwake) {
        void requestWorkoutWakeLock();
    }

    startButton.addEventListener(
        "click",
        () => {
            keepScreenAwake = true;
            void requestWorkoutWakeLock();
        });

    const bindEndButton = () => {
        const endButton =
            document.querySelector('[name="stopWorkout"]');

        if (
            !(endButton instanceof HTMLButtonElement)
            || endButton.dataset.wakeLockEndBound === "true"
        ) {
            return;
        }

        endButton.dataset.wakeLockEndBound = "true";

        endButton.addEventListener(
            "click",
            () => {
                void releaseWorkoutWakeLock();
            });
    };

    bindEndButton();

    if (workoutForm instanceof HTMLFormElement) {
        workoutForm.addEventListener(
            "submit",
            event => {
                const submitter =
                    event.submitter;

                if (
                    submitter instanceof HTMLButtonElement
                    && submitter.name === "stopWorkout"
                ) {
                    void releaseWorkoutWakeLock();
                }
            });

        // detailed-workout.js may normalize action controls after exercises
        // change. Re-apply the clearer labels without touching calculations.
        const actionObserver =
            new MutationObserver(
                () => {
                    normalizeLabels();
                    bindEndButton();
                });

        actionObserver.observe(
            workoutForm,
            {
                childList: true,
                subtree: true
            });
    }

    const restoreVisibleWorkoutState = () => {
        normalizeLabels();

        if (timerHasStarted()) {
            keepScreenAwake = true;
        }

        if (
            keepScreenAwake
            && document.visibilityState === "visible"
        ) {
            void requestWorkoutWakeLock();
        }
    };

    document.addEventListener(
        "visibilitychange",
        () => {
            if (document.visibilityState === "visible") {
                restoreVisibleWorkoutState();
            }
        });

    window.addEventListener(
        "focus",
        restoreVisibleWorkoutState,
        { passive: true });

    window.addEventListener(
        "pageshow",
        restoreVisibleWorkoutState,
        { passive: true });
})();
