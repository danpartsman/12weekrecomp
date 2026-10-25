(() => {
    'use strict';
    const catalog = [
        ['calories', 'Calories', 'macro', 'calories', 'Daily calories compared with the assigned prescription.', 'fine', 2, 'daily', -5, 5],
        ['protein', 'Protein', 'macro', 'protein', 'Protein compared with the assigned prescription; no extra reward for overage.', 'fine', 2, 'daily', -5, 5],
        ['carbs', 'Carbs', 'macro', 'carbs', 'Carbohydrates compared with the workout-based prescription.', 'fine', 2, 'daily', -5, 5],
        ['fat', 'Fat', 'macro', 'fat', 'Fat compared with the assigned prescription.', 'fine', 2, 'daily', -5, 5],
        ['training', 'Completed training sessions', 'count', null, 'Reviewed completed or approved substitute sessions in the assessment period.', 'incentive', 10, 'weekly', 3, 5],
        ['recovery', 'Prescribed recovery days', 'count', null, 'Reviewed rest or recovery days completed as prescribed.', 'incentive', 5, 'weekly', 1, 3],
        ['logging', 'Complete daily records', 'count', null, 'Count one when the agreed daily record is complete.', 'incentive', 2, 'daily', 1, 1],
        ['checkin', 'Weekly check-ins', 'count', null, 'Reviewed check-ins submitted within the agreed deadline.', 'incentive', 5, 'weekly', 1, 1],
        ['cheat-foods', 'Cheat-food occurrences', 'count', null, 'Reviewed occurrences; two per day are allowed by default.', 'fine', 2, 'daily', 0, 2],
        ['missed-review', 'Unapproved missed appointments', 'count', null, 'Only reviewed, unapproved missed appointments; approved exceptions are excluded.', 'fine', 5, 'weekly', 0, 0]
    ].map(([id, title, kind, metric, description, type, amount, frequency, lower, upper]) => ({ id, title, kind, metric, description, type, amount, frequency, lower, upper }));
    const amounts = [0, 1, 2, 5, 10, 15, 20, 25, 30, 50, 75, 100];
    const frequencies = ['daily', 'weekly', 'monthly'];
    const methods = { 'bank-transfer': 'Bank transfer', 'card-external': 'Card through external provider', cash: 'Cash', check: 'Check', other: 'Other agreed method' };
    const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    const cents = n => Number.isFinite(n) && n >= 0 && n <= 10000 && Math.abs(Math.round(n * 100) - n * 100) < 0.000001;
    const defaults = () => ({ version: 2, enabled: false, rules: catalog.map(r => ({ id: r.id, assigned: false, type: r.type, amount: r.amount, frequency: r.frequency, lower: r.lower, upper: r.upper, min: 0, max: 100 })),
        payment: { method: 'bank-transfer', frequency: 'weekly', day: 'Monday', amount: 0, graceDays: 3, lateBasis: 'amount', lateValue: 0, lateFrequency: 'weekly', lateCap: 100, instructions: '' }, excused: {}, observations: {} });
    const datePattern = /^\d{4}-\d{2}-\d{2}$/;
    function valid(s) {
        if (s?.version !== 2 || typeof s.enabled !== 'boolean' || !Array.isArray(s.rules) || s.rules.length !== catalog.length) return false;
        if (!catalog.every((c, i) => { const r = s.rules[i]; return r?.id === c.id && typeof r.assigned === 'boolean' && ['fine', 'incentive'].includes(r.type) && amounts.includes(r.amount) && frequencies.includes(r.frequency)
            && cents(r.min) && cents(r.max) && r.min <= r.max && Number.isFinite(r.lower) && Number.isFinite(r.upper) && r.lower <= r.upper
            && (c.kind === 'macro' ? r.lower >= -100 && r.lower <= 0 && r.upper >= 0 && r.upper <= 500 : Number.isInteger(r.lower) && Number.isInteger(r.upper) && r.lower >= 0 && r.upper <= 1000); })) return false;
        const p = s.payment;
        if (!p || !Object.hasOwn(methods, p.method) || !frequencies.includes(p.frequency) || !weekdays.includes(p.day) || !cents(p.amount) || !Number.isInteger(p.graceDays) || p.graceDays < 0 || p.graceDays > 30
            || !['amount', 'percent'].includes(p.lateBasis) || !cents(p.lateValue) || (p.lateBasis === 'percent' && p.lateValue > 100) || !frequencies.includes(p.lateFrequency) || !cents(p.lateCap) || typeof p.instructions !== 'string' || p.instructions.length > 300) return false;
        if (!s.excused || typeof s.excused !== 'object' || Array.isArray(s.excused) || !s.observations || typeof s.observations !== 'object' || Array.isArray(s.observations)) return false;
        if (Object.keys(s.excused).length > 200 || Object.keys(s.observations).length > 200) return false;
        return Object.entries(s.excused).every(([d, v]) => datePattern.test(d) && typeof v === 'boolean') && Object.entries(s.observations).every(([d, values]) => datePattern.test(d) && values && typeof values === 'object' && !Array.isArray(values)
            && Object.entries(values).every(([id, n]) => catalog.some(c => c.id === id && c.kind === 'count') && Number.isInteger(n) && n >= 0 && n <= 1000));
    }
    const terms = s => JSON.stringify({ version: 2, enabled: s.enabled, rules: s.rules, payment: s.payment, excusedPolicy: 'Coach-approved dated excuses waive adherence assessments and incentives, but not contract payments. Missing evidence remains pending. No extra reward for extra training or under-eating.' });
    const known = n => typeof n === 'number' && Number.isFinite(n);
    function outcome(rule, days, s) {
        if (!days.length) return { status: 'excused', reason: 'All dates excused' };
        const c = catalog.find(c => c.id === rule.id);
        let value, target = null;
        if (c.kind === 'macro') {
            if (days.some(d => !known(d.actual?.[c.metric]) || !known(d.target?.[c.metric]) || d.target[c.metric] <= 0)) return { status: 'pending', reason: 'Missing logged values or prescribed targets' };
            value = days.reduce((n, d) => n + d.actual[c.metric], 0); target = days.reduce((n, d) => n + d.target[c.metric], 0);
            const lower = target * (1 + rule.lower / 100), upper = target * (1 + rule.upper / 100);
            return { status: value < lower - 1e-7 || value > upper + 1e-7 ? 'outside' : 'within', value, target, lower, upper };
        }
        if (days.some(d => !known(s.observations[d.date]?.[rule.id]))) return { status: 'pending', reason: 'Reviewed count not entered' };
        value = days.reduce((n, d) => n + s.observations[d.date][rule.id], 0);
        return { status: value < rule.lower || value > rule.upper ? 'outside' : 'within', value, lower: rule.lower, upper: rule.upper };
    }
    function assess(s, evidence) {
        if (!s.enabled || !evidence?.days?.length) return [];
        const result = [];
        for (const rule of s.rules.filter(r => r.assigned)) {
            const title = catalog.find(c => c.id === rule.id).title;
            let groups;
            if (rule.frequency === 'monthly') { result.push({ id: rule.id, title, status: 'pending', reason: 'Monthly assessment needs a full month; this sample contains one week', amount: 0, type: rule.type }); continue; }
            if (rule.frequency === 'weekly') groups = [evidence.days]; else groups = evidence.days.map(d => [d]);
            let accumulated = 0;
            for (const group of groups) {
                const label = group.length === 1 ? group[0].date : `${group[0].date}–${group.at(-1).date}`;
                if (group.some(d => !d.completed)) { result.push({ id: rule.id, title, label, status: 'pending', reason: 'Assessment period is not complete', amount: 0, type: rule.type }); continue; }
                const included = group.filter(d => !s.excused[d.date]);
                const o = outcome(rule, included, s);
                const applies = rule.type === 'fine' ? o.status === 'outside' : o.status === 'within';
                const amount = applies ? Math.min(rule.amount, Math.max(0, rule.max - accumulated)) : 0;
                accumulated += amount;
                result.push({ ...o, id: rule.id, title, label, amount, type: rule.type, applies, minimum: rule.min, cap: rule.max });
            }
            result.filter(x => x.id === rule.id).forEach(x => { x.settle = accumulated >= rule.min; });
        }
        return result;
    }
    function lateFee(payment, principal, overdue) {
        if (!cents(principal) || !Number.isInteger(overdue) || overdue < 0 || overdue > 365) return null;
        const elapsed = Math.max(0, overdue - payment.graceDays), size = payment.lateFrequency === 'daily' ? 1 : payment.lateFrequency === 'weekly' ? 7 : 30;
        const periods = Math.floor(elapsed / size), per = payment.lateBasis === 'percent' ? principal * payment.lateValue / 100 : payment.lateValue;
        return { periods, fee: principal === 0 ? 0 : Math.round(Math.min(payment.lateCap, per * periods) * 100) / 100 };
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { catalog, defaults, valid, terms, outcome, assess, lateFee };
    if (typeof document === 'undefined') return;
    const money = n => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n);
    const number = n => known(n) ? new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 }).format(n) : '—';
    const dayLabel = d => new Date(d + 'T12:00:00Z').toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' });
    const el = (tag, text, cls) => { const n = document.createElement(tag); if (text !== undefined) n.textContent = text; if (cls) n.className = cls; return n; };
    function initialize(root) {
        if (root.dataset.initialized) return; root.dataset.initialized = 'true';
        const key = `12wr:accountability-preview:v2:${root.dataset.clientId}`, receiptKey = key + ':approval';
        const api = `/accountability-preview/clients/${encodeURIComponent(root.dataset.clientId)}`;
        const editable = root.dataset.editable === 'true', reviewPage = root.dataset.reviewPage === 'true';
        const form = root.querySelector('[data-accountability-form]'), status = root.querySelector('[data-preview-status]');
        const query = s => root.querySelector(s), token = () => query('input[name="__RequestVerificationToken"]').value;
        let state = defaults(), evidence = null, savedReceipt = null, requestVersion = 0;
        function notice(text) { if (status) status.textContent = text; }
        function load() {
            state = defaults(); savedReceipt = null;
            try {
                const raw = localStorage.getItem(key); if (raw) { const parsed = JSON.parse(raw); if (valid(parsed)) state = parsed; else notice('Stored settings were invalid. Review and save a new preview.'); }
                const receipt = localStorage.getItem(receiptKey); if (receipt) { const parsed = JSON.parse(receipt); if (typeof parsed.token === 'string' && typeof parsed.terms === 'string') savedReceipt = parsed; }
            } catch { notice('Browser storage is unavailable. Settings and approval cannot be retained.'); }
        }
        function input(name, value, min, max, step, label) {
            const n = el('input'); Object.assign(n, { type: 'number', name, value, min, max, step, required: true }); n.setAttribute('aria-label', label); return n;
        }
        function select(name, values, value, label) {
            const n = el('select'); n.name = name; n.setAttribute('aria-label', label);
            values.forEach(v => { const option = el('option', name.endsWith('-amount') ? money(v) : v[0].toUpperCase() + v.slice(1)); option.value = v; n.append(option); }); n.value = value; return n;
        }
        const paymentNames = { paymentMethod: 'method', paymentFrequency: 'frequency', paymentDay: 'day', paymentAmount: 'amount', graceDays: 'graceDays', lateBasis: 'lateBasis', lateValue: 'lateValue', lateFrequency: 'lateFrequency', lateCap: 'lateCap', paymentInstructions: 'instructions' };
        function drawEditor() {
            if (!form) return;
            form.elements.enabled.checked = state.enabled;
            const body = query('[data-rule-editor]'); body.replaceChildren();
            catalog.forEach((c, i) => {
                const r = state.rules[i], row = el('tr'), check = el('input'); check.type = 'checkbox'; check.name = c.id + '-assigned'; check.checked = r.assigned; check.setAttribute('aria-label', `Assign ${c.title}`);
                const title = el('div'); title.append(el('strong', c.title), el('small', c.description), el('small', c.kind === 'macro' ? 'Criteria unit: % from prescription' : 'Criteria unit: occurrence count'));
                const nodes = [check, title, select(c.id + '-type', ['fine', 'incentive'], r.type, `${c.title}: type`), select(c.id + '-amount', amounts, r.amount, `${c.title}: dollar amount`), select(c.id + '-frequency', frequencies, r.frequency, `${c.title}: assessment frequency`),
                    input(c.id + '-lower', r.lower, c.kind === 'macro' ? -100 : 0, c.kind === 'macro' ? 0 : 1000, c.kind === 'macro' ? '.1' : '1', `${c.title}: lower ${c.kind === 'macro' ? 'tolerance percent' : 'count'}`),
                    input(c.id + '-upper', r.upper, 0, c.kind === 'macro' ? 500 : 1000, c.kind === 'macro' ? '.1' : '1', `${c.title}: upper ${c.kind === 'macro' ? 'tolerance percent' : 'count'}`),
                    input(c.id + '-min', r.min, 0, 10000, '.01', `${c.title}: minimum accumulated dollars`), input(c.id + '-max', r.max, 0, 10000, '.01', `${c.title}: maximum accumulated dollars`)];
                nodes.forEach(node => { const td = el('td'); td.append(node); row.append(td); }); body.append(row);
            });
            Object.entries(paymentNames).forEach(([field, prop]) => { form.elements[field].value = state.payment[prop]; });
        }
        function paymentText() {
            const p = state.payment, due = p.frequency === 'daily' ? 'each day' : p.frequency === 'weekly' ? `each ${p.day}` : `the first ${p.day} of each month`;
            const late = p.lateBasis === 'amount' ? money(p.lateValue) : `${number(p.lateValue)}% of unpaid principal`;
            return `${methods[p.method]} · ${money(p.amount)} ${p.frequency}, due ${due}. ${p.instructions ? 'Instructions: ' + p.instructions + '. ' : ''}Grace period: ${p.graceDays} days. Late charge: ${late} per full ${p.lateFrequency === 'monthly' ? '30-day' : p.lateFrequency === 'weekly' ? '7-day' : '1-day'} period after grace, capped at ${money(p.lateCap)}, without compounding. Excused dates do not waive contract payments.`;
        }
        function render() {
            const assigned = state.rules.filter(r => r.assigned);
            root.hidden = !editable && !reviewPage && !state.enabled;
            query('[data-agreement-state]').textContent = state.enabled ? 'Enabled · Preview only' : 'Not enabled';
            query('[data-assigned-preview]').hidden = !state.enabled;
            query('[data-no-agreement]').hidden = state.enabled || editable;
            query('[data-assignment-summary]').textContent = `${assigned.length} assigned rules · USD · Lower and upper endpoints are included. Weekly macro checks compare summed actual intake with summed prescribed targets for non-excused days.`;
            const list = query('[data-assigned-rules]'); list.replaceChildren();
            assigned.forEach(r => {
                const c = catalog.find(c => c.id === r.id), card = el('article', undefined, 'accountability-rule-card');
                const criterion = c.kind === 'macro' ? `Allowed deviation ${r.lower}% to +${r.upper}% (${100 + r.lower}%–${100 + r.upper}% of target)` : `${r.lower}–${r.upper} occurrences per ${r.frequency} assessment`;
                card.append(el('strong', c.title), el('p', `${r.type === 'fine' ? 'Fine' : 'Incentive'} · ${money(r.amount)} ${r.frequency}`, 'accountability-kind'), el('p', criterion), el('p', `Minimum to settle ${money(r.min)} · Program cap ${money(r.max)}`), el('small', c.description)); list.append(card);
            });
            query('[data-payment-summary]').textContent = paymentText();
            renderAssessments(); renderLateFee(); checkApproval();
        }
        function renderAssessments() {
            const ledger = query('[data-assessment-ledger]'); ledger.replaceChildren();
            if (!evidence) { query('[data-macro-evidence]').replaceChildren(); query('[data-assessment-total]').textContent = 'Awaiting evidence; no assessment can be calculated.'; return; }
            const rows = assess(state, evidence);
            const fine = rows.filter(x => x.type === 'fine').reduce((n, x) => n + x.amount, 0), incentive = rows.filter(x => x.type === 'incentive').reduce((n, x) => n + x.amount, 0), pending = rows.filter(x => x.status === 'pending').length;
            query('[data-assessment-total]').textContent = `Selected-week preview: fines ${money(fine)} · incentives ${money(incentive)} · ${pending} assessments pending. These are draft amounts, not a balance due.`;
            rows.filter(x => x.amount > 0 || x.status === 'pending').forEach(x => ledger.append(el('li', `${x.title} · ${x.label || 'Selected week'} · ${x.status === 'pending' ? 'Pending: ' + x.reason : `${x.type === 'fine' ? 'Fine' : 'Incentive'} ${money(x.amount)}${x.settle ? '' : ' · below minimum settlement threshold'}`}`)));
            if (!rows.length) ledger.append(el('li', 'Assign rules to see assessments.'));
            const body = query('[data-macro-evidence]'); body.replaceChildren();
            evidence.days.forEach(d => {
                const row = el('tr'); row.append(el('th', dayLabel(d.date)));
                catalog.filter(c => c.kind === 'macro').forEach(c => row.append(el('td', `${number(d.actual?.[c.metric])} / ${number(d.target?.[c.metric])}${c.metric === 'calories' ? ' kcal' : ' g'}`)));
                const daily = rows.filter(x => x.label === d.date);
                row.append(el('td', state.excused[d.date] ? 'Excused · no assessments' : !d.completed ? 'Day not complete' : daily.some(x => x.status === 'pending') ? 'Some evidence pending' : daily.some(x => x.amount > 0) ? 'See assessed rules below' : 'No daily assessment', 'accountability-day-status'));
                body.append(row);
                if (d.source) { const sourceRow = el('tr'), td = el('td', `Target source: ${d.source}`); td.colSpan = 6; sourceRow.append(td); body.append(sourceRow); }
            });
        }
        function drawWeek() {
            const box = query('[data-excused-days]'); box.replaceChildren();
            if (!evidence) { query('[data-count-heading]').replaceChildren(); query('[data-count-evidence]').replaceChildren(); return; }
            evidence.days.forEach(d => {
                const card = el('div', undefined, 'accountability-excuse' + (state.excused[d.date] ? ' is-excused' : '')), label = el('label'), check = el('input'); check.type = 'checkbox'; check.checked = !!state.excused[d.date]; check.disabled = !editable; check.setAttribute('aria-label', `Excused ${dayLabel(d.date)}`);
                label.append(check, el('span', 'Excused')); card.append(el('strong', dayLabel(d.date)), label);
                check.addEventListener('change', () => { if (check.checked) state.excused[d.date] = true; else delete state.excused[d.date]; card.classList.toggle('is-excused', check.checked); notice('Unsaved excused-date change. Save preview to retain it.'); renderAssessments(); });
                box.append(card);
            });
            const rules = state.rules.filter(r => r.assigned && catalog.find(c => c.id === r.id).kind === 'count');
            query('[data-count-panel]').hidden = !rules.length;
            const heading = query('[data-count-heading]'), body = query('[data-count-evidence]'); heading.replaceChildren(); body.replaceChildren();
            const tr = el('tr'); tr.append(el('th', 'Date')); rules.forEach(r => tr.append(el('th', catalog.find(c => c.id === r.id).title))); heading.append(tr);
            evidence.days.forEach(d => {
                const row = el('tr'); row.append(el('th', dayLabel(d.date)));
                rules.forEach(r => {
                    const td = el('td');
                    if (editable) {
                        const n = input('', state.observations[d.date]?.[r.id] ?? '', 0, 1000, 1, `${dayLabel(d.date)}: reviewed ${r.id} count`); n.required = false; n.disabled = !d.completed; n.dataset.reviewedCount = 'true';
                        n.addEventListener('change', () => {
                            if (n.value === '') { if (state.observations[d.date]) { delete state.observations[d.date][r.id]; if (!Object.keys(state.observations[d.date]).length) delete state.observations[d.date]; } }
                            else if (n.reportValidity()) { (state.observations[d.date] ??= {})[r.id] = Number(n.value); }
                            notice('Unsaved reviewed-count change. Save preview to retain it.'); renderAssessments();
                        }); td.append(n);
                    } else td.textContent = state.excused[d.date] ? 'Excused' : number(state.observations[d.date]?.[r.id]);
                    row.append(td);
                }); body.append(row);
            });
        }
        async function loadEvidence(date) {
            if (!state.enabled) return;
            const version = ++requestVersion; evidence = null; drawWeek(); renderAssessments(); query('[data-evidence-status]').textContent = 'Loading prescribed targets and nutrition logs…';
            try {
                const response = await fetch(api + '/evidence' + (date ? '?date=' + encodeURIComponent(date) : ''), { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
                const result = await response.json(); if (version !== requestVersion) return; evidence = result;
                query('[data-preview-week]').value = date || result.today; query('[data-preview-week]').max = result.today;
                query('[data-evidence-status]').textContent = `Week of ${dayLabel(result.weekStart)}. ${result.notice || 'Daily targets and saved logs loaded.'}`;
                drawWeek(); renderAssessments();
            } catch { if (version === requestVersion) { query('[data-evidence-status]').textContent = 'Could not load evidence. No amounts are inferred; sign in again or choose a valid date within the last 12 weeks.'; drawWeek(); renderAssessments(); } }
        }
        function renderLateFee() {
            const p = query('[data-unpaid]'), d = query('[data-overdue]');
            const calculation = p.value !== '' && d.value !== '' ? lateFee(state.payment, Number(p.value), Number(d.value)) : null;
            query('[data-late-result]').textContent = calculation ? `${calculation.periods} full late period(s) after grace · late fee preview ${money(calculation.fee)}. Monthly late periods use 30 days in this sample. No fee is charged.` : 'Enter valid principal and whole days past due.';
        }
        async function post(path, fields) {
            const body = new URLSearchParams({ ...fields, __RequestVerificationToken: token() });
            const response = await fetch(api + path, { method: 'POST', credentials: 'same-origin', cache: 'no-store', body, headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error(response.status === 429 ? 'Too many approval attempts. Try again in 15 minutes.' : 'Could not verify approval. Confirm agreement and use the signed-in client’s username and password.');
            return response.json();
        }
        let approvalRequest = 0;
        async function checkApproval() {
            const output = query('[data-approval-state]'), current = terms(state), revision = ++approvalRequest;
            output.textContent = 'Awaiting client approval. No fees are payable from this preview.';
            if (!savedReceipt || savedReceipt.terms !== current || !state.enabled) return;
            output.textContent = 'Verifying saved client approval…';
            try {
                const result = await post('/approval-status', { Token: savedReceipt.token, Terms: current });
                if (revision !== approvalRequest) return;
                output.textContent = result.approved ? `Preview approved by ${result.signedBy} · ${new Date(result.approvedAt).toLocaleString()}. Credentials verified; receipt expires ${new Date(result.expiresAt).toLocaleDateString()}. Browser-only record, no permanent contract.` : 'Saved approval is invalid or expired. Client must approve these terms again.';
            } catch { if (revision === approvalRequest) output.textContent = 'Saved approval could not be verified. It is not treated as approved.'; }
        }
        load(); drawEditor(); render(); if (state.enabled) loadEvidence();
        query('[data-load-week]').addEventListener('click', () => { const date = query('[data-preview-week]'); if (date.value && date.reportValidity()) loadEvidence(date.value); });
        query('[data-unpaid]').addEventListener('input', renderLateFee); query('[data-overdue]').addEventListener('input', renderLateFee);
        if (form) {
            form.addEventListener('input', () => notice('Unsaved changes. Save to update the agreement and assessment preview.'));
            form.addEventListener('submit', event => {
                event.preventDefault(); if (!form.reportValidity() || [...root.querySelectorAll('[data-reviewed-count]')].some(n => !n.reportValidity())) return;
                const next = { ...state, enabled: form.elements.enabled.checked, rules: catalog.map(c => { const f = suffix => form.elements[c.id + '-' + suffix]; return { id: c.id, assigned: f('assigned').checked, type: f('type').value, amount: Number(f('amount').value), frequency: f('frequency').value, lower: Number(f('lower').value), upper: Number(f('upper').value), min: Number(f('min').value), max: Number(f('max').value) }; }), payment: {} };
                Object.entries(paymentNames).forEach(([field, prop]) => { next.payment[prop] = ['amount', 'graceDays', 'lateValue', 'lateCap'].includes(prop) ? Number(form.elements[field].value) : form.elements[field].value; });
                if (!valid(next)) { notice('Correct the criteria and totals: lower ≤ upper, minimum total ≤ maximum total, whole occurrence counts, and percentage late charges between 0% and 100%.'); return; }
                const changed = terms(state) !== terms(next);
                try { localStorage.setItem(key, JSON.stringify(next)); if (changed) { localStorage.removeItem(receiptKey); savedReceipt = null; } state = next; render(); drawWeek(); if (state.enabled && !evidence) loadEvidence(); notice(`Preview saved in this browser.${changed ? ' Terms changed; client approval is required again.' : ' Excused dates and reviewed counts updated.'} No fees have been charged.`); }
                catch { notice('Could not save browser settings. No changes were retained.'); }
            });
            query('[data-reset-preview]').addEventListener('click', () => { try { localStorage.removeItem(key); localStorage.removeItem(receiptKey); state = defaults(); savedReceipt = null; evidence = null; ++requestVersion; drawEditor(); render(); drawWeek(); notice('Preview reset. All rules are unassigned and the agreement is off.'); } catch { notice('Could not reset browser storage.'); } });
        }
        const approvalForm = query('[data-approval-form]');
        if (approvalForm) approvalForm.addEventListener('submit', async event => {
            event.preventDefault(); if (!approvalForm.reportValidity()) return;
            const current = terms(state), fields = { Username: approvalForm.elements.username.value, Password: approvalForm.elements.password.value, Agree: 'true', Terms: current }, button = approvalForm.querySelector('button');
            approvalForm.elements.password.value = ''; button.disabled = true; query('[data-approval-status]').textContent = 'Verifying your credentials securely…';
            try {
                const result = await post('/approve', fields); fields.Password = '';
                if (current !== terms(state)) throw new Error('Terms changed during approval. Review and approve the new terms.');
                savedReceipt = { token: result.token, terms: current }; localStorage.setItem(receiptKey, JSON.stringify(savedReceipt));
                query('[data-approval-status]').textContent = 'Credentials verified and preview approval saved in this browser. Your password was not stored.'; checkApproval();
            } catch (error) { query('[data-approval-status]').textContent = error.message || 'Approval could not be saved. No agreement was recorded.'; }
            finally { fields.Password = ''; button.disabled = false; }
        });
        window.addEventListener('storage', event => { if ([key, receiptKey, null].includes(event.key)) { load(); drawEditor(); render(); drawWeek(); if (state.enabled) loadEvidence(query('[data-preview-week]').value); } });
        window.addEventListener('pageshow', event => { if (event.persisted) { load(); drawEditor(); render(); if (state.enabled) loadEvidence(); } });
    }
    document.querySelectorAll('[data-accountability]').forEach(initialize);
})();
