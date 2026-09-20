(function () {
    "use strict";

    const storageKey = "12wr.workout-plan.recent-workouts.v1";
    const maxRecent = 8;
    const selector =
        'select[name$=".WorkoutTypeCode"]';

    const weeklyStatus =
        document.getElementById("weekly-autosave-status");

    const planForm =
        weeklyStatus
            ? weeklyStatus.closest("form")
            : null;

    if (!planForm) {
        return;
    }

    function loadRecent() {
        try {
            const raw =
                window.localStorage.getItem(storageKey);

            if (!raw) {
                return [];
            }

            const parsed =
                JSON.parse(raw);

            return Array.isArray(parsed)
                ? parsed.filter(x => typeof x === "string" && x.length > 0)
                : [];
        }
        catch {
            return [];
        }
    }

    function saveRecent(codes) {
        try {
            window.localStorage.setItem(
                storageKey,
                JSON.stringify(codes.slice(0, maxRecent)));
        }
        catch {
            // Browser storage is a convenience only.
        }
    }

    function markRecent(code) {
        if (!code) {
            return;
        }

        const current =
            loadRecent()
                .filter(x => x !== code);

        current.unshift(code);

        saveRecent(current);
    }

    function ensureLegend(select, hasRecent) {
        let legend =
            select.nextElementSibling;

        if (
            !legend ||
            !legend.classList.contains("recent-workout-legend")
        ) {
            legend =
                document.createElement("span");

            legend.className =
                "recent-workout-legend";

            legend.innerHTML =
                '<span class="recent-workout-star">*</span> Recently selected';

            select.insertAdjacentElement(
                "afterend",
                legend);
        }

        legend.hidden =
            !hasRecent;
    }

    function baseText(option) {
        if (!option.dataset.workoutBaseText) {
            option.dataset.workoutBaseText =
                option.textContent.trim();
        }

        return option.dataset.workoutBaseText;
    }

    function moveRecentToFront(parent, options, recent) {
        const recentOptions =
            options
                .filter(option =>
                    option.value &&
                    recent.includes(option.value))
                .sort((a, b) =>
                    recent.indexOf(a.value) -
                    recent.indexOf(b.value));

        if (recentOptions.length === 0) {
            return;
        }

        const isSelect =
            parent instanceof HTMLSelectElement;

        const placeholder =
            isSelect
                ? Array.from(parent.children)
                    .find(node =>
                        node instanceof HTMLOptionElement &&
                        !node.value)
                : null;

        for (
            let index = recentOptions.length - 1;
            index >= 0;
            index--
        ) {
            const option =
                recentOptions[index];

            if (isSelect && placeholder) {
                parent.insertBefore(
                    option,
                    placeholder.nextSibling);
            }
            else {
                parent.insertBefore(
                    option,
                    parent.firstChild);
            }
        }
    }

    function renderSelect(select) {
        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        const recent =
            loadRecent();

        const options =
            Array.from(select.options);

        let hasRecent =
            false;

        for (const option of options) {
            const original =
                baseText(option);

            const isRecent =
                option.value &&
                recent.includes(option.value);

            option.textContent =
                isRecent
                    ? original + "  * Recently selected"
                    : original;

            option.classList.toggle(
                "recent-workout-option",
                Boolean(isRecent));

            if (isRecent) {
                hasRecent = true;
            }
        }

        const parents =
            Array.from(
                new Set(
                    options
                        .filter(x => x.value)
                        .map(x => x.parentElement)
                        .filter(Boolean)));

        for (const parent of parents) {
            const children =
                options.filter(
                    option =>
                        option.value &&
                        option.parentElement === parent);

            moveRecentToFront(
                parent,
                children,
                recent);
        }

        ensureLegend(
            select,
            hasRecent);
    }

    function bindSelect(select) {
        if (
            !(select instanceof HTMLSelectElement) ||
            select.dataset.recentWorkoutBound === "true"
        ) {
            return;
        }

        select.dataset.recentWorkoutBound =
            "true";

        renderSelect(select);

        select.addEventListener(
            "change",
            function () {
                if (select.value) {
                    markRecent(
                        select.value);

                    planForm
                        .querySelectorAll(selector)
                        .forEach(renderSelect);
                }
            });
    }

    function bindInside(root) {
        if (
            root instanceof HTMLSelectElement &&
            root.matches(selector)
        ) {
            bindSelect(root);
        }

        if (
            root instanceof Element ||
            root instanceof Document
        ) {
            root
                .querySelectorAll(selector)
                .forEach(bindSelect);
        }
    }

    const style =
        document.createElement("style");

    style.textContent = [
        ".recent-workout-option {",
        "  color: #b91c1c;",
        "  font-weight: 600;",
        "}",
        ".recent-workout-legend {",
        "  display: inline-block;",
        "  margin-left: .45rem;",
        "  font-size: .78rem;",
        "  color: #6b7280;",
        "  vertical-align: middle;",
        "}",
        ".recent-workout-star {",
        "  color: #dc2626;",
        "  font-weight: 800;",
        "  font-size: 1.05em;",
        "}",
        ".recent-workout-legend[hidden] {",
        "  display: none !important;",
        "}"
    ].join("\n");

    document.head.appendChild(style);

    function initialize() {
        bindInside(planForm);

        const observer =
            new MutationObserver(
                function (mutations) {
                    for (const mutation of mutations) {
                        for (const node of mutation.addedNodes) {
                            if (node instanceof Element) {
                                bindInside(node);
                            }
                        }
                    }
                });

        observer.observe(
            planForm,
            {
                childList: true,
                subtree: true
            });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize);
    }
    else {
        initialize();
    }
})();

