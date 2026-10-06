(() => {
    "use strict";

    const designSystemHref = "/css/design-system-v1012.css";

    if (!document.querySelector('link[data-12wr-design-system="v1012"]')) {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = designSystemHref;
        link.dataset["12wrDesignSystem"] = "v1012";
        document.head.appendChild(link);
    }

    const body = document.body;

    if (!body || !body.classList.contains("test-live-preview")) {
        return;
    }

    if (!document.getElementById("recomp-nav-overlay-layer-v1012")) {
        const style = document.createElement("style");
        style.id = "recomp-nav-overlay-layer-v1012";
        style.textContent = `
            body.test-live-preview .topbar,
            body.test-live-preview.app-shell .topbar {
                position: relative !important;
                z-index: 50000 !important;
                overflow: visible !important;
                isolation: isolate;
            }

            body.test-live-preview .app-nav.app-menu,
            body.test-live-preview.app-shell .app-nav.app-menu,
            body.test-live-preview .app-nav.app-menu.is-open,
            body.test-live-preview.app-shell .app-nav.app-menu.is-open {
                z-index: 50001 !important;
            }

            body.test-live-preview main.container {
                position: relative;
                z-index: 1;
            }
        `;
        document.head.appendChild(style);
    }

    if (!document.querySelector('link[data-12wr-journey="v1012"]')) {
        const journeyStyle = document.createElement("link");
        journeyStyle.rel = "stylesheet";
        journeyStyle.href = "/css/journey-v1012.css";
        journeyStyle.dataset["12wrJourney"] = "v1012";
        document.head.appendChild(journeyStyle);
    }

    if (!document.querySelector('script[data-12wr-journey="v1012"]')) {
        const journeyScript = document.createElement("script");
        journeyScript.src = "/js/journey-v1012.js?v=1015coachresponsive1";
        journeyScript.dataset["12wrJourney"] = "v1012";
        document.head.appendChild(journeyScript);
    }

    const normalize = (value) =>
        (value || "")
            .toString()
            .trim()
            .toLowerCase();

    const classifyWorkout = (value) => {
        const text = normalize(value);

        if (!text) {
            return "vib-neutral";
        }

        if (
            /rest|recover|recovery|mobility|stretch|easy|off day|yoga|walk/.test(text)
        ) {
            return "vib-recovery";
        }

        if (
            /accessory|hypertrophy|arms|arm |shoulder|delts|isolation|core/.test(text)
        ) {
            return "vib-accessory";
        }

        if (
            /cardio|conditioning|interval|hiit|orange|otf|run|row|bike|cycle|ride|z2|zone 2|aerobic/.test(text)
        ) {
            return "vib-conditioning";
        }

        if (
            /mixed|composite|athletic|circuit|cross|hybrid|metcon/.test(text)
        ) {
            return "vib-mixed";
        }

        if (
            /strength|major|heavy|push|pull|legs|leg |chest|back|squat|deadlift|press/.test(text)
        ) {
            return "vib-strength";
        }

        return "vib-neutral";
    };

    const workoutClasses = [
        "vib-strength",
        "vib-accessory",
        "vib-conditioning",
        "vib-recovery",
        "vib-mixed",
        "vib-neutral"
    ];

    const applyWorkoutAccent = (select) => {
        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        const selected =
            select.options[select.selectedIndex];

        const descriptor =
            `${select.value} ${selected?.textContent || ""}`;

        const category =
            classifyWorkout(descriptor);

        workoutClasses.forEach(
            className =>
                select.classList.remove(className)
        );

        select.classList.add(
            "vib-workout-select",
            category
        );

        let label =
            select.closest("label");

        if (!label) {
            label = select.parentElement;
        }

        if (label) {
            workoutClasses.forEach(
                className =>
                    label.classList.remove(className)
            );

            label.classList.add(
                "vib-workout-label",
                category
            );

            let dot =
                label.querySelector(
                    ":scope > .vib-workout-dot"
                );

            if (!dot) {
                dot =
                    document.createElement("span");

                dot.className =
                    "vib-workout-dot";

                dot.setAttribute(
                    "aria-hidden",
                    "true"
                );

                label.insertBefore(
                    dot,
                    select
                );
            }
        }

        const host =
            select.closest(
                ".plan-day, " +
                ".plan-day-card, " +
                ".schedule-day, " +
                ".day-card, " +
                ".weekly-day, " +
                ".plan-card, " +
                "article"
            );

        if (host) {
            workoutClasses.forEach(
                className =>
                    host.classList.remove(className)
            );

            host.classList.add(
                "vib-workout-host",
                category
            );
        }
    };

    const initializeWorkoutColors = () => {
        if (
            !location.pathname
                .toLowerCase()
                .startsWith("/schedule")
        ) {
            return;
        }

        const selects =
            Array.from(
                document.querySelectorAll("select")
            ).filter(select => {
                const identity =
                    `${select.name} ${select.id}`;

                const options =
                    Array.from(select.options)
                        .slice(0, 12)
                        .map(option =>
                            `${option.value} ${option.textContent || ""}`)
                        .join(" ");

                return (
                    /workout.*type|type.*workout/i.test(identity)
                    || /strength|cardio|conditioning|recovery|rest|accessory|push|pull|legs|z2|zone 2/i.test(options)
                );
            });

        selects.forEach(select => {
            applyWorkoutAccent(select);

            select.addEventListener(
                "change",
                () => applyWorkoutAccent(select)
            );
        });
    };

    const classifyDailyEntry = () => {
        if (
            !location.pathname
                .toLowerCase()
                .startsWith("/dailyentry")
        ) {
            return;
        }

        body.classList.add(
            "vib-daily-entry"
        );

        const classifyText =
            (element, text) => {
                const value =
                    normalize(text);

                if (
                    /body|recovery/.test(value)
                ) {
                    element.classList.add(
                        "vib-body-recovery"
                    );
                    return;
                }

                if (
                    /training|workout/.test(value)
                ) {
                    element.classList.add(
                        "vib-training"
                    );
                    return;
                }

                if (
                    /nutrition|food|macro/.test(value)
                ) {
                    element.classList.add(
                        "vib-nutrition"
                    );
                }
            };

        document
            .querySelectorAll(
                ".daily-entry-status, " +
                ".daily-entry-section"
            )
            .forEach(element => {
                const heading =
                    element.querySelector(
                        "h2, h3, strong"
                    );

                classifyText(
                    element,
                    heading?.textContent
                        || element.textContent
                );
            });
    };

    const enforceDashboardCoachCleanup = () => {
        if (!location.pathname.toLowerCase().startsWith("/dashboard")) {
            return;
        }

        const trainerImage =
            "/images/marketing-v12/coach-marisol-gym.png?v=dashboard-trainer-20260919";

        const setImportant = (element, property, value) => {
            if (!element) return;
            if (
                element.style.getPropertyValue(property) !== value
                || element.style.getPropertyPriority(property) !== "important"
            ) {
                element.style.setProperty(property, value, "important");
            }
        };

        const apply = () => {
            document
                .querySelectorAll(".recomp-dashboard .journey-momentum-banner")
                .forEach(element => element.remove());

            const coach =
                document.querySelector(".recomp-dashboard .journey-coach-p8");
            const image =
                coach?.querySelector(":scope > .journey-coach-p8__bg");

            if (!coach || !(image instanceof HTMLImageElement)) {
                return false;
            }

            /*
             * Pass 10 owns the coach artwork once the horizontal-swipe layout
             * is active. Do not paint the older responsive IMG over its pinned
             * card background.
             */
            if (coach.classList.contains("journey-coach-horizontal-swipe")) {
                setImportant(image, "opacity", "0");
                return true;
            }

            if (!image.src.includes("/images/marketing-v12/coach-marisol-gym.png")) {
                image.src = trainerImage;
                image.removeAttribute("srcset");
            }

            setImportant(coach, "background-image", "none");
            setImportant(image, "opacity", "1");
            setImportant(image, "display", "block");
            setImportant(image, "filter", "saturate(.96) contrast(1.07) brightness(.78)");
            setImportant(image, "object-fit", "cover");

            if (window.matchMedia("(max-width: 940px)").matches) {
                setImportant(image, "inset", "0 0 auto 0");
                setImportant(image, "width", "100%");
                setImportant(image, "height", "330px");
                setImportant(image, "object-position", "center 28%");
            }
            else {
                setImportant(image, "inset", "0 auto 0 0");
                setImportant(image, "width", "31%");
                setImportant(image, "height", "100%");
                setImportant(image, "object-position", "center center");
            }

            return true;
        };

        const delays = [0, 100, 250, 500, 1000, 1600, 2400, 3500];
        delays.forEach(delay => window.setTimeout(apply, delay));

        const dashboard = document.querySelector(".recomp-dashboard");
        if (dashboard) {
            const observer = new MutationObserver(() => {
                window.requestAnimationFrame(apply);
            });

            observer.observe(dashboard, {
                childList: true,
                subtree: true
            });

            window.setTimeout(() => observer.disconnect(), 6000);
        }

        window.addEventListener(
            "resize",
            () => window.setTimeout(apply, 40),
            { passive: true }
        );
    };

    const initialize = () => {
        initializeWorkoutColors();
        classifyDailyEntry();
        enforceDashboardCoachCleanup();
    };

    if (
        document.readyState === "loading"
    ) {
        document.addEventListener(
            "DOMContentLoaded",
            initialize,
            { once: true }
        );
    }
    else {
        initialize();
    }
})();

