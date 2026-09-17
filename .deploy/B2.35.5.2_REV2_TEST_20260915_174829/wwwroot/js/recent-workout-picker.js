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