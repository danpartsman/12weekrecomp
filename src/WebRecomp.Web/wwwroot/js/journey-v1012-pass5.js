(() => {
    "use strict";

    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const FALLBACK_IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";

    const buildCoach = () => {
        const coach = document.querySelector(".recomp-dashboard .journey-mission-coach, .recomp-dashboard .dash-coach");
        if (!coach || coach.classList.contains("journey-coach-enhanced")) {
            return Boolean(coach);
        }

        const title = coach.querySelector(":scope > h2");
        const recommendation = coach.querySelector(":scope > .dash-coach-copy");
        const guideCard = coach.querySelector(":scope > .dash-coach-guide-card");
        const brief = coach.querySelector(":scope > .dash-coach-brief-launch");
        const factors = coach.querySelector(":scope > .dash-coach-factors");
        const why = coach.querySelector(":scope > .dash-why");
        const guided = coach.querySelector(":scope > .guided-demo-callout");

        if (!title || !recommendation) {
            return false;
        }

        coach.classList.add("journey-coach-enhanced");

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

        const spotlight = document.createElement("section");
        spotlight.className = "journey-coach-spotlight";
        spotlight.setAttribute("aria-label", "Coach guidance");

        const bg = document.createElement("div");
        bg.className = "journey-coach-spotlight-bg";
        bg.setAttribute("aria-hidden", "true");
        bg.style.backgroundImage = `url("${String(featureAsset).replaceAll('"', "%22")}")`;

        const copy = document.createElement("div");
        copy.className = "journey-coach-spotlight-copy";
        copy.innerHTML = `
            <p class="journey-coach-kicker">COACH'S CALL</p>
            <h2>${title.innerHTML}</h2>
            <p class="journey-coach-recommendation">${recommendation.innerHTML}</p>
            <div class="journey-coach-motivation">EXECUTE THE NEXT WIN</div>
        `;

        const stage = document.createElement("div");
        stage.className = "journey-coach-portrait-stage";

        const img = document.createElement("img");
        img.className = "journey-coach-portrait";
        img.src = portrait;
        img.alt = "";
        img.loading = "lazy";

        const nameplate = document.createElement("div");
        nameplate.className = "journey-coach-nameplate";
        nameplate.innerHTML = `<small>12 WEEK RECOMP</small><strong>${coachName}</strong>`;

        stage.append(img, nameplate);
        spotlight.append(bg, copy, stage);

        const data = document.createElement("section");
        data.className = "journey-coach-data";
        data.setAttribute("aria-label", "Coach data and tools");

        const dataHead = document.createElement("div");
        dataHead.className = "journey-coach-data-head";
        dataHead.innerHTML = `
            <div>
                <small>COACH INTELLIGENCE</small>
                <strong>THE DATA BEHIND THE CALL</strong>
            </div>
            <span>Signals • guide • context</span>
        `;
        data.appendChild(dataHead);

        [factors, guideCard, brief, why, guided].forEach(node => {
            if (node) {
                data.appendChild(node);
            }
        });

        const firstExisting = coach.firstElementChild;
        if (firstExisting) {
            coach.insertBefore(spotlight, firstExisting);
        } else {
            coach.appendChild(spotlight);
        }
        coach.appendChild(data);

        return true;
    };

    const initialize = () => {
        let attempts = 0;
        const tryBuild = () => {
            attempts += 1;
            if (buildCoach() || attempts >= 12) {
                return;
            }
            window.setTimeout(tryBuild, 120);
        };
        tryBuild();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
