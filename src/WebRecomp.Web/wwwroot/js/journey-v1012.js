(() => {
    "use strict";

    const IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";
    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview")) {
        return;
    }

    const makeDashboardHero = () => {
        if (!path.startsWith("/dashboard")) {
            return;
        }

        const dashboard = document.querySelector(".recomp-dashboard");
        if (!dashboard || document.querySelector(".journey-hero")) {
            return;
        }

        body.classList.add("journey-dashboard");

        const hero = document.createElement("section");
        hero.className = "journey-hero";
        hero.setAttribute("aria-label", "12 Week Recomp journey");
        hero.innerHTML = `
            <div class="journey-hero-copy">
                <p class="journey-hero-kicker">12 WEEK RECOMP</p>
                <h1>REBUILD <span>MOTIVATION</span></h1>
                <p class="journey-hero-line">Break free. Reclaim control. Live stronger.</p>
                <div class="journey-keywords" aria-label="Program pillars">
                    <span>Break Free</span>
                    <span>Build Momentum</span>
                    <span>Reclaim Control</span>
                    <span>Live Stronger</span>
                </div>
                <div class="journey-hero-actions">
                    <a class="journey-hero-action primary-action" href="/DailyEntry">START TODAY</a>
                    <a class="journey-hero-action secondary-action" href="/Progress">SEE PROGRESS</a>
                </div>
            </div>
            <div class="journey-hero-art" aria-hidden="true">
                <img src="${IMAGE}" alt="" loading="eager" fetchpriority="high" />
            </div>
        `;

        const breakbar = document.createElement("section");
        breakbar.className = "journey-breakbar";
        breakbar.setAttribute("aria-label", "Recomp focus");
        breakbar.innerHTML = `
            <div>Cut the Noise</div>
            <div>Stack Wins</div>
            <div>Build Strength</div>
            <div>Feel Alive</div>
        `;

        dashboard.parentNode.insertBefore(hero, dashboard);
        dashboard.parentNode.insertBefore(breakbar, dashboard);
    };

    const makeDailyEntryHero = () => {
        if (!path.startsWith("/dailyentry")) {
            return;
        }

        const page = document.querySelector(".daily-entry-page");
        const header = page?.querySelector(".daily-entry-header");

        if (!page || !header || document.querySelector(".journey-daily-hero")) {
            return;
        }

        body.classList.add("journey-daily-entry");

        const hero = document.createElement("section");
        hero.className = "journey-daily-hero";
        hero.setAttribute("aria-label", "Daily momentum");
        hero.innerHTML = `
            <div class="journey-daily-copy">
                <small>DAILY MOMENTUM</small>
                <strong>TODAY <span>COUNTS.</span></strong>
                <p>Check in. Train. Fuel. Recover.</p>
            </div>
            <div class="journey-daily-art" aria-hidden="true">
                <img src="${IMAGE}" alt="" loading="eager" />
            </div>
        `;

        page.insertBefore(hero, header);

        const kicker = header.querySelector(".daily-entry-kicker");
        if (kicker) {
            kicker.textContent = "CHECK IN • TRAIN • FUEL • RECOVER";
        }
    };

    const initialize = () => {
        makeDashboardHero();
        makeDailyEntryHero();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
