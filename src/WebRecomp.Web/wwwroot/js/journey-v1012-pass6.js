(() => {
    "use strict";

    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const FALLBACK_IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";

    const buildCoachOverlay = () => {
        const coach = document.querySelector(
            ".recomp-dashboard .journey-mission-coach, .recomp-dashboard .dash-coach"
        );

        if (!coach) return false;
        if (coach.classList.contains("journey-coach-pass6")) return true;

        const title = coach.querySelector(":scope > h2");
        const recommendation = coach.querySelector(":scope > .dash-coach-copy");
        const guideCard = coach.querySelector(":scope > .dash-coach-guide-card");
        const brief = coach.querySelector(":scope > .dash-coach-brief-launch");
        const factors = coach.querySelector(":scope > .dash-coach-factors");
        const why = coach.querySelector(":scope > .dash-why");
        const guided = coach.querySelector(":scope > .guided-demo-callout");

        if (!title || !recommendation) return false;

        const titleHtml = title.innerHTML;
        const recommendationHtml = recommendation.innerHTML;

        const portrait =
            guideCard?.querySelector(".dash-coach-guide-card__portrait")?.getAttribute("src")
            || FALLBACK_IMAGE;

        const featureAsset =
            guideCard?.getAttribute("data-coach-guide-asset")
            || portrait
            || FALLBACK_IMAGE;

        const coachName =
            guideCard?.querySelector(".dash-coach-guide-card__copy strong")?.textContent?.trim()
            || "YOUR COACH";

        const overlay = document.createElement("section");
        overlay.className = "journey-coach-overlay-card";
        overlay.setAttribute("aria-label", "Coach guidance");

        const bg = document.createElement("div");
        bg.className = "journey-coach-overlay-bg";
        bg.setAttribute("aria-hidden", "true");
        bg.style.backgroundImage = `url("${String(featureAsset).replaceAll('"', "%22")}")`;

        const copy = document.createElement("div");
        copy.className = "journey-coach-overlay-copy";
        copy.innerHTML = `
            <p class="journey-coach-overlay-kicker">COACH'S CALL</p>
            <h2 class="journey-coach-overlay-title">${titleHtml}</h2>
            <p class="journey-coach-overlay-say">${recommendationHtml}</p>
            <ul class="journey-coach-overlay-points" aria-label="Coach focus">
                <li>Eat with purpose</li>
                <li>Train with intent</li>
                <li>Stack the next win</li>
            </ul>
            <div class="journey-coach-overlay-name">
                12 WEEK RECOMP · <strong>${coachName}</strong>
            </div>
        `;

        overlay.append(bg, copy);

        const data = document.createElement("section");
        data.className = "journey-coach-data-pass6";
        data.setAttribute("aria-label", "Coach signals and tools");

        const dataHead = document.createElement("div");
        dataHead.className = "journey-coach-data-pass6-head";
        dataHead.innerHTML = `
            <div>
                <small>COACH INTELLIGENCE</small>
                <strong>THE DATA BEHIND THE CALL</strong>
            </div>
            <span>Signals · guide · context</span>
        `;
        data.appendChild(dataHead);

        [factors, guideCard, brief, why, guided].forEach(node => {
            if (node) data.appendChild(node);
        });

        coach.classList.add("journey-coach-pass6");
        coach.replaceChildren(overlay, data);

        return true;
    };

    const initialize = () => {
        let attempts = 0;
        const tryBuild = () => {
            attempts += 1;
            if (buildCoachOverlay() || attempts >= 16) return;
            window.setTimeout(tryBuild, 100);
        };
        tryBuild();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once:true });
    } else {
        initialize();
    }
})();
