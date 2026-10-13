(() => {
    const page = document.querySelector(
        ".workout-intelligence-page.wi-live-page-minimal");

    if (!page) {
        return;
    }

    const root =
        document.getElementById("wi-exercise-list");

    const form =
        document.getElementById("wi-detailed-workout-form");

    if (!root || !form) {
        return;
    }

    let scheduled = false;

    function exerciseCards() {
        return [
            ...root.querySelectorAll(
                "[data-exercise-card]")
        ];
    }

    function addNotesField(card) {
        let notes =
            card.querySelector(
                '[data-field="Notes"]');

        if (notes) {
            return;
        }

        const existingNotes =
            card.querySelector(
                ".wi-exercise-instructions");

        const existingText =
            existingNotes?.textContent?.trim()
            || "";

        const label =
            document.createElement("label");

        label.className =
            "wi-notes-field";

        const caption =
            document.createElement("span");

        caption.textContent =
            "Notes";

        notes =
            document.createElement("textarea");

        notes.rows = 1;
        notes.wrap = "off";
        notes.dataset.field = "Notes";
        notes.value = existingText;
        notes.placeholder =
            "Optional notes, setup, cues, or how the set felt";
        notes.autocomplete = "off";
        notes.setAttribute(
            "aria-label",
            "Exercise notes");

        label.appendChild(caption);
        label.appendChild(notes);

        const weight =
            card.querySelector(
                ".wi-heaviest-field");

        if (weight) {
            weight.insertAdjacentElement(
                "afterend",
                label);
        }
        else {
            const actions =
                card.querySelector(
                    ".wi-exercise-action-stack");

            if (actions) {
                card.insertBefore(
                    label,
                    actions);
            }
            else {
                card.appendChild(label);
            }
        }

        if (existingNotes) {
            existingNotes.classList.add(
                "wi-note-source-hidden");
            existingNotes.setAttribute(
                "aria-hidden",
                "true");
        }
    }

    function renumberNotes() {
        exerciseCards().forEach(
            (card, index) => {
                const notes =
                    card.querySelector(
                        '[data-field="Notes"]');

                if (notes) {
                    notes.name =
                        `Exercises[${index}].Notes`;
                }
            });
    }

    function enhance() {
        scheduled = false;

        exerciseCards().forEach(
            card => addNotesField(card));

        renumberNotes();
    }

    function scheduleEnhance() {
        if (scheduled) {
            return;
        }

        scheduled = true;
        queueMicrotask(enhance);
    }

    enhance();

    const observer =
        new MutationObserver(
            scheduleEnhance);

    observer.observe(
        root,
        {
            childList: true,
            subtree: true
        });

    form.addEventListener(
        "submit",
        renumberNotes,
        true);
})();
