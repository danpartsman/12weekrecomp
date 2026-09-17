(function () {
    "use strict";

    var modal =
        document.getElementById("coachTodayDetail");

    if (!modal) {
        return;
    }

    var shell =
        modal.querySelector(".coach-today-detail__shell");

    var image =
        document.getElementById("coachWorkoutGuideImage");

    var expandButton =
        modal.querySelector("[data-coach-guide-expand]");

    var openButtons =
        document.querySelectorAll("[data-coach-guide-open]");

    var closeButtons =
        modal.querySelectorAll("[data-coach-guide-close]");

    var lastFocus =
        null;

    function setExpanded(expanded) {
        shell.classList.toggle(
            "is-expanded",
            expanded);

        expandButton.setAttribute(
            "aria-label",
            expanded
                ? "Restore coaching guide"
                : "Expand coaching guide");

        expandButton.setAttribute(
            "title",
            expanded
                ? "Restore"
                : "Expand");
    }

    function openGuide(event) {
        var button =
            event.currentTarget;

        var asset =
            button.getAttribute(
                "data-coach-guide-asset");
        var layout =
            button.getAttribute(
                "data-coach-guide-layout") || "left";

        shell.setAttribute(
            "data-coach-guide-layout",
            layout);

        if (!asset) {
            return;
        }

        lastFocus =
            button;

        image.src =
            asset;

        image.alt =
            button.getAttribute(
                "data-coach-guide-alt") || "";

        modal.querySelector(
            ".coach-today-detail__shell")
            .setAttribute(
                "aria-label",
                button.getAttribute(
                    "data-coach-guide-title")
                || "Workout coaching guide");

        modal.hidden =
            false;

        modal.setAttribute(
            "aria-hidden",
            "false");

        document.body.classList.add(
            "coach-today-detail-open");

        setExpanded(
            false);

        expandButton.focus();
    }

    function closeGuide() {
        modal.hidden =
            true;

        modal.setAttribute(
            "aria-hidden",
            "true");

        document.body.classList.remove(
            "coach-today-detail-open");

        setExpanded(
            false);

        if (lastFocus) {
            lastFocus.focus();
        }
    }

    openButtons.forEach(
        function (button) {
            button.addEventListener(
                "click",
                openGuide);
        });

    closeButtons.forEach(
        function (button) {
            button.addEventListener(
                "click",
                closeGuide);
        });

    expandButton.addEventListener(
        "click",
        function () {
            setExpanded(
                !shell.classList.contains(
                    "is-expanded"));
        });

    document.addEventListener(
        "keydown",
        function (event) {
            if (
                event.key === "Escape"
                && !modal.hidden
            ) {
                closeGuide();
            }
        });
})();

