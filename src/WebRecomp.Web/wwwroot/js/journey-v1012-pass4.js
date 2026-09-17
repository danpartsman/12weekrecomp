(() => {
    "use strict";

    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const icon = {
        train: `
            <svg viewBox="0 0 48 48" aria-hidden="true">
                <path d="M7 18v12M12 15v18M36 15v18M41 18v12M12 24h24" />
            </svg>`,
        fuel: `
            <svg viewBox="0 0 48 48" aria-hidden="true">
                <path d="M13 7v15M9 7v9c0 4 8 4 8 0V7M13 22v19M32 7v34M26 7c0 10 2 15 6 15" />
            </svg>`,
        coach: `
            <svg viewBox="0 0 48 48" aria-hidden="true">
                <circle cx="24" cy="24" r="16" />
                <path d="M28 20l-3 9-9 3 3-9 9-3z" />
            </svg>`
    };

    const makeCap = (kind, kicker, title, line) => {
        const cap = document.createElement("div");
        cap.className = "journey-energy-cap";
        cap.innerHTML = `
            <div class="journey-energy-copy">
                <small>${kicker}</small>
                <strong>${title}</strong>
                <span>${line}</span>
            </div>
            <div class="journey-energy-icon">${icon[kind]}</div>
        `;
        return cap;
    };

    const ensureMissionCap = (panel, kind, kicker, title, line) => {
        if (!panel || panel.querySelector(":scope > .journey-energy-cap")) {
            return;
        }

        panel.insertBefore(makeCap(kind, kicker, title, line), panel.firstChild);

        panel.querySelectorAll(".dash-pa-card").forEach(card => {
            const planned = card.classList.contains("dash-pa-planned");
            card.dataset.energyLabel = planned ? "PLAN" : "ACTUAL";
        });
    };

    const ensureTomorrowCaps = (dashboard) => {
        const cards = [...dashboard.querySelectorAll(".dash-tomorrow-panel")];
        const labels = [
            ["TOMORROW", "TRAIN"],
            ["TOMORROW", "FUEL"],
            ["TOMORROW", "COACH"]
        ];

        cards.slice(0, 3).forEach((card, index) => {
            if (card.querySelector(":scope > .journey-tile-cap")) {
                return;
            }

            const cap = document.createElement("div");
            cap.className = "journey-tile-cap";
            cap.innerHTML = `
                <div>
                    <small>${labels[index]?.[0] ?? "NEXT"}</small>
                    <strong>${labels[index]?.[1] ?? "PLAN"}</strong>
                </div>
            `;
            card.insertBefore(cap, card.firstChild);
        });
    };

    const ensureIntelCap = (dashboard) => {
        const grid = dashboard.querySelector(".dash-analysis-grid");
        const section = grid?.closest("section");
        if (!section || section.querySelector(":scope > .journey-intel-cap")) {
            return;
        }

        section.classList.add("journey-analysis-section");

        const cap = document.createElement("div");
        cap.className = "journey-intel-cap";
        cap.innerHTML = `
            <div>
                <small>ENGINE 3.0</small>
                <strong>READ THE SIGNALS</strong>
            </div>
            <span>Train · Fuel · Recover · Adapt</span>
        `;
        section.insertBefore(cap, section.firstChild);
    };

    const enhance = () => {
        const dashboard = document.querySelector(".recomp-dashboard");
        if (!dashboard) {
            return;
        }

        body.classList.add("journey-dashboard", "journey-dashboard-energy");

        const primaryGrid = dashboard.querySelector(".dash-primary-grid");
        if (primaryGrid) {
            const workout = primaryGrid.querySelector(":scope > .dash-workout");
            const nutrition = primaryGrid.querySelector(":scope > .dash-nutrition");
            const coach = primaryGrid.querySelector(":scope > .dash-coach");

            ensureMissionCap(workout, "train", "MOVE WITH PURPOSE", "TRAIN", "Build strength. Build energy.");
            ensureMissionCap(nutrition, "fuel", "FEED THE WORK", "FUEL", "Protein. Carbs. Recovery.");
            ensureMissionCap(coach, "coach", "CUT THE NOISE", "COACH", "One clear next move.");
        }

        ensureTomorrowCaps(dashboard);
        ensureIntelCap(dashboard);
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => requestAnimationFrame(enhance), { once: true });
    } else {
        requestAnimationFrame(enhance);
    }
})();
