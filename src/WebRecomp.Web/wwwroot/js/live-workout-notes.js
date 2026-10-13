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
            document.createElement("div");

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

    function addHistory(card) {
        const notes = card.querySelector('[data-field="Notes"]');
        const field = notes?.closest('.wi-notes-field');
        if (!field || field.querySelector('[data-exercise-history-toggle]')) return;
        const row = document.createElement('div');
        row.className = 'wi-notes-input-row';
        notes.before(row); row.appendChild(notes);
        const toggle = document.createElement('button');
        toggle.type = 'button'; toggle.className = 'wi-history-toggle';
        toggle.dataset.exerciseHistoryToggle = '';
        toggle.textContent = '▾'; toggle.title = 'Exercise history';
        toggle.setAttribute('aria-label', 'Show exercise history');
        toggle.setAttribute('aria-expanded', 'false');
        const panel = document.createElement('section');
        panel.className = 'wi-exercise-history'; panel.hidden = true;
        panel.id = 'exercise-history-' + crypto.randomUUID();
        toggle.setAttribute('aria-controls', panel.id);
        row.appendChild(toggle); field.after(panel);
        let nextOffset = 0, loading = false, loaded = false;
        const addText = (parent, tag, text, className) => {
            const node = document.createElement(tag); node.textContent = text;
            if (className) node.className = className;
            parent.appendChild(node); return node;
        };
        const load = async () => {
            if (loading) return;
            loading = true;
            panel.querySelectorAll('.wi-history-status,[data-history-retry]').forEach(node=>node.remove());
            const status = addText(panel, 'p', 'Loading history…', 'wi-history-status');
            status.setAttribute('role', 'status');
            try {
                const params = new URLSearchParams({exerciseId: panel.dataset.exerciseId, offset: String(nextOffset)});
                const response = await fetch('/DetailedWorkout/ExerciseHistory?' + params, {credentials:'same-origin', headers:{Accept:'application/json'}});
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('History unavailable');
                const data = await response.json();
                if (!card.isConnected) return;
                status.remove();
                if (!loaded && data.entries.length === 0) addText(panel, 'p', 'No completed workouts recorded for this exercise yet.');
                for (const entry of data.entries) {
                    const block = addText(panel, 'article', '', 'wi-history-entry');
                    const date = new Date(entry.date + 'T00:00:00').toLocaleDateString(undefined,{year:'numeric',month:'short',day:'numeric'});
                    addText(block, 'strong', date + ' · ' + entry.workout);
                    addText(block, 'p', entry.sets.length + ' recorded sets' + (entry.recordedMaxWeight != null ? ' · recorded max: ' + entry.recordedMaxWeight + ' ' + entry.unit.toLowerCase() : ''));
                    if (entry.sets.length) {
                        const table = document.createElement('table'); table.className = 'wi-history-table';
                        const head = document.createElement('tr');
                        for (const text of ['Set','Reps / time','Weight','Notes']) { const th = addText(head,'th',text); th.scope='col'; }
                        const thead=document.createElement('thead'); thead.appendChild(head); table.appendChild(thead);
                        const tbody=document.createElement('tbody');
                        for (const set of entry.sets) {
                            const tr=document.createElement('tr');
                            addText(tr,'td',String(set.number) + (set.warmup?' · warm-up':''));
                            addText(tr,'td',set.durationSeconds != null ? set.durationSeconds + ' s' : set.reps == null ? '—' : String(set.reps));
                            addText(tr,'td',set.weight == null ? '—' : set.weight + ' ' + set.unit.toLowerCase());
                            addText(tr,'td',set.notes || '—'); tbody.appendChild(tr);
                        }
                        table.appendChild(tbody); const scroll=document.createElement('div'); scroll.className='wi-history-table-scroll'; scroll.appendChild(table); block.appendChild(scroll);
                    } else addText(block,'p','No individual set details were recorded.');
                    if (entry.notes) addText(block,'p',entry.notes,'wi-history-notes');
                }
                loaded = true; nextOffset = data.nextOffset;
                if (nextOffset != null) {
                    const more = addText(panel,'button','Earlier sessions'); more.type='button';
                    more.addEventListener('click', () => { more.remove(); void load(); });
                }
            } catch {
                status.textContent = 'Could not load history.';
                const retry = addText(panel,'button','Retry'); retry.type='button'; retry.dataset.historyRetry='';
                retry.addEventListener('click', () => { retry.remove(); status.remove(); void load(); });
            } finally { loading = false; }
        };
        toggle.addEventListener('click', () => {
            panel.hidden = !panel.hidden;
            toggle.textContent = panel.hidden ? '▾' : '▴';
            toggle.setAttribute('aria-expanded',String(!panel.hidden));
            toggle.setAttribute('aria-label',panel.hidden?'Show exercise history':'Hide exercise history');
            const id = card.dataset.exerciseId || card.querySelector('[data-field="ExerciseId"]')?.value;
            if (panel.dataset.exerciseId !== id && !loading) { panel.replaceChildren(); panel.dataset.exerciseId=id; nextOffset=0; loaded=false; }
            if (!panel.hidden && !loaded && !loading) void load();
        });
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
            card => { addNotesField(card); addHistory(card); });

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