// TEST nutrition picker bridge.
// Provider-backed frequent/past foods should behave like provider search
// results so the user can choose a real serving measure and then quantity.
(function () {
    "use strict";

    const originalFetch =
        window.fetch.bind(window);

    const normalizeMeasurePrompt = () => {
        const select =
            document.querySelector(
                "[data-picker-measure]");

        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        const placeholder =
            Array.from(select.options)
                .find(option => !option.value);

        if (
            placeholder
            && placeholder.textContent.trim()
                === "Choose a food first"
        ) {
            placeholder.textContent =
                "Select measurement";
        }
    };

    window.fetch =
        async (...args) => {
            const response =
                await originalFetch(...args);

            const request =
                args[0];

            const rawUrl =
                typeof request === "string"
                    ? request
                    : request instanceof URL
                        ? request.href
                        : request?.url;

            if (!rawUrl) {
                return response;
            }

            let url;

            try {
                url =
                    new URL(
                        rawUrl,
                        window.location.href);
            }
            catch {
                return response;
            }

            if (!url.pathname.endsWith(
                    "/Nutrition/FoodPickerData")) {
                return response;
            }

            let payload;

            try {
                payload =
                    await response.clone().json();
            }
            catch {
                return response;
            }

            if (!Array.isArray(
                    payload?.foods)) {
                return response;
            }

            let changed = false;

            payload.foods =
                payload.foods.map(
                    food => {
                        if (
                            food?.kind !== "pastFood"
                            || !food.providerCode
                            || !food.sourceFoodId
                        ) {
                            return food;
                        }

                        changed = true;

                        return {
                            ...food,
                            kind: "provider",
                            dataType:
                                food.usageCount
                                    ? `Used ${food.usageCount} time${food.usageCount === 1 ? "" : "s"}`
                                    : "Previously used"
                        };
                    });

            if (!changed) {
                return response;
            }

            const headers =
                new Headers(
                    response.headers);

            headers.set(
                "content-type",
                "application/json; charset=utf-8");

            return new Response(
                JSON.stringify(payload),
                {
                    status:
                        response.status,
                    statusText:
                        response.statusText,
                    headers
                });
        };

    const initializeNutritionMeasurePrompt = () => {
        normalizeMeasurePrompt();

        const select =
            document.querySelector(
                "[data-picker-measure]");

        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        const observer =
            new MutationObserver(
                normalizeMeasurePrompt);

        observer.observe(
            select,
            {
                childList: true,
                subtree: true,
                characterData: true
            });
    };

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeNutritionMeasurePrompt);
    }
    else {
        initializeNutritionMeasurePrompt();
    }
})();

