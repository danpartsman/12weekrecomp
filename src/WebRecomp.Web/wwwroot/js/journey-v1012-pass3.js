(() => {
    "use strict";

    const IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";
    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const enhance = () => {
        const dashboard = document.querySelector(".recomp-dashboard");
        if (!dashboard) {
            return;
        }

        body.classList.add("journey-dashboard");

        // Retire the standalone marketing-style hero.  The same visual language
        // is folded into the working Dashboard below.
        document.querySelectorAll(".journey-hero, .journey-breakbar").forEach(element => element.remove());

        const titleRow = dashboard.querySelector(".dash-title-row");
        if (titleRow && !titleRow.querySelector(".journey-integrated-brand")) {
            titleRow.classList.add("journey-dashboard-date");

            const brand = document.createElement("div");
            brand.className = "journey-integrated-brand";
            brand.setAttribute("aria-hidden", "true");
            brand.innerHTML = `
                <img src="${IMAGE}" alt="" loading="eager" fetchpriority="high" />
                <div class="journey-integrated-brand-copy">
                    <small>12 WEEK RECOMP</small>
                    <strong>REBUILD <span>MOTIVATION</span></strong>
                </div>
            `;
            titleRow.appendChild(brand);

            const pills = document.createElement("div");
            pills.className = "journey-integrated-pills";
            pills.setAttribute("aria-label", "Recomp focus");
            pills.innerHTML = `
                <span>Break Free</span>
                <span>Build Momentum</span>
                <span>Live Stronger</span>
            `;
            titleRow.appendChild(pills);
        }

        // Keep mission labels from pass 2, but make the existing cards the visual
        // structure instead of adding another layer above them.
        dashboard.querySelectorAll(".journey-command-heading").forEach(element => element.remove());

        const primaryGrid = dashboard.querySelector(".dash-primary-grid");
        if (primaryGrid) {
            primaryGrid.classList.add("journey-mission-grid");

            const workout = primaryGrid.querySelector(":scope > .dash-workout");
            const nutrition = primaryGrid.querySelector(":scope > .dash-nutrition");
            const coach = primaryGrid.querySelector(":scope > .dash-coach");

            workout?.classList.add("journey-mission-card", "journey-mission-train");
            nutrition?.classList.add("journey-mission-card", "journey-mission-fuel");
            coach?.classList.add("journey-mission-card", "journey-mission-coach");

            const ensureLabel = (panel, title, cue) => {
                if (!panel || panel.querySelector(":scope > .journey-mission-label")) {
                    return;
                }
                const label = document.createElement("div");
                label.className = "journey-mission-label";
                label.innerHTML = `<strong>${title}</strong><small>${cue}</small>`;
                panel.insertBefore(label, panel.firstChild);
            };

            ensureLabel(workout, "TRAIN", "MOVE WITH PURPOSE");
            ensureLabel(nutrition, "FUEL", "FEED THE WORK");
            ensureLabel(coach, "COACH", "CUT THE NOISE");
        }

        dashboard.querySelectorAll(".dash-tomorrow-panel").forEach(card => {
            card.classList.add("journey-card-upgrade");
        });

        dashboard.querySelectorAll(".dash-analysis-card").forEach(card => {
            card.classList.add("journey-card-upgrade");
        });

        const momentum = dashboard.querySelector(".journey-momentum-banner");
        if (momentum) {
            momentum.setAttribute("aria-label", "Build momentum");
        }
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => {
            requestAnimationFrame(enhance);
        }, { once: true });
    } else {
        requestAnimationFrame(enhance);
    }
})();
