(() => {
    const root =
        document.getElementById("wi-exercise-list");

    const form =
        document.getElementById("wi-detailed-workout-form");

    if (!root || !form) {
        return;
    }

    const defaultUnit =
        (root.dataset.defaultUnit || "LB").toUpperCase() === "KG"
            ? "KG"
            : "LB";

    const emptyState =
        document.getElementById("wi-empty-workout");

    const searchRoot =
        document.getElementById("wi-exercise-search");

    const searchInput =
        document.getElementById("wi-search-input");

    const searchResults =
        document.getElementById("wi-search-results");

    const searchStatus =
        document.getElementById("wi-search-status");

    const techniqueUrl =
        "/DetailedWorkout/Technique";

    const antiForgery =
        form.querySelector(
            'input[name="__RequestVerificationToken"]'
        );

    let searchTimer = null;

    function makeLabel(text, control) {
        const wrapper =
            document.createElement("label");

        wrapper.append(
            document.createTextNode(text)
        );

        wrapper.appendChild(control);
        return wrapper;
    }

    function makeNumberInput(
        field,
        value,
        min,
        max,
        step = null,
        placeholder = null) {

        const input =
            document.createElement("input");

        input.type = "number";
        input.dataset.field = field;
        input.inputMode =
            field === "MaxWeight"
                ? "decimal"
                : "numeric";
        input.autocomplete = "off";
        input.min = String(min);
        input.max = String(max);

        if (step !== null) {
            input.step = String(step);
        }

        if (value !== null
            && value !== undefined
            && value !== "") {
            input.value = String(value);
        }

        if (placeholder) {
            input.placeholder = placeholder;
        }

        return input;
    }

    function ensureTechniqueModal() {
        let modal =
            document.getElementById(
                "wi-technique-modal");

        if (modal) {
            return modal;
        }

        modal =
            document.createElement("div");

        modal.id =
            "wi-technique-modal";
        modal.className =
            "wi-technique-modal";
        modal.hidden = true;

        modal.innerHTML = `
            <div class="wi-technique-backdrop"
                 data-technique-close></div>
            <section class="wi-technique-dialog"
                     role="dialog"
                     aria-modal="true"
                     aria-labelledby="wi-technique-modal-title">
                <button type="button"
                        class="wi-technique-close"
                        data-technique-close
                        aria-label="Close technique">&times;</button>
                <h2 id="wi-technique-modal-title">Technique</h2>
                <div id="wi-technique-modal-body"
                     class="wi-technique-modal-body"></div>
            </section>`;

        document.body.appendChild(
            modal);

        modal.addEventListener(
            "click",
            event => {
                if (event.target.closest(
                        "[data-technique-close]")) {
                    closeTechniqueModal();
                }
            });

        return modal;
    }

    function closeTechniqueModal() {
        const modal =
            document.getElementById(
                "wi-technique-modal");

        if (!modal) {
            return;
        }

        modal.hidden = true;

        document.body.classList.remove(
            "wi-modal-open");
    }

    async function openTechnique(
        exerciseId,
        fallbackName = "Technique") {

        if (!exerciseId) {
            return;
        }

        const modal =
            ensureTechniqueModal();

        const title =
            modal.querySelector(
                "#wi-technique-modal-title");

        const body =
            modal.querySelector(
                "#wi-technique-modal-body");

        title.textContent =
            fallbackName || "Technique";

        body.textContent =
            "Loading technique...";

        modal.hidden = false;

        document.body.classList.add(
            "wi-modal-open");

        try {
            const response =
                await fetch(
                    `${techniqueUrl}?exerciseId=${encodeURIComponent(exerciseId)}`,
                    {
                        headers: {
                            "X-Requested-With":
                                "XMLHttpRequest"
                        }
                    });

            if (!response.ok) {
                throw new Error(
                    `Technique lookup failed (${response.status}).`);
            }

            const data =
                await response.json();

            title.textContent =
                data.name
                || fallbackName
                || "Technique";

            body.replaceChildren();

            const instructions =
                data.instructions || [];

            if (instructions.length === 0) {
                body.textContent =
                    "No technique instructions are available for this exercise.";
                return;
            }

            const list =
                document.createElement("ol");

            instructions.forEach(
                instruction => {
                    const li =
                        document.createElement("li");

                    li.textContent =
                        instruction;

                    list.appendChild(li);
                });

            body.appendChild(list);
        }
        catch (error) {
            body.textContent =
                error?.message
                || "Technique instructions could not be loaded.";
        }
    }

    document.addEventListener(
        "keydown",
        event => {
            if (event.key === "Escape") {
                closeTechniqueModal();
            }
        });

    function renderSetRepFields(
        card,
        requestedCount,
        preferredValues = []) {

        const group =
            card.querySelector(
                "[data-set-reps]");

        if (!group) {
            return;
        }

        const count =
            Math.max(
                1,
                Math.min(
                    20,
                    Number(requestedCount) || 1));

        const existing =
            [...group.querySelectorAll(
                "[data-set-rep]")]
                .map(
                    input =>
                        Number(input.value) || 0);

        const existingLoads =
            [...group.querySelectorAll(
                "[data-set-load]")]
                .map(
                    input =>
                        String(input.value || ""));

        const hiddenReps =
            card.querySelector(
                '[data-field="Reps"]');

        const fallback =
            preferredValues.find(
                value =>
                    Number(value) > 0)
            || existing.find(
                value =>
                    value > 0)
            || Number(hiddenReps?.value)
            || 10;

        const values =
            Array.from(
                {
                    length: count
                },
                (_, index) =>
                    preferredValues[index]
                    || existing[index]
                    || existing.at(-1)
                    || fallback);

        group.replaceChildren();

        values.forEach(
            (value, index) => {
                const label =
                    document.createElement(
                        "label");

                label.className =
                    "wi-set-rep-field";

                const caption =
                    document.createElement(
                        "span");

                caption.textContent =
                    `Reps ${index + 1}`;

                const input =
                    document.createElement(
                        "input");

                input.type = "number";
                input.min = "1";
                input.max = "500";
                input.value = value;
                input.dataset.field =
                    `SetReps[${index}]`;
                input.dataset.setRep = "";

                input.addEventListener(
                    "input",
                    () => {
                        if (index === 0
                            && hiddenReps) {
                            hiddenReps.value =
                                input.value;
                        }
                    });

                const loadInput =
                    document.createElement(
                        "input");

                loadInput.type =
                    "hidden";

                loadInput.value =
                    existingLoads[index]
                    || "";

                loadInput.dataset.field =
                    `SetLoads[${index}]`;

                loadInput.dataset.setLoad =
                    "";

                label.appendChild(caption);
                label.appendChild(input);
                label.appendChild(loadInput);
                group.appendChild(label);
            });

        if (hiddenReps) {
            hiddenReps.value =
                values[0] || fallback;
        }

        if (activeWorkoutIsActualSession()) {
            ensureActiveSetLoadFields(card);
        }
    }

    function initializeSetRepCard(card) {
        if (!card
            || card.dataset.setRepInitialized
                === "true") {
            return;
        }

        const setsInput =
            card.querySelector(
                "[data-set-count]");

        if (!setsInput) {
            return;
        }

        card.dataset.setRepInitialized =
            "true";

        setsInput.addEventListener(
            "input",
            () => {
                renderSetRepFields(
                    card,
                    setsInput.value);

                renumber();
            });

        card.querySelectorAll(
            "[data-set-rep]"
        ).forEach(
            (input, index) => {
                input.addEventListener(
                    "input",
                    () => {
                        if (index !== 0) {
                            return;
                        }

                        const hiddenReps =
                            card.querySelector(
                                '[data-field="Reps"]');

                        if (hiddenReps) {
                            hiddenReps.value =
                                input.value;
                        }
                    });
            });
    }

    function createExerciseCard(item) {
        const card =
            document.createElement("article");

        card.className = "wi-workout-exercise";
        card.dataset.exerciseCard = "";
        card.dataset.exerciseId =
            item.exerciseId;

        const id =
            document.createElement("input");

        id.type = "hidden";
        id.dataset.field = "ExerciseId";
        id.value = item.exerciseId;
        card.appendChild(id);

        const nameInput =
            document.createElement("input");

        nameInput.type = "hidden";
        nameInput.dataset.field = "ExerciseName";
        nameInput.value = item.name;
        card.appendChild(nameInput);

        const mechanicsInput =
            document.createElement("input");

        mechanicsInput.type = "hidden";
        mechanicsInput.dataset.field = "Mechanics";
        mechanicsInput.value =
            item.mechanics || "";

        card.appendChild(mechanicsInput);

        const name =
            document.createElement("div");

        name.className =
            "wi-workout-exercise-name";

        const strong =
            document.createElement("strong");

        strong.textContent = item.name;
        name.appendChild(strong);

        const selectedSeparator =
            document.createElement("span");

        selectedSeparator.className =
            "wi-title-separator";
        selectedSeparator.textContent =
            " - ";

        name.appendChild(
            selectedSeparator);

        const selectedTechnique =
            document.createElement("button");

        selectedTechnique.type =
            "button";
        selectedTechnique.className =
            "wi-technique-button wi-selected-technique";
        selectedTechnique.dataset.techniqueExerciseId =
            item.exerciseId;
        selectedTechnique.dataset.techniqueExerciseName =
            item.name;
        selectedTechnique.textContent =
            "Technique";

        name.appendChild(
            selectedTechnique);

        card.appendChild(name);

        const fallbackReps =
            item.defaultReps ?? 10;

        const hiddenReps =
            document.createElement("input");

        hiddenReps.type = "hidden";
        hiddenReps.dataset.field = "Reps";
        hiddenReps.value = fallbackReps;
        card.appendChild(hiddenReps);

        const hiddenUnit =
            document.createElement("input");

        hiddenUnit.type = "hidden";
        hiddenUnit.dataset.field =
            "LoadUnitCode";
        hiddenUnit.value =
            (item.defaultLoadUnitCode || defaultUnit)
                .toUpperCase() === "KG"
                ? "KG"
                : "LB";

        card.appendChild(hiddenUnit);

        const setsInput =
            makeNumberInput(
                "Sets",
                item.defaultSets ?? 3,
                1,
                20);

        setsInput.dataset.setCount = "";

        const setsLabel =
            makeLabel(
                "Sets",
                setsInput);

        setsLabel.className =
            "wi-sets-field";

        card.appendChild(
            setsLabel);

        const repsGroup =
            document.createElement("div");

        repsGroup.className =
            "wi-set-reps-group";
        repsGroup.dataset.setReps = "";

        card.appendChild(
            repsGroup);

        renderSetRepFields(
            card,
            Number(setsInput.value) || 3,
            [fallbackReps]);

        const heaviestLabel =
            makeLabel(
                "Heaviest",
                makeNumberInput(
                    "MaxWeight",
                    item.defaultMaxWeight == null
                        ? null
                        : Math.round(
                            Number(item.defaultMaxWeight)),
                    0,
                    10000,
                    1,
                    "Heaviest"));

        heaviestLabel.className =
            "wi-heaviest-field";

        card.appendChild(
            heaviestLabel);

        const remove =
            document.createElement("button");

        remove.type = "button";
        remove.className =
            "workout-delete-button wi-remove-exercise";

        remove.dataset.removeExercise = "";
        remove.textContent = "Remove";
        card.appendChild(remove);

        return card;
    }

    function ensureSelectedTechniqueLinks() {
        root.querySelectorAll(
            "[data-exercise-card]"
        ).forEach(card => {
            const name =
                card.querySelector(
                    ".wi-workout-exercise-name");

            const exerciseId =
                card.querySelector(
                    '[data-field="ExerciseId"]')
                    ?.value;

            const exerciseName =
                card.querySelector(
                    '[data-field="ExerciseName"]')
                    ?.value
                || name?.textContent?.trim()
                || "Technique";

            if (!name
                || !exerciseId
                || name.querySelector(
                    "[data-technique-exercise-id]")) {
                return;
            }

            const separator =
                document.createElement("span");

            separator.className =
                "wi-title-separator";
            separator.textContent =
                " - ";

            const technique =
                document.createElement("button");

            technique.type =
                "button";
            technique.className =
                "wi-technique-button wi-selected-technique";
            technique.dataset.techniqueExerciseId =
                exerciseId;
            technique.dataset.techniqueExerciseName =
                exerciseName;
            technique.textContent =
                "Technique";

            name.appendChild(separator);
            name.appendChild(technique);
        });
    }

    // ================================================================
    // B2.15 stable Move handle
    //
    // The page remains scrollable everywhere EXCEPT the compact Move
    // button. The Move button has touch-action:none so phone dragging
    // reorders the exercise card without scrolling the page.
    // ================================================================

    let moveState = null;

    function moveCardByPointer(
        card,
        clientY) {

        const otherCards =
            [...root.querySelectorAll(
                "[data-exercise-card]")]
                .filter(candidate =>
                    candidate !== card);

        let before = null;

        for (const candidate of otherCards) {
            const rect =
                candidate.getBoundingClientRect();

            if (clientY <
                rect.top + (rect.height / 2)) {
                before = candidate;
                break;
            }
        }

        root.insertBefore(
            card,
            before);
    }

    function finishMove() {
        root.querySelectorAll(
            "[data-exercise-card].is-dragging"
        ).forEach(card =>
            card.classList.remove(
                "is-dragging"));

        root.classList.remove(
            "is-drag-active");

        moveState = null;
        renumber();
    }

    function ensureWorkoutModeCards() {
        root.querySelectorAll(
            "[data-exercise-card]"
        ).forEach(card => {
            card.removeAttribute(
                "draggable");

            delete card.dataset.reorderCard;

            const name =
                card.querySelector(
                    ".wi-workout-exercise-name");

            if (name) {
                name.removeAttribute(
                    "draggable");
                delete name.dataset.reorderHandle;
            }

            card.querySelectorAll(
                ".wi-drag-handle"
            ).forEach(old =>
                old.remove());

            let moveButton =
                card.querySelector(
                    "[data-card-move-handle]");

            if (!moveButton) {
                moveButton =
                    document.createElement(
                        "button");

                moveButton.type =
                    "button";

                moveButton.className =
                    "button wi-card-move-handle";

                moveButton.dataset.cardMoveHandle =
                    "";

                moveButton.textContent =
                    "ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¾ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¾ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¾ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¾ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ Move";

                moveButton.setAttribute(
                    "aria-label",
                    "Move this exercise up or down");

                const remove =
                    card.querySelector(
                        "[data-remove-exercise]");

                if (remove) {
                    card.insertBefore(
                        moveButton,
                        remove);
                }
                else {
                    card.appendChild(
                        moveButton);
                }
            }

            card.querySelectorAll(
                'input[type="number"]'
            ).forEach(input => {
                input.inputMode =
                    input.dataset.field === "MaxWeight"
                        ? "decimal"
                        : "numeric";

                input.autocomplete =
                    "off";
            });

            const weight =
                card.querySelector(
                    '[data-field="MaxWeight"]');

            if (weight) {
                weight.placeholder =
                    "Heaviest";
            }
        });
    }

    root.addEventListener(
        "pointerdown",
        event => {
            const handle =
                event.target.closest(
                    "[data-card-move-handle]");

            if (!handle) {
                return;
            }

            const card =
                handle.closest(
                    "[data-exercise-card]");

            if (!card) {
                return;
            }

            event.preventDefault();

            moveState = {
                pointerId:
                    event.pointerId,
                card,
                handle
            };

            card.classList.add(
                "is-dragging");

            root.classList.add(
                "is-drag-active");

            try {
                handle.setPointerCapture(
                    event.pointerId);
            }
            catch {
                // Pointer capture is optional.
            }

            if (navigator.vibrate) {
                navigator.vibrate(12);
            }
        });

    root.addEventListener(
        "pointermove",
        event => {
            const state =
                moveState;

            if (!state
                || state.pointerId
                    !== event.pointerId) {
                return;
            }

            event.preventDefault();

            moveCardByPointer(
                state.card,
                event.clientY);
        });

    root.addEventListener(
        "pointerup",
        event => {
            const state =
                moveState;

            if (!state
                || state.pointerId
                    !== event.pointerId) {
                return;
            }

            event.preventDefault();
            finishMove();
        });

    root.addEventListener(
        "pointercancel",
        event => {
            const state =
                moveState;

            if (!state
                || state.pointerId
                    !== event.pointerId) {
                return;
            }

            finishMove();
        });

    // Desktop users can also drag directly from the Move button.
    root.addEventListener(
        "mousedown",
        event => {
            const handle =
                event.target.closest(
                    "[data-card-move-handle]");

            if (handle) {
                handle.setAttribute(
                    "draggable",
                    "true");
            }
        });

    root.addEventListener(
        "dragstart",
        event => {
            const handle =
                event.target.closest(
                    "[data-card-move-handle]");

            if (!handle) {
                return;
            }

            const card =
                handle.closest(
                    "[data-exercise-card]");

            if (!card) {
                return;
            }

            card.classList.add(
                "is-dragging");

            root.classList.add(
                "is-drag-active");

            if (event.dataTransfer) {
                event.dataTransfer.effectAllowed =
                    "move";

                event.dataTransfer.setData(
                    "text/plain",
                    card.dataset.exerciseId
                    || "exercise");
            }
        });

    root.addEventListener(
        "dragover",
        event => {
            const dragging =
                root.querySelector(
                    "[data-exercise-card].is-dragging");

            if (!dragging) {
                return;
            }

            event.preventDefault();

            moveCardByPointer(
                dragging,
                event.clientY);
        });

    root.addEventListener(
        "drop",
        event => {
            if (!root.querySelector(
                    "[data-exercise-card].is-dragging")) {
                return;
            }

            event.preventDefault();
            finishMove();
        });

    root.addEventListener(
        "dragend",
        () => {
            if (root.querySelector(
                    "[data-exercise-card].is-dragging")) {
                finishMove();
            }
        });
    function ensureWorkoutModeActions() {
        form.classList.add(
            "wi-workout-mode-ready");

        let actions =
            form.querySelector(
                ".wi-primary-save-row");

        if (!actions) {
            actions =
                [...form.querySelectorAll(
                    ".wi-actions")]
                    .find(row =>
                        row.querySelector(
                            '[name="completeWorkout"]'));
        }

        if (!actions) {
            return;
        }

        let addButton =
            actions.querySelector(
                "[data-add-exercise-bottom]");

        if (!addButton) {
            addButton =
                document.createElement(
                    "button");

            addButton.type = "button";
            addButton.className =
                "button wi-add-exercise-bottom";
            addButton.dataset.addExerciseBottom = "";
            addButton.textContent =
                "+ Add Exercise";

            actions.insertBefore(
                addButton,
                actions.firstChild);

            addButton.addEventListener(
                "click",
                () => {
                    if (!searchRoot
                        || !searchInput) {
                        return;
                    }

                    actions.parentElement
                        ?.insertBefore(
                            searchRoot,
                            actions);

                    searchRoot.classList.add(
                        "wi-bottom-exercise-search");

                    searchInput.value = "";
                    clearSearch(
                        "Type an exercise name or muscle to add it.");

                    searchRoot.scrollIntoView({
                        behavior: "smooth",
                        block: "center"
                    });

                    window.setTimeout(
                        () => searchInput.focus(),
                        220);
                });
        }

        const complete =
            actions.querySelector(
                '[name="completeWorkout"]');

        if (complete) {
            complete.textContent =
                "Complete";

            complete.classList.add(
                "wi-complete-workout");
        }

        const save =
            actions.querySelector(
                '[name="saveTemplate"]');

        if (save) {
            save.classList.add(
                "wi-save-workout");
        }
    }
    function renumber() {
        ensureWorkoutModeCards();
        ensureWorkoutModeActions();

        const cards =
            [...root.querySelectorAll(
                "[data-exercise-card]")];

        cards.forEach(
            (card, exerciseIndex) => {

                card.querySelectorAll(
                    "[data-field]"
                ).forEach(control => {
                    control.name =
                        `Exercises[${exerciseIndex}].${control.dataset.field}`;
                });
            });

        if (emptyState) {
            emptyState.hidden =
                cards.length > 0;
        }

        ensureSelectedTechniqueLinks();

        cards.forEach(
            card =>
                initializeSetRepCard(card));

        if (typeof activeWorkoutIsActualSession === "function"
            && activeWorkoutIsActualSession()) {
            ensureActiveWorkoutCardControls();
            ensureInsertionLinks();
        }
    }

    function clearSearch(message = "Type an exercise name or muscle to search.") {
        if (searchResults) {
            searchResults.replaceChildren();
            searchResults.hidden = true;
        }

        if (searchStatus) {
            searchStatus.textContent = message;
        }
    }

    function addExercise(item) {
        if (!item || !item.exerciseId) {
            return;
        }

        const selector =
            `[data-exercise-card][data-exercise-id="${CSS.escape(item.exerciseId)}"]`;

        const existing =
            root.querySelector(selector);

        if (existing) {
            if (searchStatus) {
                searchStatus.textContent =
                    `${item.name} is already in this workout.`;
            }

            existing.scrollIntoView({
                behavior: "smooth",
                block: "center"
            });

            return;
        }

        const card =
            createExerciseCard(item);

        // There is now only ONE Add Exercise button at the bottom.
        // Remove the current insertion row before appending the card.
        root.querySelectorAll(
            ".wi-insert-exercise-row"
        ).forEach(row => row.remove());

        root.appendChild(card);

        renumber();

        if (typeof ensureActiveWorkoutCardControls === "function") {
            ensureActiveWorkoutCardControls();
        }

        if (typeof b223NormalizeCard === "function") {
            b223NormalizeCard(card);
        }

        if (typeof ensureInsertionLinks === "function") {
            ensureInsertionLinks();
        }

        if (typeof b225KeepOnlyBottomAdd === "function") {
            b225KeepOnlyBottomAdd();
        }

        if (searchResults) {
            searchResults.replaceChildren();
            searchResults.hidden = true;
            searchResults.style.display = "none";
        }

        if (searchInput) {
            searchInput.value = "";
            searchInput.blur();
        }

        if (searchStatus) {
            searchStatus.textContent =
                `${item.name} added.`;
        }

        card.scrollIntoView({
            behavior: "smooth",
            block: "center"
        });
    }

    function b224AddExercise(item) {
        if (!item || !item.exerciseId) {
            return false;
        }

        const selector =
            `[data-exercise-card][data-exercise-id="${CSS.escape(item.exerciseId)}"]`;

        const existing =
            root.querySelector(selector);

        if (existing) {
            existing.scrollIntoView({
                behavior: "smooth",
                block: "center"
            });

            b224CloseSearchAfterAdd(
                item.name || "Exercise");

            return true;
        }

        const newCard =
            createExerciseCard(item);

        if (!newCard) {
            return false;
        }

        if (activeWorkoutIsActualSession()
            && activeInsertAfterCard
            && activeInsertAfterCard.isConnected) {
            activeInsertAfterCard
                .insertAdjacentElement(
                    "afterend",
                    newCard);

            activeInsertAfterCard = null;
        }
        else {
            root.appendChild(newCard);
        }

        renumber();

        if (typeof b223NormalizeCard === "function") {
            b223NormalizeCard(newCard);
        }

        if (typeof ensureActiveWorkoutCardControls === "function") {
            ensureActiveWorkoutCardControls();
        }

        if (typeof ensureInsertionLinks === "function") {
            ensureInsertionLinks();
        }

        newCard.scrollIntoView({
            behavior: "smooth",
            block: "center"
        });

        b224CloseSearchAfterAdd(
            item.name || "Exercise");

        return true;
    }
    function renderResults(items) {
        if (!searchResults) {
            return;
        }

        
        
        // B224_REOPEN_RESULTS
        searchResults.style.removeProperty("display");searchResults.style.removeProperty("display");
searchResults.replaceChildren();

        items.forEach(item => {
            const row =
                document.createElement("div");

            row.className =
                "wi-search-result";

            row.tabIndex = 0;
            row.setAttribute(
                "role",
                "button");

            const heart =
                document.createElement("button");

            heart.type = "button";
            heart.className =
                "wi-favorite-button";

            heart.dataset.favoriteButton = "";
            heart.setAttribute(
                "aria-label",
                item.isFavorite
                    ? `Remove ${item.name} from favorites`
                    : `Add ${item.name} to favorites`);

            heart.classList.toggle(
                "is-favorite",
                Boolean(item.isFavorite));

            heart.textContent =
                item.isFavorite
                    ? "\u2665"
                    : "\u2661";

            row.appendChild(heart);

            const copy =
                document.createElement("div");

            copy.className =
                "wi-search-copy";

            const titleRow =
                document.createElement("div");

            titleRow.className =
                "wi-search-title-row";

            const name =
                document.createElement("strong");

            name.textContent = item.name;
            titleRow.appendChild(name);

            const titleSeparator =
                document.createElement("span");

            titleSeparator.className =
                "wi-title-separator";
            titleSeparator.textContent = " - ";
            titleRow.appendChild(
                titleSeparator);

            const metadata =
                document.createElement("span");

            const pieces = [];

            if (item.matchedMuscleName) {
                const role =
                    item.matchedMuscleRole
                        ? ` (${item.matchedMuscleRole})`
                        : "";

                pieces.push(
                    `${item.matchedMuscleName}${role}`);
            }
            else {
                const muscles =
                    (item.primaryMuscles || [])
                        .join(", ");

                if (muscles) {
                    pieces.push(muscles);
                }
            }

            if (item.mechanics) {
                pieces.push(item.mechanics);
            }

            if (item.difficultyLevel) {
                pieces.push(item.difficultyLevel);
            }

            if (item.forceType) {
                pieces.push(item.forceType);
            }

            if (item.category) {
                pieces.push(item.category);
            }

            if ((item.equipment || []).length > 0) {
                pieces.push(
                    item.equipment.join(", "));
            }

            metadata.textContent =
                pieces.length > 0
                    ? pieces.join(" | ")
                    : "Exercise details unavailable";

            const technique =
                document.createElement("button");

            technique.type = "button";
            technique.className =
                "wi-technique-button";
            technique.dataset.techniqueButton = "";
            technique.textContent = "Technique";
            technique.setAttribute(
                "aria-expanded",
                "false");

            titleRow.appendChild(technique);
            copy.appendChild(titleRow);
            copy.appendChild(metadata);

            technique.addEventListener(
                "click",
                event => {
                    event.stopPropagation();

                    openTechnique(
                        item.exerciseId,
                        item.name);
                });

            row.appendChild(copy);
            row.addEventListener(
                "click",
                event => {
                    if (event.target.closest(
                            "[data-favorite-button]")
                        || event.target.closest(
                            "[data-technique-button]")) {
                        return;
                    }

                    event.preventDefault();
                    addExercise(item);
                });

            row.addEventListener(
                "keydown",
                event => {
                    if (event.key !== "Enter"
                        && event.key !== " ") {
                        return;
                    }

                    if (event.target.closest(
                            "[data-favorite-button]")
                        || event.target.closest(
                            "[data-technique-button]")) {
                        return;
                    }

                    event.preventDefault();
                    addExercise(item);
                });

            heart.addEventListener(
                "click",
                async event => {
                    event.stopPropagation();

                    const next =
                        !heart.classList.contains(
                            "is-favorite");

                    await setFavorite(
                        item,
                        heart,
                        next);
                });

            searchResults.appendChild(row);
        });

        searchResults.hidden =
            items.length === 0;
    }

    async function setFavorite(
        item,
        heart,
        favorite) {

        if (!searchRoot
            || !antiForgery) {
            return;
        }

        const url =
            searchRoot.dataset.favoriteUrl;

        if (!url) {
            return;
        }

        heart.disabled = true;

        try {
            const body =
                new URLSearchParams();

            body.set(
                "__RequestVerificationToken",
                antiForgery.value);

            body.set(
                "exerciseId",
                item.exerciseId);

            body.set(
                "favorite",
                favorite ? "true" : "false");

            const response =
                await fetch(
                    url,
                    {
                        method: "POST",
                        headers: {
                            "Content-Type":
                                "application/x-www-form-urlencoded;charset=UTF-8",
                            "X-Requested-With":
                                "XMLHttpRequest"
                        },
                        body: body.toString()
                    });

            if (!response.ok) {
                throw new Error(
                    `Favorite update failed (${response.status}).`);
            }

            item.isFavorite = favorite;

            heart.classList.toggle(
                "is-favorite",
                favorite);

            heart.textContent =
                favorite ? "\u2665" : "\u2661";

            const query =
                searchInput?.value.trim()
                || "";

            if (query) {
                await search();
            }
        }
        catch (error) {
            if (searchStatus) {
                searchStatus.textContent =
                    error?.message
                    || "Favorite update failed.";
            }
        }
        finally {
            heart.disabled = false;
        }
    }

    async function search() {
        if (!searchRoot
            || !searchInput
            || !searchStatus) {
            return;
        }

        const query =
            searchInput.value.trim();

        if (!query) {
            clearSearch();
            return;
        }

        const url =
            searchRoot.dataset.searchUrl;

        if (!url) {
            return;
        }

        searchStatus.textContent =
            "Searching...";

        try {
            const response =
                await fetch(
                    `${url}?q=${encodeURIComponent(query)}&take=40`,
                    {
                        headers: {
                            "X-Requested-With":
                                "XMLHttpRequest"
                        }
                    });

            if (!response.ok) {
                throw new Error(
                    `Search failed (${response.status}).`);
            }

            const items =
                await response.json();

            renderResults(items);

            searchStatus.textContent =
                items.length === 0
                    ? "No exercises matched."
                    : `${items.length} match${items.length === 1 ? "" : "es"}. Click one to add it.`;
        }
        catch (error) {
            clearSearch(
                error?.message
                || "Exercise search failed.");
        }
    }

    searchInput?.addEventListener(
        "input",
        () => {
            clearTimeout(searchTimer);

            if (!searchInput.value.trim()) {
                clearSearch();
                return;
            }

            searchTimer =
                setTimeout(search, 250);
        });

    searchInput?.addEventListener(
        "keydown",
        event => {
            if (event.key === "Enter") {
                event.preventDefault();
                clearTimeout(searchTimer);
                search();
            }
        });

    form.addEventListener(
        "submit",
        renumber);

    renumber();
    clearSearch(
        searchStatus?.textContent
        || "Type an exercise name or muscle to search.");

    // ================================================================
    // B2.16 ACTIVE WORKOUT ENVIRONMENT
    // ================================================================

    let activeInsertAfterCard = null;

    function activeWorkoutIsActualSession() {
        const form = document.getElementById("wi-detailed-workout-form");
        if (!form) return false;

        const session = form.querySelector('input[name="WorkoutSessionId"]');
        const editCompleted = form.querySelector('input[name="EditCompleted"]');
        const templateOnly = form.querySelector('input[name="TemplateOnlyMode"]');

        const hasSession =
            !!session && !!session.value &&
            session.value !== "00000000-0000-0000-0000-000000000000";

        const editingHistory =
            !!editCompleted &&
            String(editCompleted.value).toLowerCase() === "true";

        const planningOnly =
            !!templateOnly &&
            String(templateOnly.value).toLowerCase() === "true";

        return hasSession && !editingHistory && !planningOnly;
    }

    function ensureActiveWorkoutOverlay() {
        if (!activeWorkoutIsActualSession()) return;

        const form = document.getElementById("wi-detailed-workout-form");
        if (!form || form.closest(".wi-active-workout-overlay")) return;

        const overlay = document.createElement("div");
        overlay.className = "wi-active-workout-overlay";
        overlay.setAttribute("role","dialog");
        overlay.setAttribute("aria-modal","true");
        overlay.setAttribute("aria-label","Active workout");

        const shell = document.createElement("section");
        shell.className = "wi-active-workout-shell";

        const header = document.createElement("header");
        header.className = "wi-active-workout-header";

        const titleWrap = document.createElement("div");
        const kicker = document.createElement("div");
        kicker.className = "app-kicker";
        kicker.textContent = "ACTIVE WORKOUT";

        const title = document.createElement("h2");
        const workoutName = form.querySelector('input[name="WorkoutName"]');
        title.textContent = workoutName?.value || "Workout";

        titleWrap.append(kicker,title);

        const back = document.createElement("a");
        back.className = "button wi-active-workout-back";
        const dateInput = form.querySelector('input[name="Date"]');
        const date = dateInput?.value || "";
        back.href = "/DetailedWorkout" + (date ? "?date=" + encodeURIComponent(date) : "");
        back.textContent = "Back";
        back.setAttribute("aria-label","Return to workout library");

        header.append(titleWrap,back);
        shell.appendChild(header);

        const parent = form.parentNode;
        parent.insertBefore(overlay,form);
        overlay.appendChild(shell);
        shell.appendChild(form);

        document.body.classList.add("wi-active-workout-open");
        form.classList.add("wi-active-workout-form");

        const saveButton = form.querySelector('button[name="saveTemplate"][value="true"]');
        if (saveButton) saveButton.hidden = true;

        const complete = form.querySelector('button[name="completeWorkout"][value="true"]');
        if (complete) {
            complete.textContent = "Complete Workout";
            complete.classList.add("wi-active-complete");
        }

        const saveNote = form.querySelector(".wi-save-note");
        if (saveNote) saveNote.hidden = true;

        ensureActiveWorkoutCardControls();
        ensureInsertionLinks();
    }

    function moveExerciseCard(card,direction) {
        if (!card) return;

        const cards =
            [...root.querySelectorAll("[data-exercise-card]")];

        const index =
            cards.indexOf(card);

        if (index < 0) return;

        if (direction < 0 && index > 0) {
            root.insertBefore(
                card,
                cards[index - 1]);

            renumber();
            return;
        }

        if (direction > 0
            && index < cards.length - 1) {
            root.insertBefore(
                cards[index + 1],
                card);

            renumber();
        }
    }

    function ensureB220SetFields(card) {
        if (!card) return;

        const repInputs =
            [...card.querySelectorAll("[data-set-rep]")];

        if (repInputs.length === 0) return;

        let loadInputs =
            [...card.querySelectorAll("[data-set-load]")];

        while (loadInputs.length < repInputs.length) {
            const load =
                document.createElement("input");

            load.type = "hidden";
            load.dataset.field =
                `SetLoads[${loadInputs.length}]`;
            load.dataset.setLoad = "";

            const group =
                card.querySelector("[data-set-reps]");

            group?.appendChild(load);
            loadInputs.push(load);
        }

        const maxWeight =
            card.querySelector(
                '[data-field="MaxWeight"]');

        const hasExistingLoad =
            loadInputs.some(
                input =>
                    String(input.value || "").trim() !== "");

        if (!hasExistingLoad
            && maxWeight?.value
            && loadInputs.length > 0) {
            loadInputs.at(-1).value =
                maxWeight.value;
        }

        repInputs.forEach((repInput,index) => {
            const label =
                repInput.closest(
                    ".wi-set-rep-field");

            if (!label) return;

            label.classList.add(
                "wi-active-set-column");

            const caption =
                label.querySelector("span");

            if (caption) {
                caption.textContent =
                    `Set ${index + 1}`;
            }

            repInput.placeholder = "Reps";
            repInput.inputMode = "numeric";

            const loadInput =
                loadInputs[index];

            loadInput.type = "number";
            loadInput.min = "0";
            loadInput.max = "10000";
            loadInput.step = "1";
            loadInput.inputMode = "decimal";
            loadInput.placeholder = "Wt";
            loadInput.classList.add(
                "wi-set-load-input");

            if (loadInput.parentElement !== label) {
                label.appendChild(loadInput);
            }

            if (loadInput.dataset.b220Bound !== "true") {
                loadInput.dataset.b220Bound = "true";

                loadInput.addEventListener(
                    "input",
                    () => {
                        const loads =
                            [...card.querySelectorAll(
                                "[data-set-load]")];

                        const values =
                            loads
                                .map(x => {
                                    const raw =
                                        String(x.value || "").trim();

                                    if (!raw) return null;

                                    const n = Number(raw);

                                    return Number.isFinite(n)
                                        ? n
                                        : null;
                                })
                                .filter(x => x !== null);

                        const last =
                            loads.at(-1);

                        if (last) {
                            last.required =
                                values.length > 0;
                        }

                        if (maxWeight
                            && values.length > 0) {
                            maxWeight.value =
                                Math.max(...values);
                        }
                    });
            }
        });
    }
    function ensureActiveWorkoutCardControls() {
        if (!activeWorkoutIsActualSession()) return;

        root.querySelectorAll("[data-exercise-card]").forEach(card => {
            card.removeAttribute("draggable");

            card.querySelectorAll(
                "[data-card-move-handle], .wi-drag-handle")
                .forEach(button => button.remove());

            let controls =
                card.querySelector(
                    ".wi-active-order-controls");

            if (!controls) {
                controls =
                    document.createElement("div");

                controls.className =
                    "wi-active-order-controls";

                const up =
                    document.createElement("button");

                up.type = "button";
                up.className =
                    "button wi-active-order-button";
                up.dataset.exerciseUp = "";
                up.textContent = "\u2191";
                up.setAttribute(
                    "aria-label",
                    "Move exercise up");

                const down =
                    document.createElement("button");

                down.type = "button";
                down.className =
                    "button wi-active-order-button";
                down.dataset.exerciseDown = "";
                down.textContent = "\u2193";
                down.setAttribute(
                    "aria-label",
                    "Move exercise down");

                controls.append(up,down);
            }

            const remove =
                card.querySelector(
                    "[data-remove-exercise]");

            if (remove
                && remove.parentElement !== controls) {
                controls.appendChild(remove);
            }

            const exerciseName =
                card.querySelector(
                    ".wi-workout-exercise-name");

            if (exerciseName) {
                exerciseName.insertAdjacentElement(
                    "afterend",
                    controls);
            }
            else if (!controls.isConnected) {
                card.prepend(controls);
            }
            ensureB220SetFields(card);
        });
    }

    function ensureInsertionLinks() {
        if (!activeWorkoutIsActualSession()) return;

        document.querySelectorAll(".wi-insert-exercise-row")
            .forEach(row => row.remove());

        [...root.querySelectorAll("[data-exercise-card]")].forEach(card => {
            const row = document.createElement("div");
            row.className = "wi-insert-exercise-row";

            const link = document.createElement("button");
            link.type = "button";
            link.className = "wi-insert-exercise-link";
            link.dataset.insertExerciseHere = "";
            link.textContent = "+ Add exercise here";

            row.appendChild(link);
            card.insertAdjacentElement("afterend",row);
        });
    }

    root.addEventListener("click",event => {
        const up = event.target.closest("[data-exercise-up]");
        if (up) {
            event.preventDefault();
            moveExerciseCard(up.closest("[data-exercise-card]"),-1);
            ensureInsertionLinks();
            return;
        }

        const down = event.target.closest("[data-exercise-down]");
        if (down) {
            event.preventDefault();
            moveExerciseCard(down.closest("[data-exercise-card]"),1);
            ensureInsertionLinks();
            return;
        }

        const insert = event.target.closest("[data-insert-exercise-here]");
        if (!insert) return;

        event.preventDefault();

        const row = insert.closest(".wi-insert-exercise-row");
        activeInsertAfterCard =
            row?.previousElementSibling?.matches("[data-exercise-card]")
                ? row.previousElementSibling
                : null;

        const search = document.getElementById("wi-exercise-search");
        const input = document.getElementById("wi-search-input");

        if (search) {
            search.classList.add("wi-active-exercise-picker-open");
            search.scrollIntoView({behavior:"smooth",block:"center"});
        }

        if (input) window.setTimeout(() => input.focus(),180);
    });
window.setTimeout(() => {
        ensureActiveWorkoutOverlay();
        ensureActiveWorkoutCardControls();
        ensureInsertionLinks();
    },0);


    // ================================================================
    // B2.17.1 ACTIVE WORKOUT FREEZE HOTFIX
    // Removed recursive root MutationObserver.
    // Insert-here now places the new exercise directly.
    // ================================================================

    // ================================================================
    // B2.19 ACTIVE WORKOUT UI CLEANUP
    // Search closes after selection.
    // Up / Down / Remove sit under exercise name.
    // Card movement ignores insert rows.
    // ================================================================

    // ================================================================
    // B2.20 ACTIVE WORKOUT LAYOUT + SEARCH CLOSE FIX
    // Self-contained set Weight fields and deterministic search closing.
    // ================================================================

    // ================================================================
    // B2.21 ACTIVE WORKOUT POPUP CLEAN REBUILD
    // APPEND-ONLY runtime stabilizer.
    // ================================================================

    function b221CloseExercisePicker(message) {
        const searchRoot =
            document.getElementById("wi-exercise-search");

        const searchInput =
            document.getElementById("wi-search-input");

        const searchResults =
            document.getElementById("wi-search-results");

        const searchStatus =
            document.getElementById("wi-search-status");

        if (searchResults) {
            searchResults.replaceChildren();
            searchResults.hidden = true;
            searchResults.style.display = "none";
        }

        if (searchInput) {
            searchInput.value = "";
            searchInput.blur();
        }

        if (searchRoot) {
            searchRoot.classList.remove(
                "wi-active-exercise-picker-open");
        }

        if (searchStatus && message) {
            searchStatus.textContent = message;
        }
    }

    function b221PrepareSearchForTyping() {
        const searchResults =
            document.getElementById("wi-search-results");

        if (searchResults) {
            searchResults.style.removeProperty("display");
        }
    }

    function b221NormalizeActiveCard(card) {
        if (!card) return;

        const name =
            card.querySelector(
                ".wi-workout-exercise-name");

        const controls =
            card.querySelector(
                ".wi-active-order-controls");

        const sets =
            card.querySelector(
                ".wi-sets-field");

        const setGrid =
            card.querySelector(
                "[data-set-reps]");

        if (name) {
            name.dataset.b221Role = "name";
        }

        if (controls) {
            controls.dataset.b221Role =
                "controls";
        }

        if (sets) {
            sets.dataset.b221Role = "sets";
        }

        if (setGrid) {
            setGrid.dataset.b221Role =
                "set-grid";
        }

        const remove =
            card.querySelector(
                "[data-remove-exercise]");

        if (remove && controls
            && remove.parentElement !== controls) {
            controls.appendChild(remove);
        }

        card.querySelectorAll(
            "[data-set-rep]"
        ).forEach((repInput,index) => {
            const label =
                repInput.closest(
                    ".wi-set-rep-field");

            if (!label) return;

            label.classList.add(
                "wi-b221-set-column");

            const caption =
                label.querySelector("span");

            if (caption) {
                caption.textContent =
                    `Set ${index + 1}`;
            }

            repInput.placeholder =
                "Reps";
        });

        const repInputs =
            [...card.querySelectorAll(
                "[data-set-rep]")];

        let loadInputs =
            [...card.querySelectorAll(
                "[data-set-load]")];

        while (loadInputs.length < repInputs.length) {
            const load =
                document.createElement("input");

            load.type = "number";
            load.min = "0";
            load.max = "10000";
            load.step = "1";
            load.inputMode = "decimal";
            load.placeholder = "Weight";
            load.dataset.field =
                `SetLoads[${loadInputs.length}]`;
            load.dataset.setLoad = "";

            const label =
                repInputs[loadInputs.length]
                    ?.closest(
                        ".wi-set-rep-field");

            label?.appendChild(load);
            loadInputs.push(load);
        }

        loadInputs.forEach((load,index) => {
            load.type = "number";
            load.min = "0";
            load.max = "10000";
            load.step = "1";
            load.inputMode = "decimal";
            load.placeholder = "Weight";
            load.dataset.field =
                `SetLoads[${index}]`;

            const label =
                repInputs[index]
                    ?.closest(
                        ".wi-set-rep-field");

            if (label
                && load.parentElement !== label) {
                label.appendChild(load);
            }
        });
    }

    function b221NormalizeAllCards() {
        if (!activeWorkoutIsActualSession()) {
            return;
        }

        root.querySelectorAll(
            "[data-exercise-card]"
        ).forEach(
            card =>
                b221NormalizeActiveCard(card));
    }

    function b221BindSearchClose() {
        const searchResults =
            document.getElementById(
                "wi-search-results");

        const searchInput =
            document.getElementById(
                "wi-search-input");

        if (searchInput
            && searchInput.dataset.b221InputBound
                !== "true") {
            searchInput.dataset.b221InputBound =
                "true";

            searchInput.addEventListener(
                "input",
                b221PrepareSearchForTyping);
        }

        if (!searchResults
            || searchResults.dataset.b221CloseBound
                === "true") {
            return;
        }

        searchResults.dataset.b221CloseBound =
            "true";

        searchResults.addEventListener(
            "click",
            event => {
                if (event.target.closest(
                        "[data-favorite-button]")
                    || event.target.closest(
                        "[data-technique-button]")) {
                    return;
                }

                const row =
                    event.target.closest(
                        ".wi-search-result");

                if (!row) return;

                const beforeCount =
                    root.querySelectorAll(
                        "[data-exercise-card]")
                        .length;

                window.setTimeout(
                    () => {
                        const afterCount =
                            root.querySelectorAll(
                                "[data-exercise-card]")
                                .length;

                        b221NormalizeAllCards();

                        b221CloseExercisePicker(
                            afterCount > beforeCount
                                ? "Exercise added."
                                : "Selection closed.");

                        ensureInsertionLinks();
                    },
                    0);
            });

        searchResults.addEventListener(
            "keydown",
            event => {
                if (event.key !== "Enter"
                    && event.key !== " ") {
                    return;
                }

                const row =
                    event.target.closest(
                        ".wi-search-result");

                if (!row) return;

                window.setTimeout(
                    () => {
                        b221NormalizeAllCards();
                        b221CloseExercisePicker(
                            "Exercise added.");
                        ensureInsertionLinks();
                    },
                    0);
            });
    }

    window.setTimeout(
        () => {
            b221NormalizeAllCards();
            b221BindSearchClose();
        },
        0);

    document.addEventListener(
        "click",
        event => {
            if (event.target.closest(
                    "[data-insert-exercise-here]")) {
                window.setTimeout(
                    b221BindSearchClose,
                    50);
            }
        });

    // ===== END B2.21 ACTIVE WORKOUT POPUP CLEAN REBUILD =====

    // ================================================================
    // B2.22 PICTURE-DRIVEN ACTIVE WORKOUT CARD LAYOUT
    // ================================================================

    function b222ApplyPictureLayoutLabels() {
        if (!activeWorkoutIsActualSession()) {
            return;
        }

        root.querySelectorAll(
            "[data-exercise-card]"
        ).forEach(card => {
            card.classList.add(
                "wi-b222-card");

            const name =
                card.querySelector(
                    ".wi-workout-exercise-name");

            if (name) {
                name.classList.add(
                    "wi-b222-name");
            }

            const controls =
                card.querySelector(
                    ".wi-active-order-controls");

            if (controls) {
                controls.classList.add(
                    "wi-b222-controls");
            }

            const sets =
                card.querySelector(
                    ".wi-sets-field");

            if (sets) {
                sets.classList.add(
                    "wi-b222-sets");
            }

            const setGrid =
                card.querySelector(
                    "[data-set-reps]");

            if (setGrid) {
                setGrid.classList.add(
                    "wi-b222-set-grid");
            }

            card.querySelectorAll(
                ".wi-set-rep-field"
            ).forEach((label,index) => {
                label.classList.add(
                    "wi-b222-set-column");

                const caption =
                    label.querySelector("span");

                if (caption) {
                    caption.textContent =
                        `Reps ${index + 1}`;
                }

                const rep =
                    label.querySelector(
                        "[data-set-rep]");

                if (rep) {
                    rep.placeholder =
                        "Reps";
                }

                const load =
                    label.querySelector(
                        "[data-set-load]");

                if (load) {
                    load.placeholder =
                        "Wt";
                }
            });
        });
    }

    window.setTimeout(
        b222ApplyPictureLayoutLabels,
        0);

    document.addEventListener(
        "click",
        event => {
            if (event.target.closest(
                    "[data-insert-exercise-here]")
                || event.target.closest(
                    ".wi-search-result")
                || event.target.closest(
                    "[data-exercise-up]")
                || event.target.closest(
                    "[data-exercise-down]")) {
                window.setTimeout(
                    b222ApplyPictureLayoutLabels,
                    0);
            }
        });

    // ===== END B2.22 PICTURE-DRIVEN ACTIVE WORKOUT CARD LAYOUT =====

    // ================================================================
    // B2.23 COMPACT HORIZONTAL ACTIVE WORKOUT CARD
    // ================================================================

    function b223NormalizeCard(card) {
        if (!card) return;

        card.classList.add(
            "wi-b223-card");

        const name =
            card.querySelector(
                ".wi-workout-exercise-name");

        if (name) {
            name.classList.add(
                "wi-b223-name");
        }

        const controls =
            card.querySelector(
                ".wi-active-order-controls");

        if (controls) {
            controls.classList.add(
                "wi-b223-controls");
        }

        const sets =
            card.querySelector(
                ".wi-sets-field");

        if (sets) {
            sets.classList.add(
                "wi-b223-sets");
        }

        const setGrid =
            card.querySelector(
                "[data-set-reps]");

        if (setGrid) {
            setGrid.classList.add(
                "wi-b223-set-grid");
        }

        card.querySelectorAll(
            ".wi-set-rep-field"
        ).forEach((label,index) => {
            label.classList.add(
                "wi-b223-set-column");

            const caption =
                label.querySelector("span");

            if (caption) {
                caption.textContent =
                    `Reps ${index + 1}`;
            }

            const rep =
                label.querySelector(
                    "[data-set-rep]");

            if (rep) {
                rep.placeholder = "";
                rep.inputMode = "numeric";
            }

            const load =
                label.querySelector(
                    "[data-set-load]");

            if (load) {
                load.placeholder = "Wt";
                load.inputMode = "decimal";
            }
        });
    }

    function b223NormalizeAllCards() {
        if (!activeWorkoutIsActualSession()) {
            return;
        }

        root.querySelectorAll(
            "[data-exercise-card]"
        ).forEach(
            card =>
                b223NormalizeCard(card));
    }

    function b223CloseSearchAfterSelection() {
        const searchRoot =
            document.getElementById(
                "wi-exercise-search");

        const searchInput =
            document.getElementById(
                "wi-search-input");

        const searchResults =
            document.getElementById(
                "wi-search-results");

        if (searchResults) {
            searchResults.replaceChildren();
            searchResults.hidden = true;
            searchResults.style.display = "none";
        }

        if (searchInput) {
            searchInput.value = "";
            searchInput.blur();
        }

        if (searchRoot) {
            searchRoot.classList.remove(
                "wi-active-exercise-picker-open");
        }
    }

    function b223BindSearchSelectionClose() {
        const searchResults =
            document.getElementById(
                "wi-search-results");

        if (!searchResults
            || searchResults.dataset.b223Bound
                === "true") {
            return;
        }

        searchResults.dataset.b223Bound =
            "true";

        searchResults.addEventListener(
            "click",
            event => {
                if (event.target.closest(
                        "[data-favorite-button]")
                    || event.target.closest(
                        "[data-technique-button]")) {
                    return;
                }

                const row =
                    event.target.closest(
                        ".wi-search-result");

                if (!row) return;

                // Let the existing addExercise handler run first.
                window.setTimeout(
                    () => {
                        b223NormalizeAllCards();
                        b223CloseSearchAfterSelection();
                        ensureInsertionLinks();
                    },
                    25);
            },
            true);

        searchResults.addEventListener(
            "keydown",
            event => {
                if (event.key !== "Enter"
                    && event.key !== " ") {
                    return;
                }

                const row =
                    event.target.closest(
                        ".wi-search-result");

                if (!row) return;

                window.setTimeout(
                    () => {
                        b223NormalizeAllCards();
                        b223CloseSearchAfterSelection();
                        ensureInsertionLinks();
                    },
                    25);
            },
            true);
    }

    const b223SearchInput =
        document.getElementById(
            "wi-search-input");

    if (b223SearchInput
        && b223SearchInput.dataset.b223InputBound
            !== "true") {
        b223SearchInput.dataset.b223InputBound =
            "true";

        b223SearchInput.addEventListener(
            "input",
            () => {
                const results =
                    document.getElementById(
                        "wi-search-results");

                if (results) {
                    results.style.removeProperty(
                        "display");
                }
            });
    }

    window.setTimeout(
        () => {
            b223NormalizeAllCards();
            b223BindSearchSelectionClose();
        },
        0);

    document.addEventListener(
        "click",
        event => {
            if (event.target.closest(
                    "[data-insert-exercise-here]")
                || event.target.closest(
                    "[data-exercise-up]")
                || event.target.closest(
                    "[data-exercise-down]")
                || event.target.closest(
                    "[data-remove-exercise]")) {
                window.setTimeout(
                    () => {
                        b223NormalizeAllCards();
                        b223BindSearchSelectionClose();
                    },
                    25);
            }
        });

    // ===== END B2.23 COMPACT HORIZONTAL ACTIVE WORKOUT CARD =====

    // ================================================================
    // B2.24 EXERCISE SELECTION FIX
    // Search result selection uses b224AddExercise directly.
    // ================================================================

    // ================================================================
    // B2.25 SINGLE BOTTOM ADD
    // Keep only the final insertion row produced by ensureInsertionLinks.
    // ================================================================

    function b225KeepOnlyBottomAdd() {
        if (!activeWorkoutIsActualSession()) {
            return;
        }

        const rows =
            [...root.querySelectorAll(
                ".wi-insert-exercise-row")];

        if (rows.length <= 1) {
            return;
        }

        const bottomRow =
            rows[rows.length - 1];

        rows.forEach(row => {
            if (row !== bottomRow) {
                row.remove();
            }
        });

        const bottomButton =
            bottomRow.querySelector(
                "[data-insert-exercise-here]");

        if (bottomButton) {
            bottomButton.textContent =
                "+ Add exercise";
        }
    }

    const b225OriginalEnsureInsertionLinks =
        ensureInsertionLinks;

    ensureInsertionLinks = function() {
        b225OriginalEnsureInsertionLinks();
        b225KeepOnlyBottomAdd();
    };

    window.setTimeout(
        () => {
            ensureInsertionLinks();
            b225KeepOnlyBottomAdd();
        },
        0);

    document.addEventListener(
        "click",
        event => {
            if (event.target.closest(
                    ".wi-search-result")
                || event.target.closest(
                    "[data-remove-exercise]")
                || event.target.closest(
                    "[data-exercise-up]")
                || event.target.closest(
                    "[data-exercise-down]")) {
                window.setTimeout(
                    b225KeepOnlyBottomAdd,
                    50);
            }
        });

    // ===== END B2.25 SINGLE BOTTOM ADD =====

    // ================================================================
    // B2.26 SIMPLE ADD EXERCISE
    // One bottom Add button -> select result -> append card -> close search.
    // ================================================================
})();