// TEST nutrition picker cleanup: dropdown-only amounts, no Change button,
// whole-number kcal display, selected-food emphasis, and exact 1-unit spinner steps.
(() => {
    "use strict";

    const initializeNutritionPickerCleanup = () => {
        if (!location.pathname.toLowerCase().startsWith("/nutrition")) {
            return;
        }

        if (!document.getElementById("nutrition-picker-cleanup-v1012")) {
            const style = document.createElement("style");
            style.id = "nutrition-picker-cleanup-v1012";
            style.textContent = `
                #nutrition-food-picker-dialog .nutrition-picker-quick-amounts,
                #nutrition-food-picker-dialog [data-picker-amount-presets] {
                    display: none !important;
                }

                #nutrition-food-picker-dialog [data-picker-change] {
                    display: none !important;
                }

                #nutrition-food-picker-dialog .nutrition-picker-selected-head {
                    grid-template-columns: minmax(0, 1fr) !important;
                }

                #nutrition-food-picker-dialog .nutrition-picker-selection {
                    display: grid !important;
                    gap: .22rem !important;
                    padding: .72rem .9rem !important;
                    margin: 0 0 .35rem 0 !important;
                    border: 1px solid rgba(34, 211, 238, .52) !important;
                    border-radius: .78rem !important;
                    background:
                        linear-gradient(
                            135deg,
                            rgba(8, 145, 178, .24),
                            rgba(15, 23, 42, .72)
                        ) !important;
                    box-shadow:
                        inset 3px 0 0 rgba(74, 222, 128, .85),
                        0 8px 24px rgba(2, 132, 199, .10) !important;
                }

                #nutrition-food-picker-dialog .nutrition-picker-selection > strong {
                    display: block !important;
                    color: #f8fafc !important;
                    font-size: calc(1rem + 1.3px) !important;
                    line-height: 1.2 !important;
                    font-weight: 800 !important;
                }

                #nutrition-food-picker-dialog .nutrition-picker-selection > .small,
                #nutrition-food-picker-dialog .nutrition-picker-selection > span {
                    color: #b8c7da !important;
                }
            `;
            document.head.appendChild(style);
        }

        const amountInput = document.querySelector("[data-picker-amount]");

        if (amountInput instanceof HTMLInputElement) {
            const enforceWholeStep = () => {
                if (amountInput.step !== "1") {
                    amountInput.step = "1";
                }
                amountInput.inputMode = "decimal";
            };

            // An older picker script used to restore step=.01 at DOMContentLoaded.
            // Enforce the final rule after all listeners run and whenever the
            // attribute is changed again.
            enforceWholeStep();
            window.setTimeout(enforceWholeStep, 0);
            window.setTimeout(enforceWholeStep, 60);

            const stepObserver = new MutationObserver(enforceWholeStep);
            stepObserver.observe(amountInput, {
                attributes: true,
                attributeFilter: ["step"]
            });

            if (amountInput.dataset.wholeStepBound !== "true") {
                amountInput.dataset.wholeStepBound = "true";

                const normalizeTypedAmount = () => {
                    const value = Number.parseFloat(amountInput.value);

                    if (!Number.isFinite(value)) {
                        return;
                    }

                    const minimum = Number.parseFloat(amountInput.min || "0.01");
                    const maximum = Number.parseFloat(amountInput.max || "1000");
                    const clamped = Math.min(
                        Number.isFinite(maximum) ? maximum : value,
                        Math.max(
                            Number.isFinite(minimum) ? minimum : value,
                            value));
                    const rounded = Math.round(clamped * 100) / 100;

                    amountInput.value = rounded.toString();
                    amountInput.dispatchEvent(
                        new Event("input", { bubbles: true }));
                };

                const changeAmountByOne = direction => {
                    const current = Number.parseFloat(amountInput.value);
                    const base = Number.isFinite(current) ? current : 0;
                    const minimum = Number.parseFloat(amountInput.min || "0.01");
                    const maximum = Number.parseFloat(amountInput.max || "1000");
                    const requested = base + direction;
                    const next = Math.min(
                        Number.isFinite(maximum) ? maximum : requested,
                        Math.max(
                            Number.isFinite(minimum) ? minimum : requested,
                            requested));

                    amountInput.value =
                        (Math.round(next * 100) / 100).toString();
                    amountInput.dispatchEvent(
                        new Event("input", { bubbles: true }));
                };

                amountInput.addEventListener("blur", normalizeTypedAmount);

                amountInput.addEventListener("keydown", event => {
                    if (event.key !== "ArrowUp" && event.key !== "ArrowDown") {
                        return;
                    }

                    event.preventDefault();
                    changeAmountByOne(event.key === "ArrowUp" ? 1 : -1);
                });

                // Chrome's native spin controls can keep an old fractional step
                // until the element is repainted. Intercept clicks in the spinner
                // zone so each visible arrow always changes the value by exactly 1.
                amountInput.addEventListener("mousedown", event => {
                    const rect = amountInput.getBoundingClientRect();
                    const spinnerWidth = Math.min(34, rect.width * .28);

                    if (event.clientX < rect.right - spinnerWidth) {
                        return;
                    }

                    event.preventDefault();
                    amountInput.focus();

                    const direction =
                        event.clientY < rect.top + (rect.height / 2)
                            ? 1
                            : -1;

                    changeAmountByOne(direction);
                });
            }
        }

        const roundKcalText = root => {
            if (!root) {
                return;
            }

            const walker = document.createTreeWalker(
                root,
                NodeFilter.SHOW_TEXT);
            const nodes = [];

            while (walker.nextNode()) {
                nodes.push(walker.currentNode);
            }

            for (const node of nodes) {
                const before = node.nodeValue || "";
                const after = before.replace(
                    /(-?\d+(?:\.\d+)?)\s*kcal\b/gi,
                    (match, numeric) => {
                        const value = Number.parseFloat(numeric);
                        return Number.isFinite(value)
                            ? `${Math.round(value)} kcal`
                            : match;
                    });

                if (after !== before) {
                    node.nodeValue = after;
                }
            }
        };

        const dialogs = [
            document.getElementById("nutrition-food-picker-dialog"),
            document.getElementById("nutrition-custom-food-dialog")
        ].filter(Boolean);

        for (const dialog of dialogs) {
            roundKcalText(dialog);

            const observer = new MutationObserver(() => {
                roundKcalText(dialog);
            });

            observer.observe(dialog, {
                childList: true,
                subtree: true,
                characterData: true
            });
        }
    };

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeNutritionPickerCleanup,
            { once: true });
    }
    else {
        initializeNutritionPickerCleanup();
    }
})();