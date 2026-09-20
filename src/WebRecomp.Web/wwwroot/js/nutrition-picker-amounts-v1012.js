(() => {
    "use strict";

    const path = location.pathname.toLowerCase();

    if (!path.startsWith("/nutrition")) {
        return;
    }

    const initialize = () => {
        const dialog =
            document.getElementById(
                "nutrition-food-picker-dialog");

        if (!(dialog instanceof HTMLDialogElement)) {
            return;
        }

        const dataUrl =
            dialog.dataset.pickerDataUrl;

        const foodsHost =
            dialog.querySelector(
                "[data-picker-foods]");

        const addButton =
            dialog.querySelector(
                "[data-picker-add]");

        const amountInput =
            dialog.querySelector(
                "[data-picker-amount]");

        const amountLabel =
            dialog.querySelector(
                "[data-picker-amount-label]");

        const providerControls =
            dialog.querySelector(
                "[data-picker-provider-controls]");

        const measureField =
            dialog.querySelector(
                ".nutrition-picker-measure-field");

        const amountPresetField =
            dialog.querySelector(
                "[data-picker-amount-preset-field]");

        const amountPresets =
            dialog.querySelector(
                "[data-picker-amount-presets]");

        const amountHint =
            dialog.querySelector(
                "[data-picker-amount-hint]");

        const changeButton =
            dialog.querySelector(
                "[data-picker-change]");

        const mealSelect =
            dialog.querySelector(
                "[data-picker-meal]");

        const sourcePastFoodForm =
            dialog.querySelector(
                "[data-picker-past-food-form]");

        if (!dataUrl
            || !foodsHost
            || !addButton
            || !amountInput
            || !mealSelect
            || !sourcePastFoodForm) {
            return;
        }

        // The picker already supports typed amounts for provider and custom foods.
        // Keep that input fractional for every food/measure, then extend the same
        // behavior to frequently-used past foods.
        amountInput.step = "0.01";
        amountInput.min = "0.01";
        amountInput.max = "1000";

        let latestFoods = [];
        let activePastFood = null;

        const originalFetch =
            window.fetch.bind(window);

        window.fetch = async (...args) => {
            const response =
                await originalFetch(...args);

            try {
                const requestUrl =
                    typeof args[0] === "string"
                        ? args[0]
                        : args[0] instanceof Request
                            ? args[0].url
                            : String(args[0] || "");

                const requestedPath =
                    new URL(
                        requestUrl,
                        window.location.origin)
                        .pathname
                        .toLowerCase();

                const pickerPath =
                    new URL(
                        dataUrl,
                        window.location.origin)
                        .pathname
                        .toLowerCase();

                if (requestedPath === pickerPath) {
                    response
                        .clone()
                        .json()
                        .then(payload => {
                            latestFoods =
                                payload?.success === true
                                && Array.isArray(payload.foods)
                                    ? payload.foods
                                    : [];
                        })
                        .catch(() => {
                            latestFoods = [];
                        });
                }
            }
            catch {
                // Never interfere with the picker's normal request path.
            }

            return response;
        };

        const foodForButton = button => {
            const buttons =
                Array.from(
                    foodsHost.querySelectorAll(
                        ".nutrition-picker-choice"));

            const index =
                buttons.indexOf(button);

            return index >= 0
                ? latestFoods[index] ?? null
                : null;
        };

        const clearPastFoodOverride = () => {
            activePastFood = null;
        };

        const enhancePastFoodSelection = button => {
            const food =
                foodForButton(button);

            if (!food || food.kind !== "pastFood") {
                clearPastFoodOverride();
                return;
            }

            activePastFood = food;

            if (providerControls) {
                providerControls.hidden = false;
            }

            if (measureField) {
                measureField.hidden = true;
            }

            if (amountPresetField) {
                amountPresetField.hidden = true;
            }

            if (amountPresets) {
                amountPresets.hidden = true;
            }

            if (amountHint) {
                amountHint.hidden = false;
                amountHint.textContent =
                    "Type any amount you want to add.";
            }

            const unit =
                (food.quantityUnitCode || "serving")
                    .toString()
                    .trim();

            if (amountLabel) {
                amountLabel.textContent =
                    `Amount (${unit})`;
            }

            const quantity =
                Number.parseFloat(food.quantity);

            amountInput.value =
                Number.isFinite(quantity)
                && quantity > 0
                    ? String(quantity)
                    : "1";

            amountInput.focus();
            amountInput.select();
        };

        foodsHost.addEventListener(
            "click",
            event => {
                const button =
                    event.target instanceof Element
                        ? event.target.closest(
                            ".nutrition-picker-choice")
                        : null;

                if (!button || !foodsHost.contains(button)) {
                    return;
                }

                window.setTimeout(
                    () => enhancePastFoodSelection(button),
                    0);
            });

        changeButton?.addEventListener(
            "click",
            clearPastFoodOverride);

        mealSelect.addEventListener(
            "change",
            clearPastFoodOverride);

        dialog.addEventListener(
            "close",
            clearPastFoodOverride);

        const appendHidden = (
            form,
            name,
            value) => {
            const input =
                document.createElement("input");

            input.type = "hidden";
            input.name = name;
            input.value = value;
            form.appendChild(input);
        };

        addButton.addEventListener(
            "click",
            event => {
                if (!activePastFood) {
                    return;
                }

                const selectedButton =
                    foodsHost.querySelector(
                        ".nutrition-picker-choice.is-selected");

                if (!selectedButton) {
                    clearPastFoodOverride();
                    return;
                }

                const currentFood =
                    foodForButton(selectedButton);

                if (!currentFood
                    || currentFood.kind !== "pastFood"
                    || currentFood.sourceDailyLogItemId
                        !== activePastFood.sourceDailyLogItemId) {
                    clearPastFoodOverride();
                    return;
                }

                const quantity =
                    Number.parseFloat(
                        amountInput.value);

                if (!Number.isFinite(quantity)
                    || quantity <= 0
                    || quantity > 1000) {
                    event.preventDefault();
                    event.stopImmediatePropagation();

                    amountInput.setCustomValidity(
                        "Enter an amount greater than 0 and no more than 1000.");
                    amountInput.reportValidity();
                    amountInput.focus();
                    return;
                }

                amountInput.setCustomValidity("");

                event.preventDefault();
                event.stopImmediatePropagation();
                addButton.disabled = true;

                const form =
                    document.createElement("form");

                form.method = "post";
                form.action =
                    "/NutritionAmount/AddPastFood";
                form.hidden = true;

                const token =
                    sourcePastFoodForm.querySelector(
                        'input[name="__RequestVerificationToken"]')
                        ?.value;

                if (token) {
                    appendHidden(
                        form,
                        "__RequestVerificationToken",
                        token);
                }

                appendHidden(
                    form,
                    "date",
                    dialog.dataset.pickerDate || "");

                appendHidden(
                    form,
                    "sourceDailyLogItemId",
                    currentFood.sourceDailyLogItemId);

                appendHidden(
                    form,
                    "mealSlotCode",
                    mealSelect.value);

                appendHidden(
                    form,
                    "quantity",
                    String(quantity));

                document.body.appendChild(form);
                form.submit();
            },
            true);
    };

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize,
            { once: true });
    }
    else {
        initialize();
    }
})();
