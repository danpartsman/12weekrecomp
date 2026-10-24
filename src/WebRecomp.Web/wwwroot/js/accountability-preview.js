(() => {
    'use strict';
    const catalog = [
        ['nutrition', 'Follow prescribed macros', 'Meet the agreed daily protein, carb and fat ranges; weekly totals do not erase missed days.', 'incentive', 10, 'weekly'],
        ['training', 'Complete planned training', 'Complete the agreed sessions or coach-approved substitutions.', 'incentive', 10, 'weekly'],
        ['recovery', 'Honor recovery days', 'Follow prescribed rest or recovery; extra training earns no extra credit.', 'incentive', 5, 'weekly'],
        ['logging', 'Complete daily records', 'Provide usable food and training records by the agreed deadline.', 'incentive', 2, 'daily'],
        ['checkin', 'Submit weekly check-in', 'Complete the agreed weekly review and progress notes.', 'incentive', 5, 'weekly'],
        ['consistency', 'Maintain monthly consistency', 'Meet the coach-agreed adherence goal across the month.', 'incentive', 20, 'monthly'],
        ['missed-session', 'Unapproved missed training', 'Only after review confirms a missed prescribed session without an approved change.', 'fine', 5, 'daily'],
        ['missing-log', 'Unresolved missing records', 'Missing records stay pending until reviewed; never assume zero intake.', 'fine', 2, 'daily'],
        ['missed-review', 'Missed check-in or appointment', 'An agreed review missed without cancellation or an approved exception.', 'fine', 5, 'weekly'],
        ['macro-deviation', 'Repeated unapproved macro deviation', 'Use agreed tolerance bands and reviewed logs; illness and approved adjustments are excluded.', 'fine', 5, 'weekly']
    ].map(([id, title, description, type, amount, frequency]) => ({ id, title, description, type, amount, frequency }));
    const amounts = [0, 1, 2, 5, 10, 15, 20, 25, 30, 50, 75, 100];
    const frequencies = ['daily', 'weekly', 'monthly'];
    const defaults = () => ({ version: 1, enabled: false, rules: catalog.map(r => ({ id: r.id, assigned: false, type: r.type, amount: r.amount, frequency: r.frequency, min: 0, max: 100 })) });
    function valid(state) {
        return state?.version === 1 && typeof state.enabled === 'boolean' && Array.isArray(state.rules) && state.rules.length === catalog.length && catalog.every((rule, i) => {
            const r = state.rules[i];
            return r?.id === rule.id && typeof r.assigned === 'boolean' && ['fine', 'incentive'].includes(r.type) && amounts.includes(r.amount) && frequencies.includes(r.frequency) && [r.min, r.max].every(n => Number.isFinite(n) && n >= 0 && n <= 10000 && Math.abs(Math.round(n * 100) - n * 100) < 0.000001) && r.min <= r.max;
        });
    }
    // Export pure configuration checks for the standalone UI verification script.
    if (typeof module !== 'undefined' && module.exports) module.exports = { catalog, defaults, valid };
    if (typeof document === 'undefined') return;
    const money = n => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n);
    const el = (tag, text, cls) => { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; if (cls) node.className = cls; return node; };
    function initialize(root) {
        if (root.dataset.initialized) return;
        root.dataset.initialized = 'true';
        const key = `12wr:accountability-preview:v1:${root.dataset.clientId}`;
        const editable = root.dataset.editable === 'true';
        const form = root.querySelector('form');
        const status = root.querySelector('[data-preview-status]');
        let state = defaults();
        let storageNotice = '';
        function load() {
            try {
                const raw = localStorage.getItem(key);
                if (raw) { const parsed = JSON.parse(raw); if (valid(parsed)) state = parsed; else storageNotice = 'Stored preview was invalid. Reset or save a new configuration.'; }
            } catch { storageNotice = 'Browser storage is unavailable. You can try the controls, but settings cannot be retained.'; }
        }
        function select(name, values, value, label) {
            const node = el('select'); node.name = name; node.setAttribute('aria-label', label);
            values.forEach(v => { const option = el('option', name.endsWith('-amount') ? money(v) : v[0].toUpperCase() + v.slice(1)); option.value = v; node.append(option); });
            node.value = value; return node;
        }
        function drawEditor() {
            if (!form) return;
            form.elements.enabled.checked = state.enabled;
            const body = root.querySelector('[data-rule-editor]'); body.replaceChildren();
            catalog.forEach((rule, i) => {
                const r = state.rules[i], row = el('tr');
                const check = el('input'); check.type = 'checkbox'; check.name = rule.id + '-assigned'; check.checked = r.assigned; check.setAttribute('aria-label', `Assign ${rule.title}`);
                const title = el('div'); title.append(el('strong', rule.title), el('small', rule.description));
                const min = el('input'), max = el('input');
                [min, max].forEach((input, index) => { const field = index ? 'max' : 'min'; input.type = 'number'; input.name = rule.id + '-' + field; input.min = '0'; input.max = '10000'; input.step = '0.01'; input.required = true; input.value = r[field]; input.setAttribute('aria-label', `${rule.title}: ${index ? 'maximum' : 'minimum'} accumulated total in dollars`); });
                [check, title, select(rule.id + '-type', ['fine', 'incentive'], r.type, `${rule.title}: type`), select(rule.id + '-amount', amounts, r.amount, `${rule.title}: amount in dollars`), select(rule.id + '-frequency', frequencies, r.frequency, `${rule.title}: assessment frequency`), min, max].forEach(node => { const cell = el('td'); cell.append(node); row.append(cell); });
                body.append(row);
            });
        }
        function render() {
            const assigned = state.rules.filter(r => r.assigned);
            root.hidden = !editable && !state.enabled;
            root.querySelector('[data-agreement-state]').textContent = state.enabled ? 'Enabled · Preview only' : 'Not enabled';
            const panel = root.querySelector('[data-assigned-preview]'); panel.hidden = !state.enabled;
            root.querySelector('[data-assignment-summary]').textContent = assigned.length ? `${assigned.length} assigned rules · USD · Caps apply separately to each rule across the 12-week program.` : 'Enabled, with no rules assigned yet.';
            const list = root.querySelector('[data-assigned-rules]'); list.replaceChildren();
            assigned.forEach(r => {
                const rule = catalog.find(c => c.id === r.id), card = el('article', undefined, 'accountability-rule-card');
                card.append(el('strong', rule.title), el('p', `${r.type === 'fine' ? 'Fine' : 'Incentive'} · ${money(r.amount)} ${r.frequency}`, 'accountability-kind'), el('p', `Minimum to settle ${money(r.min)} · Program cap ${money(r.max)}`), el('small', rule.description)); list.append(card);
            });
        }
        load(); drawEditor(); render();
        if (status) status.textContent = storageNotice;
        if (form) {
            form.addEventListener('input', () => { status.textContent = 'Unsaved changes — save to update the displays.'; });
            form.addEventListener('submit', event => {
                event.preventDefault();
                form.querySelectorAll('[aria-invalid]').forEach(n => { n.removeAttribute('aria-invalid'); n.setCustomValidity(''); });
                const next = { version: 1, enabled: form.elements.enabled.checked, rules: catalog.map(rule => {
                    const get = suffix => form.elements[rule.id + '-' + suffix];
                    return { id: rule.id, assigned: get('assigned').checked, type: get('type').value, amount: Number(get('amount').value), frequency: get('frequency').value, min: Number(get('min').value), max: Number(get('max').value) };
                }) };
                for (const r of next.rules) {
                    if (r.min > r.max) {
                        const input = form.elements[r.id + '-max']; input.setAttribute('aria-invalid', 'true'); input.setCustomValidity('Maximum must be at least the minimum.'); input.reportValidity(); input.setCustomValidity(''); status.textContent = 'Please correct the minimum and maximum totals.'; return;
                    }
                }
                if (!form.reportValidity() || !valid(next)) { status.textContent = 'Enter valid dollar totals between $0 and $10,000, with up to two decimal places.'; return; }
                try { localStorage.setItem(key, JSON.stringify(next)); state = next; render(); status.textContent = 'Preview saved in this browser. Open this client’s summary or Command Center on the same browser to see it. No money has been assessed.'; }
                catch { status.textContent = 'Could not save: browser storage is unavailable. No changes were retained.'; }
            });
            root.querySelector('[data-reset-preview]').addEventListener('click', () => {
                try { localStorage.removeItem(key); state = defaults(); drawEditor(); render(); status.textContent = 'Preview reset. All rules are unassigned and the arrangement is off.'; }
                catch { status.textContent = 'Could not reset browser storage.'; }
            });
        }
        window.addEventListener('storage', event => { if (event.key === key || event.key === null) { state = defaults(); load(); drawEditor(); render(); if (status) status.textContent = storageNotice || 'Preview updated from another tab.'; } });
        window.addEventListener('pageshow', event => { if (event.persisted) { state = defaults(); load(); drawEditor(); render(); } });
    }
    document.querySelectorAll('[data-accountability]').forEach(initialize);
})();
