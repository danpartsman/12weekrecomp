(() => {
    "use strict";

    const IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";
    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview")) {
        return;
    }

    if (
        !document.querySelector(
            'script[data-12wr-training-session="v1012"]')
    ) {
        const trainingSessionScript =
            document.createElement("script");

        trainingSessionScript.src =
            "/js/training-session-v1012.js?v=1012session1";

        trainingSessionScript.dataset["12wrTrainingSession"] =
            "v1012";

        document.head.appendChild(
            trainingSessionScript);
    }

    if (
        path.startsWith("/nutrition")
        && !document.querySelector(
            'script[data-12wr-nutrition-amounts="v1012"]')
    ) {
        const nutritionAmountsScript =
            document.createElement("script");

        nutritionAmountsScript.src =
            "/js/nutrition-picker-amounts-v1012.js?v=1012amount2";

        nutritionAmountsScript.dataset["12wrNutritionAmounts"] =
            "v1012";

        document.head.appendChild(
            nutritionAmountsScript);
    }

    if (
        path.startsWith("/detailedworkout")
        && !document.querySelector(
            'script[data-12wr-live-workout="v1012"]')
    ) {
        const liveWorkoutScript =
            document.createElement("script");

        liveWorkoutScript.src =
            "/js/live-workout-v1012.js?v=1012live2";

        liveWorkoutScript.dataset["12wrLiveWorkout"] =
            "v1012";

        document.head.appendChild(
            liveWorkoutScript);
    }

    if (!document.querySelector('link[data-12wr-journey-pass2="v1012"]')) {
        const pass2Style = document.createElement("link");
        pass2Style.rel = "stylesheet";
        pass2Style.href = "/css/journey-v1012-pass2.css";
        pass2Style.dataset["12wrJourneyPass2"] = "v1012";
        document.head.appendChild(pass2Style);
    }

    if (!document.querySelector('script[data-12wr-journey-pass2="v1012"]')) {
        const pass2Script = document.createElement("script");
        pass2Script.src = "/js/journey-v1012-pass2.js";
        pass2Script.dataset["12wrJourneyPass2"] = "v1012";
        document.head.appendChild(pass2Script);
    }

    if (!document.querySelector('link[data-12wr-journey-pass3="v1012"]')) {
        const pass3Style = document.createElement("link");
        pass3Style.rel = "stylesheet";
        pass3Style.href = "/css/journey-v1012-pass3.css";
        pass3Style.dataset["12wrJourneyPass3"] = "v1012";
        document.head.appendChild(pass3Style);
    }

    if (!document.querySelector('script[data-12wr-journey-pass3="v1012"]')) {
        const pass3Script = document.createElement("script");
        pass3Script.src = "/js/journey-v1012-pass3.js";
        pass3Script.dataset["12wrJourneyPass3"] = "v1012";
        document.head.appendChild(pass3Script);
    }

    if (!document.querySelector('link[data-12wr-journey-pass4="v1012"]')) {
        const pass4Style = document.createElement("link");
        pass4Style.rel = "stylesheet";
        pass4Style.href = "/css/journey-v1012-pass4.css";
        pass4Style.dataset["12wrJourneyPass4"] = "v1012";
        document.head.appendChild(pass4Style);
    }

    if (!document.querySelector('script[data-12wr-journey-pass4="v1012"]')) {
        const pass4Script = document.createElement("script");
        pass4Script.src = "/js/journey-v1012-pass4.js";
        pass4Script.dataset["12wrJourneyPass4"] = "v1012";
        document.head.appendChild(pass4Script);
    }

    if (!document.querySelector('link[data-12wr-journey-pass8="v1012"]')) {
        const pass8Style = document.createElement("link");
        pass8Style.rel = "stylesheet";
        pass8Style.href = "/css/journey-v1012-pass8.css?v=1012p8c";
        pass8Style.dataset["12wrJourneyPass8"] = "v1012";
        document.head.appendChild(pass8Style);
    }

    if (!document.querySelector('script[data-12wr-journey-pass8="v1012"]')) {
        const pass8Script = document.createElement("script");
        pass8Script.src = "/js/journey-v1012-pass8.js?v=1012p8c";
        pass8Script.dataset["12wrJourneyPass8"] = "v1012";
        document.head.appendChild(pass8Script);
    }

    if (!document.querySelector('link[data-12wr-journey-pass9="v1012"]')) {
        const pass9Style = document.createElement("link");
        pass9Style.rel = "stylesheet";
        pass9Style.href = "/css/journey-v1012-pass9.css?v=1012p9";
        pass9Style.dataset["12wrJourneyPass9"] = "v1012";
        document.head.appendChild(pass9Style);
    }

    if (!document.querySelector('script[data-12wr-journey-pass9="v1012"]')) {
        const pass9Script = document.createElement("script");
        pass9Script.src = "/js/journey-v1012-pass9.js?v=1012p9";
        pass9Script.dataset["12wrJourneyPass9"] = "v1012";
        document.head.appendChild(pass9Script);
    }

    if (!document.querySelector('link[data-12wr-journey-pass10="v1012"]')) {
        const pass10Style = document.createElement("link");
        pass10Style.rel = "stylesheet";
        pass10Style.href = "/css/journey-v1012-pass10.css?v=1015marisolmobile2";
        pass10Style.dataset["12wrJourneyPass10"] = "v1012";
        document.head.appendChild(pass10Style);
    }

    if (!document.querySelector('link[data-12wr-journey-pass10-opaque="v1012"]')) {
        const pass10OpaqueStyle = document.createElement("link");
        pass10OpaqueStyle.rel = "stylesheet";
        pass10OpaqueStyle.href = "/css/journey-v1012-pass10-opaque.css?v=1015marisolmobile2";
        pass10OpaqueStyle.dataset["12wrJourneyPass10Opaque"] = "v1012";
        document.head.appendChild(pass10OpaqueStyle);
    }

    if (!document.querySelector('script[data-12wr-journey-pass10="v1012"]')) {
        const pass10Script = document.createElement("script");
        pass10Script.src = "/js/journey-v1012-pass10.js?v=1015marisolmobile2";
        pass10Script.dataset["12wrJourneyPass10"] = "v1012";
        document.head.appendChild(pass10Script);
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
