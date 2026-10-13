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
        let nextOffset = 0, loading = false, loaded = false, entries = [];
        const addText = (parent, tag, text, className) => {
            const node = document.createElement(tag); node.textContent = text;
            if (className) node.className = className;
            parent.appendChild(node); return node;
        };
        const renderHistory = () => {
            panel.querySelector('.wi-history-table-scroll')?.remove();
            if (!entries.length) return;
            const dates = new Map();
            for (const entry of entries) {
                if (!dates.has(entry.date)) dates.set(entry.date, []);
                dates.get(entry.date).push(entry);
            }
            const rows = Array.from(dates, ([date, sessions]) => ({date, sessions, sets: sessions.flatMap(session=>session.sets)}));
            const columns = Math.max(4, ...rows.map(row=>row.sets.length));
            const table = document.createElement('table'); table.className='wi-history-table';
            const thead = document.createElement('thead'), head = document.createElement('tr');
            for (const text of ['Date', ...Array.from({length:columns},(_,i)=>'Reps '+(i+1)), 'Weight', 'Notes']) {
                const th=addText(head,'th',text); th.scope='col';
            }
            thead.appendChild(head); table.appendChild(thead);
            const tbody=document.createElement('tbody');
            for (const row of rows) {
                const tr=document.createElement('tr');
                const date=new Date(row.date+'T00:00:00').toLocaleDateString(undefined,{month:'2-digit',day:'2-digit',year:'2-digit'});
                const day=addText(tr,'td',date);day.title=row.sessions.map(session=>session.workout).join(' | ');
                for(let i=0;i<columns;i++) {
                    const set=row.sets[i];
                    const text=!set?'':set.durationSeconds!=null?set.durationSeconds+'s':set.reps==null?'—':String(set.reps);
                    const cell=addText(tr,'td',text+(set?.warmup?' W':''));
                    if(set?.warmup) cell.title='Warm-up set';
                }
                const recorded=row.sessions.filter(session=>session.recordedMaxWeight!=null);
                const perSet=row.sets.filter(set=>set.weight!=null).map(set=>set.weight+' '+set.unit.toLowerCase());
                const weights=recorded.length?recorded.map(session=>session.recordedMaxWeight+' '+session.unit.toLowerCase()):perSet;
                const weight=addText(tr,'td',Array.from(new Set(weights)).join(' / ')||'—');
                weight.title=(recorded.length?'Recorded heaviest weight. ':'')+(perSet.length?'Set weights: '+perSet.join(', '):'Individual set weights were not recorded.');
                const notes=Array.from(new Set(row.sessions.map(session=>session.notes).filter(Boolean)));
                row.sets.forEach((set,i)=>{if(set.notes)notes.push('Set '+(i+1)+': '+set.notes);});
                const note=addText(tr,'td',notes.join(' | ')||'—','wi-history-note-cell');note.title=notes.join('\n');
                tbody.appendChild(tr);
            }
            table.appendChild(tbody);
            const wrap=document.createElement('div');wrap.className='wi-history-table-scroll';wrap.appendChild(table);panel.appendChild(wrap);
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
                entries.push(...data.entries);
                renderHistory();
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
            if (panel.dataset.exerciseId !== id && !loading) { panel.replaceChildren(); panel.dataset.exerciseId=id; nextOffset=0; loaded=false; entries=[]; }
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
