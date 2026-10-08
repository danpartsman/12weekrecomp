(() => {
    const log = (card, interactionCode) => {
        const form = card?.querySelector("[data-learning-log-form]");
        const field = form?.querySelector("[data-learning-interaction-code]");
        if (!form || !field) return;

        field.value = interactionCode;

        fetch(form.action, {
            method: "POST",
            body: new FormData(form),
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        }).catch(() => {});
    };

    const openDialog = id => {
        const dialog = document.getElementById(id);
        if (!dialog) return;
        dialog.hidden = false;
        document.documentElement.style.overflow = "hidden";
        dialog.querySelector(".learning-dialog-close")?.focus();
    };

    const closeDialog = dialog => {
        if (!dialog) return;
        dialog.hidden = true;
        document.documentElement.style.overflow = "";
    };

    document.addEventListener("click", event => {
        const opener = event.target.closest("[data-learning-open]");
        if (opener) {
            const card = opener.closest("[data-learning-coach]");
            log(card, "OPEN");
            openDialog(opener.dataset.learningOpen);
            return;
        }

        const closer = event.target.closest("[data-learning-close]");
        if (closer) {
            closeDialog(closer.closest(".learning-dialog"));
            return;
        }

        const hide = event.target.closest("[data-learning-hide]");
        if (hide) {
            const card = hide.closest("[data-learning-coach]");
            if (card) {
                log(card, "DISMISS");
                card.classList.add("is-hidden");
                try {
                    sessionStorage.setItem(
                        "12wr-learning-hidden-" + (card.dataset.contentCode || ""),
                        "1");
                } catch (_) {}
            }
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        const open = [...document.querySelectorAll(".learning-dialog:not([hidden])")].pop();
        closeDialog(open);
    });

    document.querySelectorAll("[data-learning-coach]").forEach(card => {
        try {
            if (sessionStorage.getItem(
                "12wr-learning-hidden-" + (card.dataset.contentCode || "")) === "1") {
                card.classList.add("is-hidden");
            }
        } catch (_) {}
    });
})();