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

                label.appendChild(caption);
                label.appendChild(input);
                group.appendChild(label);
            });

        if (hiddenReps) {
            hiddenReps.value =
                values[0] || fallback;
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
                "Weight",
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

                moveButton.textContent = "Move";

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
                            '[name="stopWorkout"]'));
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
                '[name="stopWorkout"]');

        if (complete) {
            complete.textContent =
                "Stop Workout";

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
        const selector =
            `[data-exercise-card][data-exercise-id="${CSS.escape(item.exerciseId)}"]`;

        const existing =
            root.querySelector(selector);

        if (existing) {
            if (searchStatus) {
                searchStatus.textContent =
                    `${item.name} is already in this workout.`;
            }

            return;
        }

        root.appendChild(
            createExerciseCard(item));

        renumber();

        if (searchStatus) {
            searchStatus.textContent =
                `${item.name} added. Results stay open so you can add more exercises from this search.`;
        }
    }

    root.addEventListener(
        "click",
        event => {

            const technique =
                event.target.closest(
                    "[data-technique-exercise-id]");

            if (technique) {
                event.preventDefault();

                openTechnique(
                    technique.dataset.techniqueExerciseId,
                    technique.dataset.techniqueExerciseName
                    || "Technique");

                return;
            }

            const remove =
                event.target.closest(
                    "[data-remove-exercise]");

            if (!remove) {
                return;
            }

            remove.closest(
                "[data-exercise-card]"
            )?.remove();

            renumber();
        });

    function renderResults(items) {
        if (!searchResults) {
            return;
        }

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

                    addExercise(item);
                });

            row.addEventListener(
                "keydown",
                event => {
                    if (event.key === "Enter"
                        || event.key === " ") {

                        event.preventDefault();
                        addExercise(item);
                    }
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
})();
