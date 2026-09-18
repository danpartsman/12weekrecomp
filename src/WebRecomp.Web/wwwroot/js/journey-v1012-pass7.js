(() => {
    "use strict";

    const path = location.pathname.toLowerCase();
    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const FALLBACK_IMAGE = "/images/recomp/12wr-break-free-v1012.jpg";

    const escapeHtml = (value) => String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");

    const tidy = (node) => node?.textContent?.replace(/\s+/g, " ").trim() || "";

    const getDashboardPrescription = (dashboard) => {
        const plannedWorkout = tidy(
            dashboard.querySelector(".dash-workout .dash-pa-planned .dash-pa-title")
        ) || "Follow today's training plan";

        const plannedCalories = tidy(
            dashboard.querySelector(".dash-nutrition .dash-pa-planned .dash-pa-calories strong")
        ) || "Follow today's calorie target";

        const macroLegend = [...dashboard.querySelectorAll(
            ".dash-nutrition .dash-pa-planned .dash-pa-macro-legend > div"
        )]
            .map(tidy)
            .filter(Boolean)
            .slice(0, 3)
            .join(" · ");

        const fuelLine = macroLegend
            ? `${plannedCalories} · ${macroLegend}`
            : plannedCalories;

        return {
            plannedWorkout,
            fuelLine
        };
    };

    const buildCoachBand = () => {
        const dashboard = document.querySelector(".recomp-dashboard");
        const coach = dashboard?.querySelector(
            ".journey-mission-coach, .dash-primary-grid > .dash-coach"
        );

        if (!dashboard || !coach) return false;
        if (coach.classList.contains("journey-coach-pass7")) return true;

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

        const prescription = getDashboardPrescription(dashboard);

        const factorText = [...(factors?.querySelectorAll("span") ?? [])]
            .map(tidy)
            .filter(Boolean)
            .join(" · ") || "Follow today's recovery signals";

        const band = document.createElement("section");
        band.className = "journey-coach-band7";
        band.setAttribute("aria-label", "Coach prescription");

        const bg = document.createElement("div");
        bg.className = "journey-coach-band7-bg";
        bg.setAttribute("aria-hidden", "true");
        bg.style.backgroundImage = `url("${String(featureAsset).replaceAll('"', "%22")}")`;

        const trainer = document.createElement("div");
        trainer.className = "journey-coach-band7-trainer";
        trainer.setAttribute("aria-hidden", "true");

        const trainerImage = document.createElement("img");
        trainerImage.src = featureAsset;
        trainerImage.alt = "";
        trainerImage.loading = "eager";
        trainer.appendChild(trainerImage);

        const panel = document.createElement("div");
        panel.className = "journey-coach-band7-panel";

        const top = document.createElement("div");
        top.className = "journey-coach-band7-top";
        top.innerHTML = `
            <p class="journey-coach-band7-kicker">TODAY'S PRESCRIPTION</p>
            <h2 class="journey-coach-band7-title">${titleHtml}</h2>
        `;

        const copy = document.createElement("p");
        copy.className = "journey-coach-band7-copy";
        copy.innerHTML = recommendationHtml;

        const prescriptionDeck = document.createElement("div");
        prescriptionDeck.className = "journey-coach-band7-prescription";
        prescriptionDeck.innerHTML = `
            <div class="train">
                <small>TRAINING</small>
                <strong>${escapeHtml(prescription.plannedWorkout)}</strong>
            </div>
            <div class="fuel">
                <small>FUEL</small>
                <strong>${escapeHtml(prescription.fuelLine)}</strong>
            </div>
            <div class="ready">
                <small>READINESS</small>
                <strong>${escapeHtml(factorText)}</strong>
            </div>
        `;

        const bottom = document.createElement("div");
        bottom.className = "journey-coach-band7-bottom";

        const actions = document.createElement("div");
        actions.className = "journey-coach-band7-actions";

        if (guideCard) actions.appendChild(guideCard);
        if (brief) actions.appendChild(brief);

        const whyWrap = document.createElement("div");
        whyWrap.className = "journey-coach-band7-why";
        if (why) whyWrap.appendChild(why);
        if (guided) whyWrap.appendChild(guided);

        bottom.append(actions, whyWrap);
        panel.append(top, copy, prescriptionDeck, bottom);
        band.append(bg, trainer, panel);

        coach.classList.add("journey-coach-pass7");
        coach.replaceChildren(band);

        return true;
    };

    const initialize = () => {
        let attempts = 0;

        const tryBuild = () => {
            attempts += 1;
            if (buildCoachBand() || attempts >= 18) return;
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