// TEST nutrition picker presentation polish.
// Keep provider measure lists practical and replace the corrupted header glyphs.
(function () {
    "use strict";

    const normalize = value =>
        (value || "")
            .toString()
            .trim()
            .toLowerCase()
            .replace(/\s+/g, " ");

    const preferredUnitCodes =
        new Set([
            "oz",
            "fl oz",
            "cup",
            "cups",
            "serving",
            "servings",
            "unit",
            "units",
            "tsp",
            "teaspoon",
            "teaspoons",
            "tbsp",
            "tbs",
            "tablespoon",
            "tablespoons"
        ]);

    const preferredMeasurePattern =
        /\b(?:oz|ounce|ounces|fl oz|fluid ounce|fluid ounces|cup|cups|serving|servings|unit|units|tsp|teaspoon|teaspoons|tbsp|tbs|tablespoon|tablespoons)\b/i;

    const isPreferredMeasure = option => {
        const unitCode =
            normalize(option?.dataset?.unitCode);

        if (preferredUnitCodes.has(unitCode)) {
            return true;
        }

        const description =
            normalize(
                `${option?.textContent || ""} ${option?.value || ""}`);

        return preferredMeasurePattern.test(
            description);
    };

    const installFoodIcon = button => {
        if (!(button instanceof HTMLButtonElement)) {
            return;
        }

        button.textContent = "🍎";
        button.classList.add(
            "nutrition-picker-food-icon");
        button.setAttribute(
            "aria-label",
            "Close food picker");
        button.title = "Close";
    };

    const installFoodIconStyles = () => {
        if (document.getElementById(
                "nutrition-picker-food-icon-style")) {
            return;
        }

        const style =
            document.createElement("style");

        style.id =
            "nutrition-picker-food-icon-style";

        style.textContent = `
            .nutrition-picker-head-close.nutrition-picker-food-icon {
                width: 3rem !important;
                min-width: 3rem !important;
                height: 3rem !important;
                padding: 0 !important;
                display: inline-flex !important;
                align-items: center !important;
                justify-content: center !important;
                border-radius: 999px !important;
                border: 1px solid rgba(125, 211, 252, .55) !important;
                background: radial-gradient(circle at 35% 28%, rgba(56, 189, 248, .22), rgba(15, 23, 42, .92) 68%) !important;
                font-size: 1.7rem !important;
                line-height: 1 !important;
                overflow: hidden !important;
                box-shadow: 0 0 18px rgba(34, 211, 238, .12) !important;
            }

            .nutrition-picker-head-close.nutrition-picker-food-icon:hover,
            .nutrition-picker-head-close.nutrition-picker-food-icon:focus-visible {
                transform: translateY(-1px);
                border-color: rgba(74, 222, 128, .8) !important;
                box-shadow: 0 0 22px rgba(74, 222, 128, .2) !important;
            }
        `;

        document.head.appendChild(style);
    };

    const initializeNutritionPickerPolish = () => {
        if (!location.pathname
                .toLowerCase()
                .startsWith("/nutrition")) {
            return;
        }

        installFoodIconStyles();

        document
            .querySelectorAll(
                ".nutrition-picker-head-close")
            .forEach(installFoodIcon);

        const measureSelect =
            document.querySelector(
                "[data-picker-measure]");

        const amountInput =
            document.querySelector(
                "[data-picker-amount]");

        if (amountInput instanceof HTMLInputElement) {
            amountInput.step = "0.01";
        }

        if (!(measureSelect instanceof HTMLSelectElement)) {
            return;
        }

        let filtering = false;

        const simplifyMeasures = () => {
            if (filtering) {
                return;
            }

            const placeholder =
                Array.from(measureSelect.options)
                    .find(option => !option.value);

            if (
                placeholder
                && normalize(placeholder.textContent)
                    === "choose a food first"
            ) {
                placeholder.textContent =
                    "Select measurement";
            }

            const available =
                Array.from(measureSelect.options)
                    .filter(option => option.value);

            if (available.length < 2) {
                return;
            }

            const preferred =
                available.filter(
                    isPreferredMeasure);

            // If the provider gives none of the practical measures requested,
            // keep its original list so the food remains loggable.
            if (preferred.length === 0) {
                return;
            }

            const selectedValue =
                measureSelect.value;

            const selectedWillRemain =
                preferred.some(option =>
                    option.value === selectedValue);

            const toRemove =
                available.filter(option =>
                    !isPreferredMeasure(option));

            if (toRemove.length === 0) {
                return;
            }

            filtering = true;

            for (const option of toRemove) {
                option.remove();
            }

            if (!selectedWillRemain) {
                const firstPreferred =
                    Array.from(measureSelect.options)
                        .find(option => option.value);

                if (firstPreferred) {
                    measureSelect.value =
                        firstPreferred.value;

                    window.queueMicrotask(
                        () => measureSelect.dispatchEvent(
                            new Event(
                                "change",
                                {
                                    bubbles: true
                                })));
                }
            }

            filtering = false;
        };

        simplifyMeasures();

        const observer =
            new MutationObserver(
                simplifyMeasures);

        observer.observe(
            measureSelect,
            {
                childList: true
            });
    };

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeNutritionPickerPolish,
            { once: true });
    }
    else {
        initializeNutritionPickerPolish();
    }
})();
