(() => {
    "use strict";

    const body = document.body;
    const path = location.pathname.toLowerCase();

    if (!body || !body.classList.contains("test-live-preview") || !path.startsWith("/dashboard")) {
        return;
    }

    const clean = (node) => node?.textContent?.replace(/\s+/g, " ").trim() || "";
    const num = (text) => {
        const m = String(text || "").replace(/,/g, "").match(/\d+(?:\.\d+)?/);
        return m ? Number(m[0]) : 0;
    };
    const round5 = (value) => Math.max(0, Math.round(value / 5) * 5);
    const clamp = (value, lo, hi) => Math.min(hi, Math.max(lo, value));

    const setMetric = (metric, label, strongText, detail, iconText, extraClass) => {
        if (!metric) return;
        metric.classList.remove("journey-coach-p10-pre", "journey-coach-p10-post");
        if (extraClass) metric.classList.add(extraClass);

        const icon = metric.querySelector(".journey-coach-p8__icon");
        const small = metric.querySelector("small");
        const strong = metric.querySelector("strong");
        const span = metric.querySelector("span");

        if (icon) icon.textContent = iconText;
        if (small) small.textContent = label;
        if (strong) strong.textContent = strongText;
        if (span) span.textContent = detail;
    };

    const apply = () => {
        const coach = document.querySelector(".recomp-dashboard .journey-coach-p8");
        if (!coach) return false;
        if (coach.classList.contains("journey-coach-pass10")) return true;

        const bg = coach.querySelector(":scope > .journey-coach-p8__bg");
        const panel = coach.querySelector(":scope > .journey-coach-p8__panel");
        const message = coach.querySelector(":scope > .journey-coach-p9__message");
        const header = message?.querySelector(":scope > .journey-coach-p8__header") || panel?.querySelector(":scope > .journey-coach-p8__header");
        const lead = message?.querySelector(":scope > .journey-coach-p8__lead") || panel?.querySelector(":scope > .journey-coach-p8__lead");
        const grid = panel?.querySelector(":scope > .journey-coach-p8__grid");

        if (!bg || !panel || !header || !lead || !grid) return false;

        if (bg.tagName === "IMG") {
            bg.src = "/images/coach/marisol-card-background-v1012.webp";
            bg.removeAttribute("srcset");
            bg.style.setProperty("inset", "0", "important");
            bg.style.setProperty("width", "100%", "important");
            bg.style.setProperty("height", "100%", "important");
            bg.style.setProperty("object-fit", "cover", "important");
            bg.style.setProperty("object-position", "left center", "important");
            bg.style.setProperty("filter", "none", "important");
        }

        panel.style.setProperty("left", "31%", "important");

        if (message) message.remove();

        if (panel.firstElementChild !== header) {
            panel.insertBefore(header, panel.firstElementChild);
        }
        if (header.nextElementSibling !== lead) {
            header.insertAdjacentElement("afterend", lead);
        }

        const metrics = [...grid.querySelectorAll(":scope > .journey-coach-p8__metric")];

        if (metrics.length >= 6) {
            const nutritionMetric = metrics[4];
            const guidanceMetric = metrics[5];

            const calorieTarget = clean(nutritionMetric.querySelector("strong"));
            const macroItems = [...nutritionMetric.querySelectorAll(".journey-coach-p8__macroline b")];
            const protein = num(macroItems[0]?.textContent);
            const carbs = num(macroItems[1]?.textContent);
            const fat = num(macroItems[2]?.textContent);
            const dayType = clean(guidanceMetric.querySelector("strong"));
            const originalGuidance = clean(guidanceMetric.querySelector("span"));

            const preCarbMax = clamp(round5(carbs * .20), 35, 65);
            const preCarbMin = clamp(round5(carbs * .14), 25, Math.max(25, preCarbMax - 10));
            const postCarbMax = clamp(round5(carbs * .28), 45, 90);
            const postCarbMin = clamp(round5(carbs * .20), 35, Math.max(35, postCarbMax - 10));

            const preProteinMin = clamp(round5(protein * .14), 20, 30);
            const preProteinMax = clamp(preProteinMin + 10, 30, 40);
            const postProteinMin = clamp(round5(protein * .16), 25, 35);
            const postProteinMax = clamp(postProteinMin + 10, 35, 45);

            const dailySummary = [
                calorieTarget,
                protein ? `P ${protein} g` : "",
                carbs ? `C ${carbs} g` : "",
                fat ? `F ${fat} g` : ""
            ].filter(Boolean).join(" · ");

            setMetric(
                nutritionMetric,
                "PRE-WORKOUT FUEL",
                `${preCarbMin}–${preCarbMax} g carbs + ${preProteinMin}–${preProteinMax} g protein`,
                `60–90 min before · ${dayType || "Use today's nutrition plan"} · ${dailySummary}`,
                "⌁",
                "journey-coach-p10-pre"
            );

            setMetric(
                guidanceMetric,
                "POST-WORKOUT",
                `${postProteinMin}–${postProteinMax} g protein + ${postCarbMin}–${postCarbMax} g carbs`,
                originalGuidance || "Recover with protein, carbohydrate, fluids, and the next planned meal.",
                "▣",
                "journey-coach-p10-post"
            );
        }

        const leftTag = document.createElement("div");
        leftTag.className = "journey-coach-p10__left-tag";
        leftTag.innerHTML = "STRONGER<br>THAN<br>YESTERDAY";
        coach.appendChild(leftTag);

        coach.classList.add("journey-coach-pass10");
        return true;
    };

    const initialize = () => {
        let attempts = 0;
        const tryApply = () => {
            attempts += 1;
            if (apply() || attempts >= 30) return;
            window.setTimeout(tryApply, 100);
        };
        tryApply();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once:true });
    } else {
        initialize();
    }
})();
