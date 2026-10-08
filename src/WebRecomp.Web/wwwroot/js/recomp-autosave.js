(() => {
    function bind(form, options = {}) {
        if (!form || form.dataset.autosaveBound === "true") return;
        form.dataset.autosaveBound = "true";

        const status = options.statusElement
            || (options.statusSelector ? document.querySelector(options.statusSelector) : null);
        const delay = options.delay ?? 650;
        const reloadAfterSaveMs = options.reloadAfterSaveMs ?? 0;

        let debounceTimer = null;
        let reloadTimer = null;
        let inFlight = false;
        let pending = false;
        let dirty = false;

        const setStatus = (kind, message) => {
            if (!status) return;
            status.dataset.state = kind;
            status.textContent = message;
            status.hidden = false;
        };

        const clearReload = () => {
            if (reloadTimer) {
                clearTimeout(reloadTimer);
                reloadTimer = null;
            }
        };

        async function save() {
            if (!dirty && !pending) return;

            if (inFlight) {
                pending = true;
                return;
            }

            clearTimeout(debounceTimer);
            debounceTimer = null;
            clearReload();

            inFlight = true;
            pending = false;
            dirty = false;
            setStatus("saving", "Saving…");

            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body: new FormData(form),
                    credentials: "same-origin",
                    keepalive: true,
                    headers: {
                        "X-Requested-With": "XMLHttpRequest",
                        "Accept": "application/json"
                    }
                });

                const contentType = response.headers.get("content-type") || "";
                const payload = contentType.includes("application/json")
                    ? await response.json()
                    : null;

                if (!response.ok || payload?.success === false) {
                    throw new Error(payload?.message || "Could not save this change.");
                }

                if (payload?.workoutSessionId) {
                    const idField = form.querySelector('[name="WorkoutSessionId"]');
                    if (idField) idField.value = payload.workoutSessionId;
                }

                setStatus("saved", payload?.message || "Saved automatically.");

                if (typeof options.afterSave === "function") {
                    try {
                        options.afterSave(payload || {});
                    }
                    catch (callbackError) {
                        // The server save already succeeded. A presentation
                        // callback must never mark the saved form dirty or
                        // cause an idempotency-sensitive record to be created
                        // again on the next autosave.
                        console.error(
                            "Autosave afterSave callback failed.",
                            callbackError);
                    }
                }

                if (reloadAfterSaveMs > 0) {
                    reloadTimer = setTimeout(() => {
                        if (!dirty && !inFlight && !pending) {
                            window.location.reload();
                        }
                    }, reloadAfterSaveMs);
                }
            }
            catch (error) {
                dirty = true;
                setStatus("error", error?.message || "Could not save. Try again.");
            }
            finally {
                inFlight = false;

                if (pending) {
                    dirty = true;
                    pending = false;
                    void save();
                }
            }
        }

        function schedule(immediate = false) {
            dirty = true;
            clearReload();
            setStatus("pending", "Unsaved change…");

            clearTimeout(debounceTimer);
            debounceTimer = null;

            if (immediate) {
                void save();
                return;
            }

            debounceTimer = setTimeout(() => void save(), delay);
        }

        const isEditable = target => {
            if (!(target instanceof HTMLElement)) return false;
            if (!target.matches("input, select, textarea")) return false;
            if (target.matches('[type="hidden"], [type="submit"], [type="button"]')) return false;
            return !target.hasAttribute("data-no-autosave");
        };

        form.addEventListener("input", event => {
            if (!isEditable(event.target)) return;
            schedule(false);
        });

        form.addEventListener("change", event => {
            if (!isEditable(event.target)) return;
            schedule(true);
        });

        form.addEventListener("focusout", event => {
            if (!isEditable(event.target) || !dirty) return;
            void save();
        });

        form.addEventListener("submit", event => {
            if (options.preventSubmit !== false) {
                event.preventDefault();
                dirty = true;
                void save();
            }
        });

        window.addEventListener("pagehide", () => {
            if (dirty && !inFlight) {
                void save();
            }
        });
    }

    window.RecompAutosave = { bind };
})();
