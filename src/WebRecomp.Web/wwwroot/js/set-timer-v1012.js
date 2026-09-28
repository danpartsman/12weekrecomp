(() => {
    "use strict";

    const host = document.querySelector("[data-set-timer]");
    if (!host) return;

    const workoutId = host.dataset.workoutId;
    if (!workoutId) return;

    const durationInput = host.querySelector("[data-set-timer-duration]");
    const display = host.querySelector("[data-set-timer-display]");
    const toggle = host.querySelector("[data-set-timer-toggle]");
    const cycleInput = host.querySelector("[data-set-timer-cycle]");
    const key = `12wr:set-timer:v1:${workoutId}`;

    const parseSeconds = text => {
        const match = String(text || "").trim().match(/^(\d{1,3}):(\d{2})$/);
        if (!match || Number(match[2]) > 59) return null;
        const seconds = Number(match[1]) * 60 + Number(match[2]);
        return seconds >= 1 && seconds <= 36000 ? seconds : null;
    };
    const format = seconds => {
        const total = Math.max(0, Math.ceil(seconds));
        return `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
    };

    let duration = 120;
    let remaining = 120;
    let endsAt = null;
    let cycling = false;

    try {
        const saved = JSON.parse(localStorage.getItem(key) || "{}");
        if (Number.isInteger(saved.duration) && saved.duration > 0 && saved.duration <= 36000) {
            duration = saved.duration;
            remaining = duration;
        }
        cycling = saved.cycling === true;
    } catch { /* Storage is optional. */ }

    durationInput.value = format(duration);
    cycleInput.checked = cycling;

    const save = () => {
        try {
            localStorage.setItem(key, JSON.stringify({ duration, cycling }));
        } catch { /* The timer still works without storage. */ }
    };
    const render = () => {
        if (endsAt !== null) {
            const millisecondsLeft = endsAt - Date.now();
            if (millisecondsLeft <= 0) {
                if (cycling) {
                    const overdue = Math.abs(millisecondsLeft);
                    endsAt = Date.now() + (duration * 1000 - overdue % (duration * 1000));
                    remaining = Math.ceil((endsAt - Date.now()) / 1000);
                } else {
                    endsAt = null;
                    remaining = 0;
                }
            } else {
                remaining = Math.ceil(millisecondsLeft / 1000);
            }
        }
        display.textContent = format(remaining);
        toggle.textContent = endsAt === null ? "Start" : "Stop";
        toggle.setAttribute("aria-pressed", endsAt === null ? "false" : "true");
        host.classList.toggle("is-running", endsAt !== null);
    };

    durationInput.addEventListener("change", () => {
        const seconds = parseSeconds(durationInput.value);
        if (seconds === null) {
            durationInput.setCustomValidity("Enter minutes:seconds, for example 01:30.");
            durationInput.reportValidity();
            return;
        }
        durationInput.setCustomValidity("");
        duration = seconds;
        remaining = seconds;
        if (endsAt !== null) endsAt = Date.now() + seconds * 1000;
        durationInput.value = format(seconds);
        save();
        render();
    });
    durationInput.addEventListener("input", () => durationInput.setCustomValidity(""));
    cycleInput.addEventListener("change", () => {
        cycling = cycleInput.checked;
        save();
    });
    toggle.addEventListener("click", () => {
        if (endsAt !== null) {
            remaining = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
            endsAt = null;
        } else {
            if (remaining <= 0) remaining = duration;
            endsAt = Date.now() + remaining * 1000;
        }
        render();
    });
    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) render();
    });
    render();
    setInterval(render, 250);
})();
