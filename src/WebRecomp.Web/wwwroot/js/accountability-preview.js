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
        payment: { method: 'bank-transfer', frequency: 'weekly', day: 'Monday', amount: 0, graceDays: 3, lateBasis: 'amount', lateValue: 0, lateFrequency: 'weekly', lateCap: 100, instructions: '' }, excused: {}, observations: {}, snapWorkouts: [], snapResults: {} });
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
        return validSnap(s) && Object.entries(s.excused).every(([d, v]) => datePattern.test(d) && typeof v === 'boolean') && Object.entries(s.observations).every(([d, values]) => datePattern.test(d) && values && typeof values === 'object' && !Array.isArray(values)
            && Object.entries(values).every(([id, n]) => catalog.some(c => c.id === id && c.kind === 'count') && Number.isInteger(n) && n >= 0 && n <= 1000));
    }
    const terms = s => JSON.stringify({ version: 2, enabled: s.enabled, rules: s.rules, payment: s.payment, ...((s.snapWorkouts?.length || 0) > 0 ? { snapWorkouts: s.snapWorkouts } : {}), excusedPolicy: 'Coach-approved dated excuses waive adherence assessments and incentives, but not contract payments. Missing evidence remains pending. No extra reward for extra training or under-eating.' });
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
    const snapBases = { 'completed-reps': 'Completed reps', 'completed-sets': 'Completed sets', 'incomplete-reps': 'Incomplete reps', 'incomplete-sets': 'Incomplete sets' };
    const safeId = id => typeof id === 'string' && /^[a-zA-Z0-9-]{1,64}$/.test(id);
    const realDate = d => datePattern.test(d) && !Number.isNaN(Date.parse(d + 'T12:00:00Z')) && new Date(d + 'T12:00:00Z').toISOString().slice(0, 10) === d;
    function validSnap(s) {
        const workouts = s.snapWorkouts ?? [], results = s.snapResults ?? {};
        if (!Array.isArray(workouts) || workouts.length > 50 || !results || typeof results !== 'object' || Array.isArray(results) || Object.keys(results).length > 50) return false;
        if (new Set(workouts.map(w => w.id)).size !== workouts.length) return false;
        if (!workouts.every(w => safeId(w.id) && realDate(w.date) && typeof w.name === 'string' && w.name.trim().length > 0 && w.name.length <= 100 && cents(w.cap)
            && Array.isArray(w.exercises) && w.exercises.length > 0 && w.exercises.length <= 15 && new Set(w.exercises.map(e => e.id)).size === w.exercises.length
            && w.exercises.every(e => safeId(e.id) && safeId(e.catalogId) && typeof e.name === 'string' && e.name.length > 0 && e.name.length <= 250 && Number.isInteger(e.sets) && e.sets >= 1 && e.sets <= 10
                && Number.isInteger(e.reps) && e.reps >= 1 && e.reps <= 100 && cents(e.weight) && e.weight <= 2000 && ['fine', 'incentive'].includes(e.type) && Object.hasOwn(snapBases, e.basis) && cents(e.rate) && e.rate <= 100))) return false;
        return Object.entries(results).every(([id, result]) => {
            const w = workouts.find(w => w.id === id);
            return w && result && typeof result.finalized === 'boolean' && result.sets && typeof result.sets === 'object' && !Array.isArray(result.sets)
                && Object.keys(result.sets).length === w.exercises.length && w.exercises.every(e => Array.isArray(result.sets[e.id]) && result.sets[e.id].length === e.sets
                    && result.sets[e.id].every(set => set && (set.reps === null ? !result.finalized : Number.isInteger(set.reps) && set.reps >= 0 && set.reps <= 1000)
                        && (set.weight === null ? !result.finalized : cents(set.weight) && set.weight <= 2000)));
        });
    }
    function assessSnap(s, evidence) {
        if (!evidence?.days?.length) return [];
        const first = evidence.days[0].date, last = evidence.days.at(-1).date, today = evidence.today;
        const rows = [];
        for (const w of (s.snapWorkouts || []).filter(w => w.date >= first && w.date <= last)) {
            const totals = { fine: 0, incentive: 0 }, result = s.snapResults?.[w.id];
            for (const e of w.exercises) {
                const row = { id: `snap-${w.id}-${e.id}`, workoutId: w.id, title: `${w.name} · ${e.name}`, label: w.date, type: e.type, amount: 0, minimum: 0, cap: w.cap, settle: true, basis: e.basis };
                if (s.excused[w.date]) { rows.push({ ...row, status: 'excused', reason: 'Excused date · no assessment' }); continue; }
                if (w.date > today || !result?.finalized) { rows.push({ ...row, status: 'pending', reason: w.date > today ? 'Assignment date is in the future' : 'Completion has not been finalized' }); continue; }
                const recorded = result.sets?.[e.id];
                if (!Array.isArray(recorded) || recorded.length !== e.sets || recorded.some(set => !known(set.reps) || !known(set.weight))) { rows.push({ ...row, status: 'pending', reason: 'Missing completed reps or weight' }); continue; }
                const completedReps = recorded.reduce((n, set) => n + (set.weight + 1e-7 >= e.weight ? Math.min(e.reps, Math.max(0, set.reps)) : 0), 0);
                const completedSets = recorded.filter(set => set.reps >= e.reps && set.weight + 1e-7 >= e.weight).length;
                const units = { 'completed-reps': completedReps, 'completed-sets': completedSets, 'incomplete-reps': e.sets * e.reps - completedReps, 'incomplete-sets': e.sets - completedSets }[e.basis];
                const raw = Math.round(units * e.rate * 100) / 100, amount = Math.round(Math.min(raw, Math.max(0, w.cap - totals[e.type])) * 100) / 100;
                totals[e.type] += amount;
                rows.push({ ...row, status: 'assessed', amount, units, completedReps, completedSets, assignedReps: e.sets * e.reps, assignedSets: e.sets, applies: units > 0, reason: `${units} ${snapBases[e.basis].toLowerCase()} × ${e.rate.toFixed(2)}/unit` });
            }
        }
        return rows;
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { catalog, defaults, valid, terms, outcome, assess, lateFee, validSnap, assessSnap, snapBases };
    if (typeof document === 'undefined') return;
    const money = n => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n);
    const number = n => known(n) ? new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 }).format(n) : '—';
    const dayLabel = d => new Date(d + 'T12:00:00Z').toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' });
    const el = (tag, text, cls) => { const n = document.createElement(tag); if (text !== undefined) n.textContent = text; if (cls) n.className = cls; return n; };
    function initialize(root) {
        if (root.dataset.initialized) return; root.dataset.initialized = 'true';
        const key = `12wr:accountability-preview:v2:${root.dataset.clientId}`, receiptKey = key + ':approval';
        const api = `/accountability-preview/clients/${encodeURIComponent(root.dataset.clientId)}`;
        const editable = root.dataset.editable === 'true', reviewPage = root.dataset.reviewPage === 'true', canAssignSnap = editable || root.dataset.canAssignSnap === 'true', canRecordSnap = canAssignSnap || root.dataset.canApprove === 'true';
        const form = root.querySelector('[data-accountability-form]'), status = root.querySelector('[data-preview-status]');
        const query = s => root.querySelector(s), token = () => query('input[name="__RequestVerificationToken"]').value;
        let state = defaults(), evidence = null, savedReceipt = null, requestVersion = 0;
        function notice(text) { if (status) status.textContent = text; }
        function load() {
            state = defaults(); savedReceipt = null;
            try {
                const raw = localStorage.getItem(key); if (raw) { const parsed = JSON.parse(raw); if (valid(parsed)) state = { ...parsed, snapWorkouts: parsed.snapWorkouts || [], snapResults: parsed.snapResults || {} }; else notice('Stored settings were invalid. Review and save a new preview.'); }
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
            const assigned = state.enabled ? state.rules.filter(r => r.assigned) : [];
            root.hidden = !editable && !reviewPage && !state.enabled && !state.snapWorkouts.length;
            query('[data-agreement-state]').textContent = state.enabled ? 'Enabled · Preview only' : state.snapWorkouts.length ? 'Snap Workout · Preview only' : 'Not enabled';
            query('[data-assigned-preview]').hidden = !state.enabled && !state.snapWorkouts.length;
            query('[data-exceptions-panel]').hidden = !canAssignSnap && !state.enabled && !state.snapWorkouts.length;
            query('[data-snap-workout]').hidden = !canAssignSnap && !state.snapWorkouts.length;
            query('[data-no-agreement]').hidden = state.enabled || canAssignSnap || state.snapWorkouts.length > 0;
            query('[data-assignment-summary]').textContent = `${state.enabled ? assigned.length + ' assigned adherence rules' : 'Adherence rules disabled; Snap terms shown separately'} · USD · Lower and upper endpoints are included. Weekly macro checks compare summed actual intake with summed prescribed targets for non-excused days.`;
            const list = query('[data-assigned-rules]'); list.replaceChildren();
            assigned.forEach(r => {
                const c = catalog.find(c => c.id === r.id), card = el('article', undefined, 'accountability-rule-card');
                const criterion = c.kind === 'macro' ? `Allowed deviation ${r.lower}% to +${r.upper}% (${100 + r.lower}%–${100 + r.upper}% of target)` : `${r.lower}–${r.upper} occurrences per ${r.frequency} assessment`;
                card.append(el('strong', c.title), el('p', `${r.type === 'fine' ? 'Fine' : 'Incentive'} · ${money(r.amount)} ${r.frequency}`, 'accountability-kind'), el('p', criterion), el('p', `Minimum to settle ${money(r.min)} · Program cap ${money(r.max)}`), el('small', c.description)); list.append(card);
            });
            query('[data-payment-summary]').textContent = paymentText();
            renderAssessments(); renderSnapAssignments(); renderLateFee(); checkApproval();
        }
        function renderAssessments() {
            const ledger = query('[data-assessment-ledger]'); ledger.replaceChildren();
            if (!evidence) { query('[data-snap-weekly-total]').textContent = 'Weekly total pending: select a week to load its dates.'; query('[data-macro-evidence]').replaceChildren(); query('[data-assessment-total]').textContent = 'Awaiting evidence; no assessment can be calculated.'; return; }
            const rows = [...assess(state, evidence), ...assessSnap(state, evidence)];
            const fine = rows.filter(x => x.type === 'fine').reduce((n, x) => n + x.amount, 0), incentive = rows.filter(x => x.type === 'incentive').reduce((n, x) => n + x.amount, 0), pending = rows.filter(x => x.status === 'pending').length;
            query('[data-assessment-total]').textContent = `Selected-week preview: fines ${money(fine)} · incentives ${money(incentive)} · ${pending} assessments pending. These are draft amounts, not a balance due.`;
            const snapRows = rows.filter(x => x.workoutId);
            const snapFine = snapRows.filter(x => x.type === 'fine').reduce((n, x) => n + x.amount, 0), snapIncentive = snapRows.filter(x => x.type === 'incentive').reduce((n, x) => n + x.amount, 0);
            query('[data-snap-weekly-total]').textContent = `Week of ${dayLabel(evidence.weekStart)} · Snap fines ${money(snapFine)} · Snap incentives ${money(snapIncentive)} · Combined weekly fines ${money(fine)} · Combined weekly incentives ${money(incentive)}. Draft preview only.`;
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
                const card = el('div', undefined, 'accountability-excuse' + (state.excused[d.date] ? ' is-excused' : '')), label = el('label'), check = el('input'); check.type = 'checkbox'; check.checked = !!state.excused[d.date]; check.disabled = !canAssignSnap; check.setAttribute('aria-label', `Excused ${dayLabel(d.date)}`);
                label.append(check, el('span', 'Excused · no fees')); card.append(el('strong', dayLabel(d.date)), label);
                check.addEventListener('change', () => { if (check.checked) state.excused[d.date] = true; else delete state.excused[d.date]; card.classList.toggle('is-excused', check.checked); notice('Unsaved fee exception. Use Save fee exceptions to retain it.'); query('[data-exceptions-status]').textContent = 'Unsaved changes. Save fee exceptions to retain the checked dates.'; renderAssessments(); });
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
            if (!state.enabled && !canAssignSnap && !state.snapWorkouts.length) return;
            const version = ++requestVersion; evidence = null; drawWeek(); renderAssessments(); query('[data-evidence-status]').textContent = 'Loading prescribed targets and nutrition logs…';
            try {
                const response = await fetch(api + '/evidence' + (date ? '?date=' + encodeURIComponent(date) : ''), { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
                const result = await response.json(); if (version !== requestVersion) return; evidence = result;
                query('[data-preview-week]').value = date || result.today; query('[data-preview-week]').max = result.today;
                query('[data-evidence-status]').textContent = `Week of ${dayLabel(result.weekStart)}. ${result.notice || 'Daily targets and saved logs loaded.'}`;
                drawWeek(); renderAssessments(); renderSnapAssignments();
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
            if (!savedReceipt || savedReceipt.terms !== current || (!state.enabled && !state.snapWorkouts.length)) return;
            output.textContent = 'Verifying saved client approval…';
            try {
                const result = await post('/approval-status', { Token: savedReceipt.token, Terms: current });
                if (revision !== approvalRequest) return;
                output.textContent = result.approved ? `Preview approved by ${result.signedBy} · ${new Date(result.approvedAt).toLocaleString()}. Credentials verified; receipt expires ${new Date(result.expiresAt).toLocaleDateString()}. Browser-only record, no permanent contract.` : 'Saved approval is invalid or expired. Client must approve these terms again.';
            } catch { if (revision === approvalRequest) output.textContent = 'Saved approval could not be verified. It is not treated as approved.'; }
        }
        let snapDraft = [], exerciseOptions = [], catalogRequest = 0;
        const snapForm = query('[data-snap-assign]');
        function persistSnap(next, message) {
            if (!valid(next)) { query('[data-snap-status]').textContent = 'Check workout targets, rates and completed-set entries before saving.'; return false; }
            const changed = terms(next) !== terms(state);
            try {
                localStorage.setItem(key, JSON.stringify(next));
                if (changed) { localStorage.removeItem(receiptKey); savedReceipt = null; }
                state = next; render(); drawWeek();
                query('[data-snap-status]').textContent = message + (changed ? ' Assignment terms changed; new client approval is required.' : '') + ' Browser-only preview; no actual fee was charged.';
                return true;
            } catch { query('[data-snap-status]').textContent = 'Could not save browser storage. No changes were retained.'; return false; }
        }
        function readSnapDraft() {
            if (!snapForm) return;
            snapDraft = snapDraft.map(e => {
                const val = field => snapForm.elements[`snap-${e.id}-${field}`].value;
                return { ...e, sets: Number(val('sets')), reps: Number(val('reps')), weight: Number(val('weight')), type: val('type'), basis: val('basis'), rate: Number(val('rate')) };
            });
        }
        function drawSnapDraft() {
            if (!snapForm) return;
            const body = query('[data-snap-draft]'); body.replaceChildren();
            snapDraft.forEach(e => {
                const row = el('tr'), remove = el('button', 'Remove'); remove.type = 'button'; remove.setAttribute('aria-label', `Remove ${e.name}`);
                remove.addEventListener('click', () => { readSnapDraft(); snapDraft = snapDraft.filter(x => x.id !== e.id); drawSnapDraft(); });
                const basis = el('select'); basis.name = `snap-${e.id}-basis`; basis.setAttribute('aria-label', `${e.name}: fee basis`);
                Object.entries(snapBases).forEach(([value, label]) => { const o = el('option', label); o.value = value; basis.append(o); }); basis.value = e.basis;
                const nodes = [el('strong', e.name), input(`snap-${e.id}-sets`, e.sets, 1, 10, 1, `${e.name}: prescribed sets`), input(`snap-${e.id}-reps`, e.reps, 1, 100, 1, `${e.name}: reps per set`),
                    input(`snap-${e.id}-weight`, e.weight, 0, 2000, '.01', `${e.name}: prescribed weight in pounds`), select(`snap-${e.id}-type`, ['fine', 'incentive'], e.type, `${e.name}: fine or incentive`), basis,
                    input(`snap-${e.id}-rate`, e.rate, 0, 100, '.01', `${e.name}: dollars per assessed rep or set`), remove];
                nodes.forEach(n => { const td = el('td'); td.append(n); row.append(td); }); body.append(row);
            });
        }
        async function searchExercises() {
            if (!canAssignSnap || !snapForm) return;
            const revision = ++catalogRequest, picker = query('[data-snap-picker]'), output = query('[data-snap-catalog-status]');
            output.textContent = 'Loading exercise choices…';
            try {
                const response = await fetch(api + '/exercises?q=' + encodeURIComponent(query('[data-snap-search]').value.trim()), { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
                const result = await response.json(); if (revision !== catalogRequest) return;
                exerciseOptions = result; picker.replaceChildren(); const blank = el('option', 'Choose an exercise'); blank.value = ''; picker.append(blank);
                result.forEach(e => { const option = el('option', e.name); option.value = e.id; picker.append(option); });
                output.textContent = result.length ? `${result.length} exercises found. Refine the search if needed.` : 'No matching exercises. Try another name.';
            } catch { if (revision === catalogRequest) output.textContent = 'Could not load the exercise catalog. Only the assigned coach can select exercises.'; }
        }
        function renderSnapAssignments() {
            const container = query('[data-snap-assignments]'); if (!container) return; container.replaceChildren();
            const today = evidence?.today;
            if (snapForm && today) {
                if (!snapForm.elements.snapDate.value) snapForm.elements.snapDate.value = today;
                const addDays = amount => { const d = new Date(today + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + amount); return d.toISOString().slice(0, 10); };
                snapForm.elements.snapDate.min = addDays(-84); snapForm.elements.snapDate.max = addDays(84);
            }
            if (!state.snapWorkouts.length) { container.append(el('p', 'No Snap Workouts assigned yet.')); return; }
            const week = evidence?.days?.length ? evidence.days : null;
            const workouts = [...state.snapWorkouts].sort((a, b) => b.date.localeCompare(a.date));
            if (!workouts.length) { container.append(el('p', 'No Snap Workouts in the selected week. Choose another week to view its assignments.')); return; }
            const assessed = evidence ? assessSnap(state, evidence) : [];
            workouts.forEach(w => {
                const article = el('article', undefined, 'snap-assignment'); article.append(el('h4', `${w.name} · ${dayLabel(w.date)}`), el('p', `Assessment cap ${money(w.cap)} per type for this workout${state.excused[w.date] ? ' · EXCUSED: no assessment' : ''}.`));
                if (week && (w.date < week[0].date || w.date > week.at(-1).date)) article.append(el('p', 'Outside the selected weekly total. Choose this assignment’s week to view its assessment.'));
                const resultForm = el('form'), previous = state.snapResults[w.id];
                const canRecord = canRecordSnap && today && w.date <= today;
                w.exercises.forEach(e => {
                    const section = el('section', undefined, 'snap-exercise');
                    section.append(el('h5', e.name), el('p', `${e.sets} sets × ${e.reps} reps · ${number(e.weight)} lb · ${e.type === 'fine' ? 'Fine' : 'Incentive'} ${money(e.rate)} per ${snapBases[e.basis].toLowerCase().replace(/s$/, '')}.`));
                    const wrapper = el('div', undefined, 'accountability-scroll'), table = el('table'), heading = el('thead'), headRow = el('tr');
                    ['Set', 'Target reps', 'Target lb', 'Completed reps', 'Used lb'].forEach(text => headRow.append(el('th', text))); heading.append(headRow); table.append(heading); const body = el('tbody');
                    for (let i = 0; i < e.sets; i++) {
                        const row = el('tr'), saved = previous?.sets?.[e.id]?.[i]; row.append(el('th', String(i + 1)), el('td', String(e.reps)), el('td', number(e.weight)));
                        if (canRecord) {
                            const reps = input(`${e.id}-${i}-reps`, saved?.reps ?? '', 0, 1000, 1, `${e.name}, set ${i + 1}: completed reps`), weight = input(`${e.id}-${i}-weight`, saved?.weight ?? '', 0, 2000, '.01', `${e.name}, set ${i + 1}: weight used in pounds`);
                            reps.required = false; weight.required = false;
                            [reps, weight].forEach(n => { const td = el('td'); td.append(n); row.append(td); });
                        } else row.append(el('td', number(saved?.reps)), el('td', number(saved?.weight)));
                        body.append(row);
                    }
                    table.append(body); wrapper.append(table); section.append(wrapper);
                    const row = assessed.find(x => x.workoutId === w.id && x.id === `snap-${w.id}-${e.id}`);
                    section.append(el('p', row ? row.status === 'assessed' ? `${row.completedReps}/${row.assignedReps} qualifying reps · ${row.completedSets}/${row.assignedSets} qualifying sets · ${row.reason} · ${e.type} preview ${money(row.amount)}` : row.reason : 'Assessment pending: select the assignment week.'));
                    resultForm.append(section);
                });
                if (canRecord) {
                    const finalLabel = el('label', undefined, 'accountability-enable'), finalized = el('input'); finalized.type = 'checkbox'; finalized.name = 'finalized'; finalized.checked = !!previous?.finalized;
                    finalLabel.append(finalized, el('span', 'Finished — these are the final recorded sets.'));
                    resultForm.append(finalLabel, el('p', 'Enter reps and weight for every set before marking finished. Enter 0 reps for a set not completed; blank entries stay unknown. Results can be updated in this preview.'));
                    const save = el('button', 'Save workout results'); save.type = 'submit'; resultForm.append(save);
                    resultForm.addEventListener('submit', event => {
                        event.preventDefault(); if (!resultForm.reportValidity()) return;
                        const result = { finalized: finalized.checked, sets: {} };
                        for (const e of w.exercises) {
                            result.sets[e.id] = Array.from({ length: e.sets }, (_, i) => {
                                const get = field => { const value = resultForm.elements[`${e.id}-${i}-${field}`].value; return value === '' ? null : Number(value); };
                                return { reps: get('reps'), weight: get('weight') };
                            });
                        }
                        if (result.finalized && Object.values(result.sets).some(sets => sets.some(set => set.reps === null || set.weight === null))) { query('[data-snap-status]').textContent = 'Final results need reps and weight for every set. Use 0 reps for an uncompleted set, or save progress without marking finished.'; return; }
                        persistSnap({ ...state, snapResults: { ...state.snapResults, [w.id]: result } }, 'Workout results saved. The selected-week total has been updated.');
                    });
                } else if (w.date > today) resultForm.append(el('p', 'Record results on or after the assignment date.'));
                article.append(resultForm);
                if (canAssignSnap) {
                    const remove = el('button', 'Remove assignment'); remove.type = 'button'; remove.className = 'secondary';
                    remove.addEventListener('click', () => { const results = { ...state.snapResults }; delete results[w.id]; persistSnap({ ...state, snapWorkouts: state.snapWorkouts.filter(x => x.id !== w.id), snapResults: results }, 'Snap Workout removed.'); }); article.append(remove);
                }
                container.append(article);
            });
        }
        if (snapForm) {
            query('[data-snap-search-button]').addEventListener('click', searchExercises);
            query('[data-snap-search]').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); searchExercises(); } });
            query('[data-snap-add]').addEventListener('click', () => {
                const chosen = exerciseOptions.find(e => e.id === query('[data-snap-picker]').value);
                if (!chosen) { query('[data-snap-catalog-status]').textContent = 'Choose an exercise first.'; return; }
                if (snapDraft.length >= 15) { query('[data-snap-catalog-status]').textContent = 'Use up to 15 exercises per Snap Workout.'; return; }
                readSnapDraft(); snapDraft.push({ id: crypto.randomUUID(), catalogId: chosen.id, name: chosen.name, sets: 3, reps: 10, weight: 0, type: 'fine', basis: 'incomplete-reps', rate: .1 }); drawSnapDraft();
            });
            snapForm.addEventListener('submit', event => {
                event.preventDefault(); if (!snapForm.reportValidity()) return;
                if (!evidence?.today) { query('[data-snap-status]').textContent = 'Load a valid week before assigning a workout.'; return; }
                readSnapDraft();
                const workout = { id: crypto.randomUUID(), date: snapForm.elements.snapDate.value, name: snapForm.elements.snapName.value.trim(), cap: Number(snapForm.elements.snapCap.value), exercises: snapDraft.map(e => ({ ...e })) };
                if (persistSnap({ ...state, snapWorkouts: [...state.snapWorkouts, workout] }, 'Snap Workout assigned. Open the client portal on this browser to record results.')) { snapDraft = []; drawSnapDraft(); }
            });
            drawSnapDraft(); searchExercises();
        }
        const saveExceptions = query('[data-save-exceptions]');
        if (saveExceptions) saveExceptions.addEventListener('click', () => {
            if (!canAssignSnap) return;
            if (persistSnap(state, 'Fee exceptions saved.')) query('[data-exceptions-status]').textContent = 'Saved for these exact dates. Checked dates have no adherence or Snap Workout assessment.';
        });
        load(); drawEditor(); render(); if (state.enabled || canAssignSnap || state.snapWorkouts.length) loadEvidence();
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
        window.addEventListener('storage', event => { if ([key, receiptKey, null].includes(event.key)) { load(); drawEditor(); render(); drawWeek(); if (state.enabled || canAssignSnap || state.snapWorkouts.length) loadEvidence(query('[data-preview-week]').value); } });
        window.addEventListener('pageshow', event => { if (event.persisted) { load(); drawEditor(); render(); if (state.enabled || canAssignSnap || state.snapWorkouts.length) loadEvidence(); } });
    }
    document.querySelectorAll('[data-accountability]').forEach(initialize);
})();
