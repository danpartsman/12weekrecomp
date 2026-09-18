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
        journeyScript.src = "/js/journey-v1012.js";
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

    const enforceApprovedMarisol = () => {
        if (!location.pathname.toLowerCase().startsWith("/dashboard")) {
            return;
        }

        let attempts = 0;

        const applyImage = () => {
            attempts += 1;

            const image =
                document.querySelector(
                    ".recomp-dashboard .journey-coach-p8__bg"
                );

            if (image instanceof HTMLImageElement) {
                image.src =
                    "/images/coach/marisol-card-background-v1012.webp?v=marisol-approved-20260918c";
                image.removeAttribute("srcset");
                image.style.setProperty("object-position", "left center", "important");
                return;
            }

            if (attempts < 40) {
                window.setTimeout(applyImage, 100);
            }
        };

        applyImage();
    };

    const initialize = () => {
        initializeWorkoutColors();
        classifyDailyEntry();
        enforceApprovedMarisol();
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
