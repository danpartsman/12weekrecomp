(() => {
    "use strict";

    const IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";
    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview")) {
        return;
    }

    const createElement = (tag, className, html) => {
        const element = document.createElement(tag);
        element.className = className;
        if (html) {
            element.innerHTML = html;
        }
        return element;
    };

    const addMissionLabel = (panel, label, cue, className) => {
        if (!panel) {
            return;
        }

        panel.classList.add("journey-mission-card", className);

        if (panel.querySelector(":scope > .journey-mission-label")) {
            return;
        }

        const missionLabel = createElement(
            "div",
            "journey-mission-label",
            `<strong>${label}</strong><small>${cue}</small>`
        );

        panel.insertBefore(missionLabel, panel.firstChild);
    };

    const enhanceDashboard = () => {
        if (!path.startsWith("/dashboard")) {
            return;
        }

        const dashboard = document.querySelector(".recomp-dashboard");
        if (!dashboard) {
            return;
        }

        body.classList.add("journey-dashboard");

        const titleRow = dashboard.querySelector(".dash-title-row");
        titleRow?.classList.add("journey-dashboard-date");

        const primaryGrid = dashboard.querySelector(".dash-primary-grid");

        if (primaryGrid) {
            primaryGrid.classList.add("journey-mission-grid");

            addMissionLabel(
                primaryGrid.querySelector(":scope > .dash-workout"),
                "TRAIN",
                "MOVE WITH PURPOSE",
                "journey-mission-train"
            );

            addMissionLabel(
                primaryGrid.querySelector(":scope > .dash-nutrition"),
                "FUEL",
                "FEED THE WORK",
                "journey-mission-fuel"
            );

            addMissionLabel(
                primaryGrid.querySelector(":scope > .dash-coach"),
                "COACH",
                "CUT THE NOISE",
                "journey-mission-coach"
            );

            if (!dashboard.querySelector(".journey-command-heading")) {
                const commandHeading = createElement(
                    "section",
                    "journey-command-heading",
                    `
                        <div>
                            <small>TODAY</small>
                            <strong>YOUR <span>MISSION</span></strong>
                        </div>
                        <div class="journey-command-words" aria-label="Daily focus">
                            <span>Train</span>
                            <span>Fuel</span>
                            <span>Recover</span>
                            <span>Repeat</span>
                        </div>
                    `
                );

                primaryGrid.parentNode.insertBefore(commandHeading, primaryGrid);
            }
        }

        const tomorrow = dashboard.querySelector(".dash-tomorrow-repeat-section");
        tomorrow?.classList.add("journey-tomorrow-section");

        if (tomorrow && !dashboard.querySelector(".journey-momentum-banner")) {
            const momentum = createElement(
                "section",
                "journey-momentum-banner",
                `
                    <div class="journey-momentum-art" aria-hidden="true">
                        <img src="${IMAGE}" alt="" loading="lazy" />
                    </div>
                    <div class="journey-momentum-copy">
                        <small>REBUILD MOTIVATION</small>
                        <strong>STACK WINS.<span>DRIVE RETURNS.</span></strong>
                        <p>Momentum changes everything.</p>
                        <div class="journey-momentum-keywords" aria-label="Momentum pillars">
                            <span>Move</span>
                            <span>Fuel</span>
                            <span>Recover</span>
                            <span>Repeat</span>
                        </div>
                    </div>
                `
            );

            tomorrow.parentNode.insertBefore(momentum, tomorrow);
        }

        const analysisGrid = dashboard.querySelector(".dash-analysis-grid");
        const analysisSection = analysisGrid?.closest("section");
        analysisSection?.classList.add("journey-analysis-section");
    };

    const makeStepBanner = (section, tone, kicker, word, line) => {
        if (!section) {
            return;
        }

        section.classList.add("journey-step-section", `journey-step-${tone}`);

        if (section.querySelector(":scope > .journey-step-banner")) {
            return;
        }

        const banner = createElement(
            "div",
            "journey-step-banner",
            `
                <div class="journey-step-banner-copy">
                    <small>${kicker}</small>
                    <strong>${word}</strong>
                    <span>${line}</span>
                </div>
            `
        );

        section.insertBefore(banner, section.firstChild);
    };

    const enhanceDailyEntry = () => {
        if (!path.startsWith("/dailyentry")) {
            return;
        }

        const page = document.querySelector(".daily-entry-page");
        if (!page) {
            return;
        }

        body.classList.add("journey-daily-entry");

        makeStepBanner(
            page.querySelector("#body-recovery"),
            "recover",
            "STEP 1",
            "RECOVER",
            "Restore. Reset. Ready."
        );

        makeStepBanner(
            page.querySelector("#training"),
            "train",
            "STEP 2",
            "TRAIN",
            "Move with purpose."
        );

        makeStepBanner(
            page.querySelector("#nutrition"),
            "fuel",
            "STEP 3",
            "FUEL",
            "Fuel the work."
        );

        const form = page.querySelector("#daily-entry-autosave-form");

        if (form && !page.querySelector(".journey-daily-endcap")) {
            const endcap = createElement(
                "section",
                "journey-daily-endcap",
                `
                    <div>
                        <small>BUILD MOMENTUM</small>
                        <strong>ONE MORE DAY <span>BUILT.</span></strong>
                        <p>Small wins. Stronger you.</p>
                    </div>
                `
            );

            form.insertAdjacentElement("afterend", endcap);
        }
    };

    const initialize = () => {
        enhanceDashboard();
        enhanceDailyEntry();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
