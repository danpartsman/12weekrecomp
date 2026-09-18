(() => {
    "use strict";

    const body = document.body;
    const path = location.pathname.toLowerCase();

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const clean = (node) =>
        node?.textContent?.replace(/\s+/g, " ").trim() || "";

    const htmlEscape = (value) =>
        String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");

    const accentLastWord = (html) => {
        const holder = document.createElement("div");
        holder.innerHTML = html;
        const text = holder.textContent?.replace(/\s+/g, " ").trim() || "TODAY'S PLAN";
        const words = text.split(" ");
        const last = words.pop() || "";
        return `${htmlEscape(words.join(" "))} <span class="journey-coach-p8__accent">${htmlEscape(last)}</span>`;
    };

    const readPlannedMacro = (nutritionPanel, index) =>
        clean(nutritionPanel?.querySelectorAll(".dash-pa-planned .dash-pa-macro-legend > div")[index]?.querySelector("strong"));

    const build = () => {
        const dashboard = document.querySelector(".recomp-dashboard");
        const coach = dashboard?.querySelector(
            ".journey-mission-coach, .dash-primary-grid > .dash-coach"
        );

        if (!dashboard || !coach) return false;
        if (coach.classList.contains("journey-coach-pass8")) return true;

        const title = coach.querySelector(":scope > h2");
        const recommendation = coach.querySelector(":scope > .dash-coach-copy");
        const brief = coach.querySelector(":scope > .dash-coach-brief-launch");
        const factors = coach.querySelector(":scope > .dash-coach-factors");
        const why = coach.querySelector(":scope > .dash-why");
        const guided = coach.querySelector(":scope > .guided-demo-callout");

        const training = dashboard.querySelector(".dash-workout.dash-planactual-panel");
        const nutrition = dashboard.querySelector(".dash-nutrition.dash-planactual-panel");

        if (!title || !recommendation || !training || !nutrition) return false;

        const workoutName =
            clean(training.querySelector(".dash-pa-planned .dash-pa-title"))
            || "Follow today's planned workout";

        const workoutMeta =
            clean(training.querySelector(".dash-pa-planned .dash-pa-meta"))
            || "Use today's prescribed effort";

        const burnItems = training.querySelectorAll(
            ".dash-pa-planned .dash-workout-burn-grid > div"
        );

        const burnValue = clean(burnItems[0]?.querySelector("strong")) || "—";
        const burnLabel = clean(burnItems[0]?.querySelector("span")) || "Estimated workout burn";
        const glycogenValue = clean(burnItems[1]?.querySelector("strong")) || "—";
        const glycogenLabel = clean(burnItems[1]?.querySelector("span")) || "Estimated glycogen demand";

        const calorieTarget =
            clean(nutrition.querySelector(".dash-pa-planned .dash-pa-calories strong"))
            || "—";

        const protein = readPlannedMacro(nutrition, 0) || "—";
        const carbs = readPlannedMacro(nutrition, 1) || "—";
        const fat = readPlannedMacro(nutrition, 2) || "—";

        const nutritionLabel =
            clean(nutrition.querySelector(".dash-pa-planned .dash-pa-nutrition-foot strong"))
            || "Today's nutrition prescription";

        const nutritionTip =
            clean(nutrition.querySelector(".dash-pa-planned .dash-pa-nutrition-foot span"))
            || "Hit today's calorie and macro targets.";

        const factorText =
            [...(factors?.querySelectorAll("span") ?? [])]
                .map(clean)
                .filter(Boolean)
                .join(" · ")
            || "Use today's recovery and fuel signals.";

        const coachWhy = clean(why?.querySelector("p"));

        const section = document.createElement("section");
        section.className = "journey-coach-p8";
        section.setAttribute("aria-label", "Today's coach prescription");

        const bg = document.createElement("img");
        bg.className = "journey-coach-p8__bg";
        bg.src = "/images/coach/marisol-coach-band-v1012.webp";
        bg.alt = "";
        bg.loading = "eager";
        bg.fetchPriority = "high";
        bg.setAttribute("aria-hidden", "true");

        const panel = document.createElement("div");
        panel.className = "journey-coach-p8__panel";

        const header = document.createElement("header");
        header.className = "journey-coach-p8__header";
        header.innerHTML = `
            <p class="journey-coach-p8__eyebrow">TODAY'S PRESCRIPTION</p>
            <h2 class="journey-coach-p8__title">${accentLastWord(title.innerHTML)}</h2>
        `;

        const lead = document.createElement("p");
        lead.className = "journey-coach-p8__lead";
        lead.innerHTML = recommendation.innerHTML;

        const grid = document.createElement("div");
        grid.className = "journey-coach-p8__grid";
        grid.innerHTML = `
            <article class="journey-coach-p8__metric">
                <div class="journey-coach-p8__icon" aria-hidden="true">↗</div>
                <div>
                    <small>TRAINING</small>
                    <strong>${htmlEscape(workoutName)}</strong>
                    <span>${htmlEscape(workoutMeta)}</span>
                </div>
            </article>

            <article class="journey-coach-p8__metric journey-coach-p8__metric--yellow">
                <div class="journey-coach-p8__icon" aria-hidden="true">◎</div>
                <div>
                    <small>DURATION / EFFORT</small>
                    <strong>${htmlEscape(workoutMeta)}</strong>
                    <span>Stay inside the prescribed effort; quality beats overshooting.</span>
                </div>
            </article>

            <article class="journey-coach-p8__metric">
                <div class="journey-coach-p8__icon" aria-hidden="true">🔥</div>
                <div>
                    <small>CALORIE BURN</small>
                    <strong>${htmlEscape(burnValue)}</strong>
                    <span>${htmlEscape(burnLabel)}</span>
                </div>
            </article>

            <article class="journey-coach-p8__metric">
                <div class="journey-coach-p8__icon" aria-hidden="true">ϟ</div>
                <div>
                    <small>GLYCOGEN / FUEL</small>
                    <strong>${htmlEscape(glycogenValue)}</strong>
                    <span>${htmlEscape(glycogenLabel)} · ${htmlEscape(factorText)}</span>
                </div>
            </article>

            <article class="journey-coach-p8__metric journey-coach-p8__metric--nutrition journey-coach-p8__metric--yellow">
                <div class="journey-coach-p8__icon" aria-hidden="true">⌁</div>
                <div>
                    <small>NUTRITION TARGET</small>
                    <strong>${htmlEscape(calorieTarget)} calories · ${htmlEscape(nutritionLabel)}</strong>
                    <span class="journey-coach-p8__macroline">
                        <b class="p">Protein ${htmlEscape(protein)}</b>
                        <b class="c">Carbs ${htmlEscape(carbs)}</b>
                        <b class="f">Fat ${htmlEscape(fat)}</b>
                    </span>
                </div>
            </article>

            <article class="journey-coach-p8__metric journey-coach-p8__metric--nutrition">
                <div class="journey-coach-p8__icon" aria-hidden="true">✓</div>
                <div>
                    <small>NUTRITION GUIDANCE</small>
                    <strong>${htmlEscape(nutritionLabel)}</strong>
                    <span>${htmlEscape(nutritionTip)}</span>
                </div>
            </article>
        `;

        const focus = document.createElement("div");
        focus.className = "journey-coach-p8__focus";
        focus.innerHTML = `
            <div class="journey-coach-p8__icon" aria-hidden="true">✓</div>
            <div>
                <small>COACHING FOCUS</small>
                <strong>${htmlEscape(coachWhy || factorText)}</strong>
            </div>
        `;

        const footer = document.createElement("footer");
        footer.className = "journey-coach-p8__footer";

        const mantra = document.createElement("div");
        mantra.className = "journey-coach-p8__mantra";
        mantra.innerHTML = 'EXECUTE <span>THE NEXT WIN</span>';

        const actions = document.createElement("div");
        actions.className = "journey-coach-p8__actions";

        if (brief) actions.appendChild(brief);

        footer.append(mantra, actions);
        panel.append(header, lead, grid, focus, footer);
        section.append(bg, panel);

        coach.classList.add("journey-coach-pass8");
        coach.replaceChildren(section);

        /* Preserve Guided Demo content without letting it fight the visual composition. */
        if (guided) guided.hidden = true;

        return true;
    };

    const relocateMomentum = () => {
        const dashboard = document.querySelector(".recomp-dashboard");
        const momentum = dashboard?.querySelector(".journey-momentum-banner");
        const tomorrow =
            dashboard?.querySelector(".journey-tomorrow-section")
            || dashboard?.querySelector(".dash-tomorrow-repeat-section");

        if (!dashboard || !momentum || !tomorrow) return false;

        if (tomorrow.nextElementSibling !== momentum) {
            tomorrow.insertAdjacentElement("afterend", momentum);
        }

        momentum.classList.add("journey-momentum-relocated");
        return true;
    };

    const initialize = () => {
        let attempts = 0;

        const tryBuild = () => {
            attempts += 1;
            if (build() || attempts >= 20) return;
            window.setTimeout(tryBuild, 100);
        };

        let momentumAttempts = 0;
        const tryRelocateMomentum = () => {
            momentumAttempts += 1;
            if (relocateMomentum() || momentumAttempts >= 24) return;
            window.setTimeout(tryRelocateMomentum, 100);
        };

        tryBuild();
        tryRelocateMomentum();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once:true });
    } else {
        initialize();
    }
})();
