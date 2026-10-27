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
        ['cheat-foods', 'Cheat-food occurrences', 'count', null, 'Every reviewed occurrence is paid from the first occurrence; no free allowance.', 'fine', 2, 'daily', 0, 0],
        ['missed-review', 'Unapproved missed appointments', 'count', null, 'Only reviewed, unapproved missed appointments; approved exceptions are excluded.', 'fine', 5, 'weekly', 0, 0]
    ].map(([id, title, kind, metric, description, type, amount, frequency, lower, upper]) => ({ id, title, kind, metric, description, type, amount, frequency, lower, upper }));
    const amounts = [0, 1, 2, 5, 10, 15, 20, 25, 30, 50, 75, 100];
    const frequencies = ['daily', 'weekly', 'monthly'];
    const methods = { 'bank-transfer': 'Bank transfer', 'card-external': 'Card through external provider', cash: 'Cash', check: 'Check', other: 'Other agreed method' };
    const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    const cents = n => Number.isFinite(n) && n >= 0 && n <= 10000 && Math.abs(Math.round(n * 100) - n * 100) < 0.000001;
    const defaults = () => ({ version: 2, enabled: false, rules: catalog.map(r => ({ id: r.id, assigned: false, type: r.type, amount: r.amount, frequency: r.frequency, lower: r.lower, upper: r.upper, min: 0, max: 100 })),
        payment: { method: 'bank-transfer', frequency: 'weekly', day: 'Monday', amount: 0, graceDays: 3, lateBasis: 'amount', lateValue: 0, lateFrequency: 'weekly', lateCap: 100, instructions: '' }, agreement: { start: '', mode: 'open', weeks: 12, endedOn: null }, excused: {}, observations: {}, snapWorkouts: [], snapResults: {} });
    const datePattern = /^\d{4}-\d{2}-\d{2}$/;
    function valid(s) {
        if (s?.version !== 2 || typeof s.enabled !== 'boolean' || !Array.isArray(s.rules) || s.rules.length !== catalog.length) return false;
        if (!catalog.every((c, i) => { const r = s.rules[i]; return r?.id === c.id && typeof r.assigned === 'boolean' && ['fine', 'incentive'].includes(r.type) && cents(r.amount) && frequencies.includes(r.frequency)
            && (r.id !== 'cheat-foods' || (r.type === 'fine' && r.lower === 0 && r.upper === 0 && (!r.assigned || r.amount > 0))) && cents(r.min) && cents(r.max) && r.min <= r.max && Number.isFinite(r.lower) && Number.isFinite(r.upper) && r.lower <= r.upper
            && (c.kind === 'macro' ? r.lower >= -100 && r.lower <= 0 && r.upper >= 0 && r.upper <= 500 : Number.isInteger(r.lower) && Number.isInteger(r.upper) && r.lower >= 0 && r.upper <= 1000); })) return false;
        const a = s.agreement;
        if (a !== undefined && (!a || typeof a !== 'object' || !['open', 'weeks'].includes(a.mode) || !Number.isInteger(a.weeks) || a.weeks < 1 || a.weeks > 52
            || (a.start !== '' && (!realDate(a.start) || a.start < '1900-01-01' || a.start > '2100-01-01'))
            || (a.endedOn != null && (!realDate(a.endedOn) || !a.start || a.endedOn < a.start)))) return false;
        const p = s.payment;
        if (!p || !Object.hasOwn(methods, p.method) || !frequencies.includes(p.frequency) || !weekdays.includes(p.day) || !cents(p.amount) || !Number.isInteger(p.graceDays) || p.graceDays < 0 || p.graceDays > 30
            || !['amount', 'percent'].includes(p.lateBasis) || !cents(p.lateValue) || (p.lateBasis === 'percent' && p.lateValue > 100) || !frequencies.includes(p.lateFrequency) || !cents(p.lateCap) || typeof p.instructions !== 'string' || p.instructions.length > 300) return false;
        if (!s.excused || typeof s.excused !== 'object' || Array.isArray(s.excused) || !s.observations || typeof s.observations !== 'object' || Array.isArray(s.observations)) return false;
        if (Object.keys(s.excused).length > 200 || Object.keys(s.observations).length > 200) return false;
        return validSnap(s) && Object.entries(s.excused).every(([d, v]) => datePattern.test(d) && typeof v === 'boolean') && Object.entries(s.observations).every(([d, values]) => datePattern.test(d) && values && typeof values === 'object' && !Array.isArray(values)
            && Object.entries(values).every(([id, n]) => catalog.some(c => c.id === id && c.kind === 'count') && Number.isInteger(n) && n >= 0 && n <= 1000));
    }
    const terms = s => JSON.stringify({ version: 2, assessmentPolicy: 'PER_PERIOD_ALL_CHEAT_OCCURRENCES_V3', agreement: s.agreement || { start: '', mode: 'open', weeks: 12, endedOn: null }, enabled: s.enabled, rules: s.rules, payment: s.payment, ...((s.snapWorkouts?.length || 0) > 0 ? { snapWorkouts: s.snapWorkouts } : {}), excusedPolicy: 'Coach-approved dated excuses waive adherence assessments and incentives, but not contract payments. Missing evidence remains pending. No extra reward for under-eating. Explicit Snap completion and extra-rep rewards require assigned weight and on-time completion.' });
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
    const shiftDate = (d, days) => { const date = new Date(d + 'T12:00:00Z'); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10); };
    function agreementEnd(s) {
        const a = s.agreement;
        if (!a?.start) return null;
        return a.mode === 'weeks' ? shiftDate(a.start, a.weeks * 7 - 1) : a.endedOn || null;
    }
    function activeOn(s, date) { const a = s.agreement, end = agreementEnd(s); return !!a?.start && date >= a.start && (!end || date <= end); }
    function periodBounds(date, frequency) {
        const d = new Date(date + 'T12:00:00Z');
        if (frequency === 'daily') return [date, date];
        if (frequency === 'weekly') { const start = shiftDate(date, -((d.getUTCDay() + 6) % 7)); return [start, shiftDate(start, 6)]; }
        return [date.slice(0, 7) + '-01', new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0, 12)).toISOString().slice(0, 10)];
    }
    function assess(s, evidence) {
        if (!s.enabled || !evidence?.days?.length || !s.agreement?.start) return [];
        const result = [], viewStart = evidence.days[0].date, viewEnd = evidence.days.at(-1).date;
        for (const rule of s.rules.filter(r => r.assigned)) {
            const title = catalog.find(c => c.id === rule.id).title;
            const source = rule.frequency === 'monthly' ? evidence.periodDays || evidence.days : evidence.days;
            const keys = [...new Set(evidence.days.map(d => periodBounds(d.date, rule.frequency)[0]))];
            for (const key of keys) {
                const bounds = periodBounds(key, rule.frequency), end = agreementEnd(s);
                const from = bounds[0] < s.agreement.start ? s.agreement.start : bounds[0], until = end && bounds[1] > end ? end : bounds[1];
                if (from > until) continue;
                const group = source.filter(d => d.date >= from && d.date <= until);
                const label = from === until ? from : `${from}–${until}`;
                const base = { id: rule.id, title, label, amount: 0, type: rule.type, minimum: rule.min, cap: rule.max, assessmentDate: until, countedInWeek: until >= viewStart && until <= viewEnd };
                const expected = Math.round((Date.parse(until + 'T12:00:00Z') - Date.parse(from + 'T12:00:00Z')) / 86400000) + 1;
                if (group.length !== expected || group.some(d => !d.completed)) { result.push({ ...base, status: 'pending', reason: 'Full assessment period is not complete or its evidence is unavailable' }); continue; }
                const included = group.filter(d => !s.excused[d.date]), o = outcome(rule, included, s);
                const isCheat = rule.id === 'cheat-foods', applies = isCheat ? known(o.value) && o.value > 0 : rule.type === 'fine' ? o.status === 'outside' : o.status === 'within';
                const raw = applies ? rule.amount * (isCheat ? o.value : 1) : 0, amount = Math.round(Math.min(raw, rule.max) * 100) / 100;
                result.push({ ...base, ...o, amount, applies, settle: amount >= rule.min, units: isCheat ? o.value : undefined, reason: isCheat && known(o.value) ? `${o.value} paid occurrences × ${rule.amount.toFixed(2)}; no free allowance` : o.reason });
            }
        }
        return result;
    }
    function lateFee(payment, principal, overdue) {
        if (!cents(principal) || !Number.isInteger(overdue) || overdue < 0 || overdue > 365) return null;
        const elapsed = Math.max(0, overdue - payment.graceDays), size = payment.lateFrequency === 'daily' ? 1 : payment.lateFrequency === 'weekly' ? 7 : 30;
        const periods = Math.floor(elapsed / size), per = payment.lateBasis === 'percent' ? principal * payment.lateValue / 100 : payment.lateValue;
        return { periods, fee: principal === 0 ? 0 : Math.round(Math.min(payment.lateCap, per) * periods * 100) / 100 };
    }
    const snapBases = { 'completed-reps': 'Completed reps', 'completed-sets': 'Completed sets', 'incomplete-reps': 'Incomplete reps', 'incomplete-sets': 'Incomplete sets' };
    const safeId = id => typeof id === 'string' && /^[a-zA-Z0-9-]{1,64}$/.test(id);
    const realDate = d => datePattern.test(d) && !Number.isNaN(Date.parse(d + 'T12:00:00Z')) && new Date(d + 'T12:00:00Z').toISOString().slice(0, 10) === d;
    function validSnap(s) {
        const workouts = s.snapWorkouts ?? [], results = s.snapResults ?? {};
        if (!Array.isArray(workouts) || workouts.length > 50 || !results || typeof results !== 'object' || Array.isArray(results) || Object.keys(results).length > 50) return false;
        if (new Set(workouts.map(w => w.id)).size !== workouts.length) return false;
        if (!workouts.every(w => safeId(w.id) && (w.deadlineUtc == null || (isoInstant(w.deadlineUtc) && isoInstant(w.createdAtUtc) && Date.parse(w.deadlineUtc) > Date.parse(w.createdAtUtc))) && realDate(w.date) && typeof w.name === 'string' && w.name.trim().length > 0 && w.name.length <= 100 && cents(w.cap)
            && Array.isArray(w.exercises) && w.exercises.length > 0 && w.exercises.length <= 15 && new Set(w.exercises.map(e => e.id)).size === w.exercises.length
            && w.exercises.every(e => safeId(e.id) && safeId(e.catalogId) && typeof e.name === 'string' && e.name.length > 0 && e.name.length <= 250 && Number.isInteger(e.sets) && e.sets >= 1 && e.sets <= 10
                && Number.isInteger(e.reps) && e.reps >= 1 && e.reps <= 100 && cents(e.weight) && e.weight <= 2000 && ['fine', 'incentive'].includes(e.type) && Object.hasOwn(snapBases, e.basis) && cents(e.rate) && e.rate <= 100 && (e.completionReward == null || cents(e.completionReward)) && (e.extraRepRate == null || (cents(e.extraRepRate) && e.extraRepRate <= 100))))) return false;
        return Object.entries(results).every(([id, result]) => {
            const w = workouts.find(w => w.id === id);
            return w && result && typeof result.finalized === 'boolean' && (result.completedAtUtc == null || isoInstant(result.completedAtUtc)) && (!result.finalized || !w.deadlineUtc || isoInstant(result.completedAtUtc)) && result.sets && typeof result.sets === 'object' && !Array.isArray(result.sets)
                && Object.keys(result.sets).length === w.exercises.length && w.exercises.every(e => Array.isArray(result.sets[e.id]) && result.sets[e.id].length >= e.sets && result.sets[e.id].length <= 20
                    && result.sets[e.id].every(set => set && (set.reps === null ? !result.finalized : Number.isInteger(set.reps) && set.reps >= 0 && set.reps <= 1000)
                        && (set.weight === null ? !result.finalized : cents(set.weight) && set.weight <= 2000)));
        });
    }
    function wallTime(instant, zone = 'UTC') {
        const pieces = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(instant));
        const p = Object.fromEntries(pieces.map(x => [x.type, x.value])); return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`;
    }
    function wallToUtc(wall, zone = 'UTC', notBefore = null) {
        if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(wall)) return null;
        const wanted = Date.parse(wall + ':00Z'); if (!Number.isFinite(wanted)) return null;
        let guess = wanted;
        for (let i = 0; i < 4; i++) { const displayed = Date.parse(wallTime(guess, zone) + ':00Z'); const delta = wanted - displayed; if (!delta) break; guess += delta; }
        const candidates = [-7200000, -3600000, 0, 3600000, 7200000].map(delta => guess + delta).filter(value => wallTime(value, zone) === wall && (notBefore == null || value >= notBefore)).sort((a, b) => a - b);
        return candidates.length ? new Date(candidates[0]).toISOString() : null;
    }
    const isoInstant = value => typeof value === 'string' && /Z$/.test(value) && Number.isFinite(Date.parse(value));
    function assessSnap(s, evidence) {
        if (!evidence?.days?.length) return [];
        const first = evidence.days[0].date, last = evidence.days.at(-1).date, today = evidence.today;
        const rows = [];
        for (const w of (s.snapWorkouts || []).filter(w => w.date >= first && w.date <= last)) {
            const totals = { fine: 0, incentive: 0 }, result = s.snapResults?.[w.id];
            const limited = (type, value) => { const amount = Math.round(Math.min(value, Math.max(0, w.cap - totals[type])) * 100) / 100; totals[type] += amount; return amount; };
            for (const e of w.exercises) {
                const row = { id: `snap-${w.id}-${e.id}`, workoutId: w.id, title: `${w.name} · ${e.name}`, label: w.date, type: e.type, amount: 0, minimum: 0, cap: w.cap, settle: true, basis: e.basis };
                if (!activeOn(s, w.date)) { rows.push({ ...row, status: 'inactive', reason: 'Outside the saved agreement dates · no assessment' }); continue; }
                if (s.excused[w.date]) { rows.push({ ...row, status: 'excused', reason: 'Excused date · no assessment' }); continue; }
                const overdue = w.deadlineUtc && evidence.nowUtc && Date.parse(evidence.nowUtc) > Date.parse(w.deadlineUtc);
                if (w.date > today || !result?.finalized) { rows.push({ ...row, status: 'pending', reason: w.date > today ? 'Assignment date is in the future' : overdue ? 'Deadline passed; completion is unverified and needs review' : 'Completion has not been finalized' }); continue; }
                const recorded = result.sets?.[e.id];
                if (!Array.isArray(recorded) || recorded.length < e.sets || recorded.some(set => !known(set.reps) || !known(set.weight))) { rows.push({ ...row, status: 'pending', reason: 'Missing completed reps or weight' }); continue; }
                const totalReps = recorded.reduce((n, set) => n + (set.weight + 1e-7 >= e.weight ? Math.max(0, set.reps) : 0), 0);
                const totalSets = recorded.filter(set => set.reps >= e.reps && set.weight + 1e-7 >= e.weight).length;
                const assignedReps = e.sets * e.reps, completedReps = Math.min(assignedReps, totalReps), completedSets = Math.min(e.sets, totalSets);
                const units = { 'completed-reps': completedReps, 'completed-sets': completedSets, 'incomplete-reps': assignedReps - completedReps, 'incomplete-sets': e.sets - completedSets }[e.basis];
                const onTime = !w.deadlineUtc || (isoInstant(result.completedAtUtc) && Date.parse(result.completedAtUtc) <= Date.parse(w.deadlineUtc));
                const complete = completedSets === e.sets;
                const raw = e.type === 'incentive' && !onTime ? 0 : Math.round(units * e.rate * 100) / 100, amount = limited(e.type, raw);
                rows.push({ ...row, status: 'assessed', amount, units, completedReps, completedSets, totalReps, totalSets, onTime, assignedReps, assignedSets: e.sets, applies: units > 0,
                    reason: `${units} ${snapBases[e.basis].toLowerCase()} × ${e.rate.toFixed(2)}/unit${onTime ? '' : ' · completed after deadline or completion time unavailable; no bonus'}` });
                const extraReps = complete ? Math.max(0, totalReps - assignedReps) : 0;
                const reward = onTime && complete ? (e.completionReward || 0) + extraReps * (e.extraRepRate || 0) : 0;
                if ((e.completionReward || 0) > 0 || (e.extraRepRate || 0) > 0) rows.push({ ...row, id: row.id + '-reward', type: 'incentive', status: 'assessed', amount: limited('incentive', Math.round(reward * 100) / 100), units: extraReps,
                    onTime, applies: reward > 0, reason: !onTime ? 'Completion or extra-rep reward not earned: deadline missed or time unverified' : !complete ? 'Completion or extra-rep reward not earned: assigned sets unfinished' : `Completion reward ${(e.completionReward || 0).toFixed(2)} + ${extraReps} extra reps × ${(e.extraRepRate || 0).toFixed(2)}` });
            }
        }
        return rows;
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { catalog, defaults, valid, terms, outcome, assess, lateFee, validSnap, assessSnap, snapBases, activeOn, agreementEnd, periodBounds, wallTime, wallToUtc };
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
        const configured = JSON.parse(root.dataset.defaults || 'null');
        function initialState() {
            const initial = defaults();
            if (!configured) return initial;
            initial.rules = initial.rules.map(r => ({ ...r, ...configured.rules.find(d => d.id === r.id) }));
            initial.payment = { ...initial.payment, amount: configured.paymentAmount, lateValue: configured.lateValue, lateCap: configured.lateCap };
            return initial;
        }
        let state = initialState(), evidence = null, requestVersion = 0, evidenceReceivedAt = 0, serverRevision = 0, serverTerms = null, serverApproval = null, loaded = false, saveBusy = false;
        function notice(text) { if (status) status.textContent = text; }
        function acceptEnvelope(data) {
            const incoming = data.state || initialState();
            if (!valid(incoming)) throw new Error('The saved agreement could not be validated. Ask your coach to review it.');
            state = incoming; serverRevision = data.revision; serverTerms = data.terms; serverApproval = data.approval;
        }
        async function load() {
            loaded = false;
            try {
                const response = await fetch(api + '/state', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Could not load the saved coaching agreement. Confirm your coach connection and reload.');
                acceptEnvelope(await response.json()); loaded = true;
                const importButton = query('[data-import-browser-draft]');
                if (importButton) { try { importButton.hidden = serverRevision > 0 || !localStorage.getItem(key); } catch { importButton.hidden = true; } }
                return true;
            } catch (error) { notice(error.message); query('[data-snap-status]').textContent = error.message; return false; }
        }
        async function boot() {
            if (!await load()) return;
            drawEditor(); drawAgreementEditor(true); render();
            if (canAssignSnap) loadSnapLibrary();
            if (state.enabled || canAssignSnap || state.snapWorkouts.length) loadEvidence(query('[data-preview-week]').value);
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
                const nodes = [check, title, select(c.id + '-type', c.id === 'cheat-foods' ? ['fine'] : ['fine', 'incentive'], r.type, `${c.title}: type`), input(c.id + '-amount', r.amount, c.id === 'cheat-foods' ? '.01' : 0, 10000, '.01', `${c.title}: dollar amount`), select(c.id + '-frequency', frequencies, r.frequency, `${c.title}: assessment frequency`),
                    input(c.id + '-lower', r.lower, c.kind === 'macro' ? -100 : 0, c.kind === 'macro' ? 0 : 1000, c.kind === 'macro' ? '.1' : '1', `${c.title}: lower ${c.kind === 'macro' ? 'tolerance percent' : 'count'}`),
                    input(c.id + '-upper', r.upper, 0, c.kind === 'macro' ? 500 : 1000, c.kind === 'macro' ? '.1' : '1', `${c.title}: upper ${c.kind === 'macro' ? 'tolerance percent' : 'count'}`),
                    input(c.id + '-min', r.min, 0, 10000, '.01', `${c.title}: minimum dollars per assessment period`), input(c.id + '-max', r.max, 0, 10000, '.01', `${c.title}: maximum dollars per assessment period`)];
                if (c.id === 'cheat-foods') { nodes[5].readOnly = true; nodes[6].readOnly = true; }
                nodes.forEach(node => { const td = el('td'); td.append(node); row.append(td); }); body.append(row);
            });
            Object.entries(paymentNames).forEach(([field, prop]) => { form.elements[field].value = state.payment[prop]; });
        }
        function paymentText() {
            const p = state.payment, due = p.frequency === 'daily' ? 'each day' : p.frequency === 'weekly' ? `each ${p.day}` : `the first ${p.day} of each month`;
            const late = p.lateBasis === 'amount' ? money(p.lateValue) : `${number(p.lateValue)}% of unpaid principal`;
            return `${methods[p.method]} · ${money(p.amount)} ${p.frequency}, due ${due}. ${p.instructions ? 'Instructions: ' + p.instructions + '. ' : ''}Grace period: ${p.graceDays} days. Late charge: ${late} per full ${p.lateFrequency === 'monthly' ? '30-day' : p.lateFrequency === 'weekly' ? '7-day' : '1-day'} period after grace, capped at ${money(p.lateCap)} per late period, without compounding. Excused dates do not waive contract payments.`;
        }
        function render() {
            const banner = document.querySelector('.pending-snap-banner');
            if (banner) {
                banner.querySelectorAll('[data-pending-workout]').forEach(item => {
                    const w = state.snapWorkouts.find(w => w.id === item.dataset.pendingWorkout), result = w && state.snapResults[w.id];
                    if (w && result?.finalized && w.exercises.every(e => (result.sets[e.id] || []).filter(s => s.reps >= e.reps && s.weight >= e.weight).length >= e.sets)) item.remove();
                });
                const remaining = banner.querySelectorAll('[data-pending-workout]').length;
                if (!remaining) banner.remove();
                else banner.querySelector('strong').textContent = `${remaining} Snap Workout${remaining === 1 ? '' : 's'} to complete`;
            }
            const assigned = state.enabled ? state.rules.filter(r => r.assigned) : [];
            root.hidden = !editable && !reviewPage && !state.enabled && !state.snapWorkouts.length;
            query('[data-agreement-state]').textContent = state.enabled ? 'Enabled · Saved securely' : state.snapWorkouts.length ? 'Snap Workout · Saved securely' : 'Not enabled';
            query('[data-assigned-preview]').hidden = !state.enabled && !state.snapWorkouts.length;
            query('[data-exceptions-panel]').hidden = !canAssignSnap && !state.enabled && !state.snapWorkouts.length;
            query('[data-snap-workout]').hidden = !canAssignSnap && !state.snapWorkouts.length;
            query('[data-no-agreement]').hidden = state.enabled || canAssignSnap || state.snapWorkouts.length > 0;
            query('[data-assignment-summary]').textContent = `${state.enabled ? assigned.length + ' assigned adherence rules' : 'Adherence rules disabled; Snap terms shown separately'} · USD · Lower and upper endpoints are included. Weekly macro checks compare summed actual intake with summed prescribed targets for non-excused days.`;
            const list = query('[data-assigned-rules]'); list.replaceChildren();
            assigned.forEach(r => {
                const c = catalog.find(c => c.id === r.id), card = el('article', undefined, 'accountability-rule-card');
                const criterion = c.kind === 'macro' ? `Allowed deviation ${r.lower}% to +${r.upper}% (${100 + r.lower}%–${100 + r.upper}% of target)` : `${r.id === 'cheat-foods' ? 'Every occurrence is paid; no allowance' : r.lower + '–' + r.upper + ' occurrences per ' + r.frequency + ' assessment'}`;
                card.append(el('strong', c.title), el('p', `${r.type === 'fine' ? 'Fine' : 'Incentive'} · ${money(r.amount)} ${r.frequency}`, 'accountability-kind'), el('p', criterion), el('p', `Minimum to settle ${money(r.min)} · ${r.frequency} cap ${money(r.max)}`), el('small', c.description)); list.append(card);
            });
            renderAgreement();
            query('[data-payment-summary]').textContent = query('[data-agreement-dates-summary]').textContent + ' ' + paymentText();
            renderAgreement(); renderAssessments(); renderSnapAssignments(); renderLateFee(); checkApproval();
        }
        function renderAssessments() {
            const ledger = query('[data-assessment-ledger]'); ledger.replaceChildren();
            if (!evidence) { query('[data-snap-weekly-total]').textContent = 'Weekly total pending: select a week to load its dates.'; query('[data-macro-evidence]').replaceChildren(); query('[data-assessment-total]').textContent = 'Awaiting evidence; no assessment can be calculated.'; return; }
            const rows = [...assess(state, evidence), ...assessSnap(state, evidence)];
            const fine = rows.filter(x => x.type === 'fine' && x.countedInWeek !== false).reduce((n, x) => n + x.amount, 0), incentive = rows.filter(x => x.type === 'incentive' && x.countedInWeek !== false).reduce((n, x) => n + x.amount, 0), pending = rows.filter(x => x.status === 'pending').length;
            query('[data-assessment-total]').textContent = `Selected-week preview: fines ${money(fine)} · incentives ${money(incentive)} · ${pending} assessments pending. Calculated amounts; no automatic payment collection.`;
            const snapRows = rows.filter(x => x.workoutId);
            const snapFine = snapRows.filter(x => x.type === 'fine').reduce((n, x) => n + x.amount, 0), snapIncentive = snapRows.filter(x => x.type === 'incentive').reduce((n, x) => n + x.amount, 0);
            query('[data-snap-weekly-total]').textContent = `Week of ${dayLabel(evidence.weekStart)} · Snap fines ${money(snapFine)} · Snap incentives ${money(snapIncentive)} · Combined weekly fines ${money(fine)} · Combined weekly incentives ${money(incentive)}. Calculated totals; no payment collected.`;
            rows.filter(x => x.amount > 0 || x.status === 'pending').forEach(x => ledger.append(el('li', `${x.title} · ${x.label || 'Selected week'} · ${x.status === 'pending' ? 'Pending: ' + x.reason : `${x.type === 'fine' ? 'Fine' : 'Incentive'} ${money(x.amount)}${x.settle ? '' : ' · below minimum settlement threshold'}${x.countedInWeek === false ? ' · counts in the week this period ends' : ''}`}`)));
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
                const monthly = state.enabled && state.rules.some(r => r.assigned && r.frequency === 'monthly');
                const response = await fetch(api + '/evidence?' + (date ? 'date=' + encodeURIComponent(date) + '&' : '') + 'monthly=' + monthly, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
                const result = await response.json(); if (version !== requestVersion) return; evidence = result; evidenceReceivedAt = Date.now();
                query('[data-preview-week]').value = date || result.today; query('[data-preview-week]').max = result.today;
                query('[data-evidence-status]').textContent = `Week of ${dayLabel(result.weekStart)}. ${result.notice || 'Daily targets and saved logs loaded.'}`;
                drawAgreementEditor(); renderAgreement(); drawWeek(); renderAssessments(); renderSnapAssignments();
            } catch { if (version === requestVersion) { query('[data-evidence-status]').textContent = 'Could not load evidence. No amounts are inferred; sign in again or choose a valid date within the last 12 weeks.'; drawWeek(); renderAssessments(); } }
        }
        function renderLateFee() {
            const p = query('[data-unpaid]'), d = query('[data-overdue]');
            const calculation = p.value !== '' && d.value !== '' ? lateFee(state.payment, Number(p.value), Number(d.value)) : null;
            query('[data-late-result]').textContent = calculation ? `${calculation.periods} full late period(s) after grace · late fee preview ${money(calculation.fee)}. The cap applies separately to each late period. Monthly late periods use 30 days in this sample. No fee is charged.` : 'Enter valid principal and whole days past due.';
        }
        async function post(path, fields) {
            const body = new URLSearchParams({ ...fields, __RequestVerificationToken: token() });
            const response = await fetch(api + path, { method: 'POST', credentials: 'same-origin', cache: 'no-store', body, headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) {
                let detail = '';
                try { if (response.headers.get('content-type')?.includes('application/json')) detail = (await response.json()).error || ''; } catch {}
                throw new Error(detail || (response.status === 409 ? 'This agreement changed on another device. Reload saved settings before saving.' : response.status === 429 ? 'Too many requests. Please wait before trying again.' : 'Could not save. Check your entries, credentials and coach connection.'));
            }
            return response.json();
        }
        function checkApproval() {
            const output = query('[data-approval-state]');
            output.textContent = serverApproval && loaded
                ? `Approved by ${serverApproval.signedBy} · ${new Date(serverApproval.approvedAt).toLocaleString()} · Saved terms version ${serverApproval.termsVersion}.`
                : 'Awaiting client approval of the current saved terms. Calculated amounts are not a payment demand.';
        }
        const agreementForm = query('[data-agreement-form]');
        const serverNow = () => evidence?.nowUtc ? Date.parse(evidence.nowUtc) + Math.max(0, Date.now() - evidenceReceivedAt) : Date.now();
        let deadlineTouched = false;
        function defaultDeadline() {
            const now = serverNow(), zone = evidence?.timeZone || 'UTC';
            const chosen = snapForm?.elements.snapDate.value;
            if (chosen && evidence?.today && chosen > evidence.today) { const wall = chosen + 'T' + wallTime(now, zone).slice(11); const start = wallToUtc(wall, zone); if (start) return Date.parse(start) + 7200000; }
            return now + 7200000;
        }
        function readAgreementFields() {
            if (!agreementForm) return state.agreement;
            const mode = agreementForm.elements.agreementMode.value;
            return { start: agreementForm.elements.agreementStart.value, mode, weeks: Number(agreementForm.elements.agreementWeeks.value), endedOn: mode === 'open' ? state.agreement.endedOn : null };
        }
        function drawAgreementEditor(force = false) {
            if (!agreementForm) return;
            if (force || !agreementForm.elements.agreementStart.value) agreementForm.elements.agreementStart.value = state.agreement.start || evidence?.today || '';
            if (force) { agreementForm.elements.agreementMode.value = state.agreement.mode; agreementForm.elements.agreementWeeks.value = state.agreement.weeks; }
            agreementForm.elements.agreementWeeks.disabled = agreementForm.elements.agreementMode.value === 'open';
        }
        function renderAgreement() {
            const a = state.agreement, until = agreementEnd(state);
            query('[data-agreement-dates-summary]').textContent = !a.start ? 'Set and save an agreement start date before any fees or rewards can apply.' : `Starts ${dayLabel(a.start)} · ${until ? 'Ends after ' + dayLabel(until) : 'Until ended'}${a.mode === 'weeks' ? ' · ' + a.weeks + ' weeks' : ''}. No assessments outside these dates. Period caps reset daily, each Monday, or on the first of each month.`;
            if (agreementForm) { const end = query('[data-end-agreement]'); end.disabled = !a.start || a.mode !== 'open' || !!a.endedOn; }
        }
        if (agreementForm) {
            agreementForm.elements.agreementMode.addEventListener('change', () => drawAgreementEditor());
            agreementForm.addEventListener('submit', async event => { event.preventDefault(); if (!agreementForm.reportValidity()) return; const next = { ...state, agreement: readAgreementFields() }; if (await persistSnap(next, 'Agreement dates saved.')) { drawAgreementEditor(true); query('[data-agreement-dates-status]').textContent = 'Saved. Changed agreement dates require fresh client approval.'; } });
            query('[data-end-agreement]').addEventListener('click', async () => { if (!evidence?.today || !state.agreement.start || evidence.today < state.agreement.start) { query('[data-agreement-dates-status]').textContent = 'The agreement has not started yet.'; return; } if (await persistSnap({ ...state, agreement: { ...state.agreement, endedOn: evidence.today } }, 'Agreement ends after today.')) query('[data-agreement-dates-status]').textContent = 'Saved: no new assessments after today.'; });
        }
        let snapDraft = [], exerciseOptions = [], catalogRequest = 0;
        const snapForm = query('[data-snap-assign]');
        async function persistSnap(next, message, workoutId = null) {
            if (!loaded) { query('[data-snap-status]').textContent = 'Load the saved agreement before saving changes.'; return false; }
            if (saveBusy) { query('[data-snap-status]').textContent = 'A save is already in progress. Please wait.'; return false; }
            if (!valid(next) || terms(next).length > 32768 || ((next.enabled || next.snapWorkouts.length) && !next.agreement.start)) { query('[data-snap-status]').textContent = 'Check agreement dates, targets, rates and completed-set entries.'; return false; }
            if (workoutId === null && !canAssignSnap) { query('[data-snap-status]').textContent = 'Only the assigned coach can change the agreement.'; return false; }
            saveBusy = true; const changed = terms(next) !== serverTerms;
            try {
                const fields = workoutId === null ? { ExpectedRevision: serverRevision, StateJson: JSON.stringify(next), Terms: terms(next) }
                    : { ExpectedRevision: serverRevision, WorkoutId: workoutId, ResultJson: JSON.stringify(next.snapResults[workoutId]) };
                const result = await post(workoutId === null ? '/state/coach' : '/state/result', fields);
                acceptEnvelope(result); drawEditor(); drawAgreementEditor(true); render(); drawWeek();
                query('[data-snap-status]').textContent = message + (changed ? ' Current terms require client approval.' : '') + ' Saved securely and available on other devices. No payment was collected.';
                try { localStorage.setItem(key + ':server-change', String(Date.now())); } catch {}
                return true;
            } catch (error) { query('[data-snap-status]').textContent = error.message; notice(error.message); return false; }
            finally { saveBusy = false; }
        }
        function readSnapDraft() {
            if (!snapForm) return;
            snapDraft = snapDraft.map(e => {
                const val = field => snapForm.elements[`snap-${e.id}-${field}`].value;
                return { ...e, sets: Number(val('sets')), reps: Number(val('reps')), weight: Number(val('weight')), type: val('type'), basis: val('basis'), rate: Number(val('rate')), completionReward: Number(val('completionReward')), extraRepRate: Number(val('extraRepRate')) };
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
                    input(`snap-${e.id}-rate`, e.rate, 0, 100, '.01', `${e.name}: dollars per assessed rep or set`), input(`snap-${e.id}-completionReward`, e.completionReward || 0, 0, 10000, '.01', `${e.name}: reward for completing this exercise`), input(`snap-${e.id}-extraRepRate`, e.extraRepRate || 0, 0, 100, '.01', `${e.name}: reward per extra completed rep`), remove];
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
            const today = evidence?.today, zone = evidence?.timeZone || 'UTC';
            if (snapForm && today) {
                if (!snapForm.elements.snapDate.value) snapForm.elements.snapDate.value = today;
                snapForm.elements.snapDate.min = shiftDate(today, -84); snapForm.elements.snapDate.max = shiftDate(today, 84);
                if (!deadlineTouched) snapForm.elements.snapDeadline.value = wallTime(defaultDeadline(), zone);
                query('[data-snap-deadline-zone]').textContent = `Deadline time zone: ${zone}. Default: two hours after assignment.`;
            }
            initializeReuseDates();
            if (!state.snapWorkouts.length) { container.append(el('p', 'No Snap Workouts assigned yet.')); return; }
            const week = evidence?.days?.length ? evidence.days : null, assessed = evidence ? assessSnap(state, evidence) : [];
            [...state.snapWorkouts].sort((a, b) => b.date.localeCompare(a.date)).forEach(w => {
                const article = el('article', undefined, 'snap-assignment'); article.id = 'snap-' + w.id; article.append(el('h4', `${w.name} · ${dayLabel(w.date)}`), el('p', `Assessment cap ${money(w.cap)} per type for this workout${state.excused[w.date] ? ' · EXCUSED: no assessment' : ''}.`));
                article.append(el('p', w.deadlineUtc ? `Completion deadline: ${wallTime(w.deadlineUtc, zone).replace('T', ' ')} (${zone}).` : 'Earlier assignment: no deadline was configured.'));
                if (week && (w.date < week[0].date || w.date > week.at(-1).date)) article.append(el('p', 'Outside the selected weekly total. Choose this assignment’s week to view its assessment.'));
                const resultForm = el('form'), previous = state.snapResults[w.id], canRecord = canRecordSnap && today && w.date <= today;
                const recordBodies = {}, rowCounts = {};
                function drawSetRows(e, records) {
                    const body = recordBodies[e.id]; body.replaceChildren(); rowCounts[e.id] = records.length;
                    records.forEach((saved, i) => {
                        const row = el('tr'); row.append(el('th', String(i + 1)), el('td', i < e.sets ? String(e.reps) : 'Extra set'), el('td', number(e.weight)));
                        if (canRecord) {
                            const reps = input(`${e.id}-${i}-reps`, saved?.reps ?? '', 0, 1000, 1, `${e.name}, set ${i + 1}: completed reps`), weight = input(`${e.id}-${i}-weight`, saved?.weight ?? '', 0, 2000, '.01', `${e.name}, set ${i + 1}: weight used in pounds`);
                            reps.required = false; weight.required = false; [reps, weight].forEach(n => { const td = el('td'); td.append(n); row.append(td); });
                        } else row.append(el('td', number(saved?.reps)), el('td', number(saved?.weight)));
                        body.append(row);
                    });
                }
                w.exercises.forEach(e => {
                    const section = el('section', undefined, 'snap-exercise');
                    section.append(el('h5', e.name), el('p', `${e.sets} sets × ${e.reps} reps · ${number(e.weight)} lb · ${e.type === 'fine' ? 'Penalty/fee' : 'Base incentive'} ${money(e.rate)} per ${snapBases[e.basis].toLowerCase().replace(/s$/, '')}. Completion reward ${money(e.completionReward || 0)} + ${money(e.extraRepRate || 0)} per extra qualifying rep, earned only when the assigned sets are finished on time.`));
                    const records = previous?.sets?.[e.id] || Array.from({ length: e.sets }, () => ({ reps: null, weight: null }));
                    if (canRecord) {
                        const quick = el('div', undefined, 'snap-quick-entry accountability-fields'), setsInput = input(`quick-${e.id}-sets`, '', 0, 20, 1, `${e.name}: completed sets`), repsInput = input(`quick-${e.id}-reps`, '', 0, 1000, 1, `${e.name}: completed reps per set`), weightInput = input(`quick-${e.id}-weight`, '', 0, 2000, '.01', `${e.name}: completed weight in pounds`);
                        [setsInput, repsInput, weightInput].forEach(n => { n.required = false; });
                        [['Completed sets', setsInput], ['Reps per completed set', repsInput], ['Weight used (lb)', weightInput]].forEach(([text, n]) => { const label = el('label', text); label.append(n); quick.append(label); });
                        const apply = el('button', 'Apply completed sets / reps / weight'); apply.type = 'button'; apply.dataset.snapQuickApply = e.id;
                        apply.addEventListener('click', () => {
                            if ([setsInput, repsInput, weightInput].some(n => n.value === '' || !n.reportValidity())) { query('[data-snap-status]').textContent = 'Enter valid completed sets, reps and weight before applying.'; return; }
                            const count = Number(setsInput.value), reps = Number(repsInput.value), weight = Number(weightInput.value);
                            drawSetRows(e, Array.from({ length: Math.max(e.sets, count) }, (_, i) => i < count ? { reps, weight } : { reps: 0, weight: 0 }));
                            query('[data-snap-status]').textContent = 'Set rows filled. Review them and save the workout results.';
                        }); quick.append(apply); section.append(quick);
                    }
                    const wrapper = el('div', undefined, 'accountability-scroll'), table = el('table'), heading = el('thead'), headRow = el('tr');
                    ['Set', 'Target reps', 'Target lb', 'Completed reps', 'Used lb'].forEach(text => headRow.append(el('th', text))); heading.append(headRow); table.append(heading); const body = el('tbody'); recordBodies[e.id] = body;
                    table.append(body); wrapper.append(table); section.append(wrapper); drawSetRows(e, records);
                    assessed.filter(x => x.workoutId === w.id && (x.id === `snap-${w.id}-${e.id}` || x.id === `snap-${w.id}-${e.id}-reward`)).forEach(row => section.append(el('p', row.status === 'assessed' ? `${row.reason} · ${row.type} preview ${money(row.amount)}` : row.reason)));
                    resultForm.append(section);
                });
                if (canRecord) {
                    const finalLabel = el('label', undefined, 'accountability-enable'), finalized = el('input'); finalized.type = 'checkbox'; finalized.name = 'finalized'; finalized.checked = !!previous?.finalized; finalLabel.append(finalized, el('span', 'Finished — these are the final recorded sets.'));
                    const completionLabel = el('label', `Completion date and time (${zone})`), completionInput = el('input'); completionInput.type = 'datetime-local'; completionInput.name = 'completedAt'; completionInput.value = wallTime(previous?.completedAtUtc || serverNow(), zone); let editedTime = !!previous?.completedAtUtc; completionInput.addEventListener('input', () => { editedTime = true; }); completionLabel.append(completionInput);
                    resultForm.append(finalLabel, completionLabel, el('p', 'Enter reps and weight for every set before marking finished. Use 0 reps for an uncompleted set. Quick entry can include extra sets; individual rows can be adjusted.'));
                    const save = el('button', 'Save workout results'); save.type = 'submit'; resultForm.append(save);
                    resultForm.addEventListener('submit', async event => {
                        event.preventDefault(); if (!resultForm.reportValidity()) return;
                        const result = { finalized: finalized.checked, sets: {} };
                        if (result.finalized) {
                            if (!editedTime) completionInput.value = wallTime(serverNow(), zone);
                            const completed = editedTime ? wallToUtc(completionInput.value, zone, w.createdAtUtc ? Date.parse(w.createdAtUtc) - 60000 : null) : new Date(serverNow()).toISOString();
                            if (!completed || Date.parse(completed) > serverNow() + 60000 || (w.createdAtUtc && Date.parse(completed) < Date.parse(w.createdAtUtc) - 60000)) { query('[data-snap-status]').textContent = 'Enter a real completion time after assignment and no later than the current time.'; return; }
                            result.completedAtUtc = completed;
                        }
                        for (const e of w.exercises) result.sets[e.id] = Array.from({ length: rowCounts[e.id] }, (_, i) => { const get = field => { const value = resultForm.elements[`${e.id}-${i}-${field}`].value; return value === '' ? null : Number(value); }; return { reps: get('reps'), weight: get('weight') }; });
                        if (result.finalized && Object.values(result.sets).some(sets => sets.some(set => set.reps === null || set.weight === null))) { query('[data-snap-status]').textContent = 'Final results need reps and weight for every set. Enter 0 reps for an uncompleted set, or save progress.'; return; }
                        await persistSnap({ ...state, snapResults: { ...state.snapResults, [w.id]: result } }, 'Workout results saved. Weekly penalties and rewards updated.', w.id);
                    });
                } else if (w.date > today) resultForm.append(el('p', 'Record results on or after the assignment date.'));
                else if (previous?.completedAtUtc) resultForm.append(el('p', `Completed: ${wallTime(previous.completedAtUtc, zone).replace('T', ' ')} (${zone}).`));
                article.append(resultForm);
                if (canAssignSnap) { const remove = el('button', 'Remove assignment'); remove.type = 'button'; remove.className = 'secondary'; remove.addEventListener('click', async () => { const results = { ...state.snapResults }; delete results[w.id]; await persistSnap({ ...state, snapWorkouts: state.snapWorkouts.filter(x => x.id !== w.id), snapResults: results }, 'Snap Workout removed.'); }); article.append(remove); }
                container.append(article);
                if (location.hash === '#' + article.id) {
                    for (let parent = article.parentElement; parent; parent = parent.parentElement) if (parent.tagName === 'DETAILS') parent.open = true;
                    requestAnimationFrame(() => article.scrollIntoView({ block: 'start' }));
                }
            });
        }
        const reuseForm = query('[data-snap-reuse]');
        let templates = [], reuseDeadlineTouched = false;
        function initializeReuseDates() {
            if (!reuseForm || !evidence) return;
            const date = reuseForm.elements.reuseDate, deadline = reuseForm.elements.reuseDeadline;
            if (!date.value) date.value = evidence.today;
            date.min = shiftDate(evidence.today, -84); date.max = shiftDate(evidence.today, 84);
            if (!reuseDeadlineTouched) {
                const wall = wallTime(serverNow() + 2 * 3600000, evidence.timeZone || 'UTC');
                deadline.value = date.value > evidence.today ? date.value + wall.slice(10) : wall;
            }
            query('[data-reuse-zone]').textContent = `Deadline time zone: ${evidence.timeZone || 'UTC'}.`;
        }
        async function loadSnapLibrary() {
            if (!reuseForm) return;
            const output = query('[data-snap-library-status]');
            try {
                const response = await fetch(api + '/snap-library', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Could not load past workouts. Reload saved agreement to retry.');
                templates = await response.json();
                const list = query('[data-snap-library]'); list.replaceChildren();
                templates.forEach((workout, i) => {
                    const label = el('label', undefined, 'snap-library-choice'), check = el('input'), text = el('span');
                    check.type = 'checkbox'; check.name = 'template'; check.value = i;
                    text.append(el('strong', workout.name), el('p', workout.exercises.map(e => `${e.name}: ${e.sets} × ${e.reps} at ${number(e.weight)} lb · ${e.type} ${money(e.rate)} per ${snapBases[e.basis].toLowerCase()} · completion reward ${money(e.completionReward || 0)} · extra rep ${money(e.extraRepRate || 0)}`).join('; ')), el('small', `Cap ${money(workout.cap)} per type`));
                    label.append(check, text); list.append(label);
                });
                output.textContent = templates.length ? 'Select workouts to assign again. The most recent versions appear first.' : 'No past workouts available yet. Create your first Snap Workout below.';
            } catch (error) { output.textContent = error.message; }
        }
        if (reuseForm) {
            reuseForm.elements.reuseDeadline.addEventListener('input', () => { reuseDeadlineTouched = true; });
            reuseForm.elements.reuseDate.addEventListener('change', initializeReuseDates);
            reuseForm.addEventListener('submit', async event => {
                event.preventDefault();
                if (!reuseForm.reportValidity()) return;
                const selected = [...reuseForm.querySelectorAll('input[name=template]:checked')].map(x => templates[Number(x.value)]);
                const output = query('[data-snap-library-status]');
                if (!selected.length) { output.textContent = 'Check at least one workout to assign.'; return; }
                if (!evidence?.today) { output.textContent = 'Load a valid week before assigning workouts.'; return; }
                if (state.snapWorkouts.length + selected.length > 50) { output.textContent = 'Use up to 50 assignments per client. Remove older assignments before adding more.'; return; }
                if (!reuseDeadlineTouched) initializeReuseDates();
                const date = reuseForm.elements.reuseDate.value, deadline = wallToUtc(reuseForm.elements.reuseDeadline.value, evidence.timeZone || 'UTC', serverNow());
                if (!deadline || Date.parse(deadline) <= serverNow() || reuseForm.elements.reuseDeadline.value.slice(0, 10) < date) { output.textContent = 'Choose a future deadline on or after the assignment date.'; return; }
                const copies = selected.map(w => ({ ...w, id: crypto.randomUUID(), date, createdAtUtc: new Date(serverNow()).toISOString(), deadlineUtc: deadline,
                    exercises: w.exercises.map(e => ({ ...e, id: crypto.randomUUID() })) }));
                output.textContent = 'Saving selected workouts…';
                if (await persistSnap({ ...state, agreement: readAgreementFields(), snapWorkouts: [...state.snapWorkouts, ...copies] }, `${copies.length} Snap Workout(s) assigned.`)) {
                    output.textContent = `${copies.length} workout(s) assigned with fresh results and deadlines.`;
                    reuseForm.querySelectorAll('input[name=template]').forEach(x => { x.checked = false; });
                } else output.textContent = query('[data-snap-status]').textContent;
            });
        }
        if (snapForm) {
            snapForm.elements.snapCap.value = configured?.snapCap ?? 100;
            snapForm.elements.snapDeadline.addEventListener('input', () => { deadlineTouched = true; });
            snapForm.elements.snapDate.addEventListener('change', () => { if (!deadlineTouched && evidence) snapForm.elements.snapDeadline.value = wallTime(defaultDeadline(), evidence.timeZone || 'UTC'); });
            query('[data-snap-search-button]').addEventListener('click', searchExercises);
            query('[data-snap-search]').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); searchExercises(); } });
            query('[data-snap-add]').addEventListener('click', () => {
                const chosen = exerciseOptions.find(e => e.id === query('[data-snap-picker]').value);
                if (!chosen) { query('[data-snap-catalog-status]').textContent = 'Choose an exercise first.'; return; }
                if (snapDraft.length >= 15) { query('[data-snap-catalog-status]').textContent = 'Use up to 15 exercises per Snap Workout.'; return; }
                readSnapDraft(); snapDraft.push({ id: crypto.randomUUID(), catalogId: chosen.id, name: chosen.name, sets: 3, reps: 10, weight: 0, type: 'fine', basis: 'incomplete-reps', rate: configured?.snapRate ?? .1, completionReward: configured?.completionReward ?? 0, extraRepRate: configured?.extraRepRate ?? 0 }); drawSnapDraft();
            });
            snapForm.addEventListener('submit', async event => {
                event.preventDefault(); if (!snapForm.reportValidity()) return;
                if (!evidence?.today) { query('[data-snap-status]').textContent = 'Load a valid week before assigning a workout.'; return; }
                readSnapDraft();
                const autoDeadline = deadlineTouched ? null : defaultDeadline();
                if (autoDeadline != null) snapForm.elements.snapDeadline.value = wallTime(autoDeadline, evidence.timeZone || 'UTC');
                const deadline = autoDeadline != null ? new Date(autoDeadline).toISOString() : wallToUtc(snapForm.elements.snapDeadline.value, evidence.timeZone || 'UTC', serverNow());
                if (!deadline || Date.parse(deadline) <= serverNow() || snapForm.elements.snapDeadline.value.slice(0, 10) < snapForm.elements.snapDate.value) { query('[data-snap-status]').textContent = 'Choose a future deadline on or after the assignment date.'; return; }
                const workout = { id: crypto.randomUUID(), deadlineUtc: deadline, createdAtUtc: new Date(serverNow()).toISOString(), date: snapForm.elements.snapDate.value, name: snapForm.elements.snapName.value.trim(), cap: Number(snapForm.elements.snapCap.value), exercises: snapDraft.map(e => ({ ...e })) };
                if (await persistSnap({ ...state, agreement: readAgreementFields(), snapWorkouts: [...state.snapWorkouts, workout] }, 'Snap Workout assigned. The client can open their portal on any device to record results.')) { snapDraft = []; deadlineTouched = false; drawSnapDraft(); loadSnapLibrary(); }
            });
            drawSnapDraft(); searchExercises();
        }
        const saveExceptions = query('[data-save-exceptions]');
        if (saveExceptions) saveExceptions.addEventListener('click', async () => {
            if (!canAssignSnap) return;
            if (await persistSnap(state, 'Fee exceptions saved.')) query('[data-exceptions-status]').textContent = 'Saved for these exact dates. Checked dates have no adherence or Snap Workout assessment.';
        });
        query('[data-reload-agreement]').addEventListener('click', () => boot());
        boot();
        query('[data-load-week]').addEventListener('click', () => { const date = query('[data-preview-week]'); if (date.value && date.reportValidity()) loadEvidence(date.value); });
        query('[data-unpaid]').addEventListener('input', renderLateFee); query('[data-overdue]').addEventListener('input', renderLateFee);
        if (form) {
            form.addEventListener('input', () => notice('Unsaved changes. Save to update the agreement and assessment preview.'));
            form.addEventListener('submit', async event => {
                event.preventDefault(); if (!form.reportValidity() || [...root.querySelectorAll('[data-reviewed-count]')].some(n => !n.reportValidity())) return;
                const next = { ...state, agreement: readAgreementFields(), enabled: form.elements.enabled.checked, rules: catalog.map(c => { const f = suffix => form.elements[c.id + '-' + suffix]; return { id: c.id, assigned: f('assigned').checked, type: f('type').value, amount: Number(f('amount').value), frequency: f('frequency').value, lower: Number(f('lower').value), upper: Number(f('upper').value), min: Number(f('min').value), max: Number(f('max').value) }; }), payment: {} };
                Object.entries(paymentNames).forEach(([field, prop]) => { next.payment[prop] = ['amount', 'graceDays', 'lateValue', 'lateCap'].includes(prop) ? Number(form.elements[field].value) : form.elements[field].value; });
                if (!valid(next) || (next.enabled && !next.agreement.start) || terms(next).length > 32768) { notice('Set a valid agreement start date and correct the criteria and totals: lower ≤ upper, minimum total ≤ maximum total, whole occurrence counts, and percentage late charges between 0% and 100%.'); return; }
                if (await persistSnap(next, 'Agreement settings saved.')) {
                    notice('Saved securely. Changed terms require fresh client approval.');
                    if (state.enabled || state.snapWorkouts.length) loadEvidence(query('[data-preview-week]').value);
                }
            });
            query('[data-reset-preview]').addEventListener('click', () => boot());
        }
        const importButton = query('[data-import-browser-draft]');
        if (importButton) importButton.addEventListener('click', () => {
            if (!canAssignSnap || serverRevision !== 0) return;
            try {
                const draft = JSON.parse(localStorage.getItem(key));
                draft.agreement ??= { start: '', mode: 'open', weeks: 12, endedOn: null };
                draft.snapWorkouts ??= []; draft.snapResults ??= {};
                draft.rules = draft.rules.map(r => r.id === 'cheat-foods' ? { ...r, type: 'fine', lower: 0, upper: 0, amount: r.assigned && r.amount === 0 ? 2 : r.amount } : r);
                if (!valid(draft)) throw new Error();
                state = draft; drawEditor(); drawAgreementEditor(true); render(); drawWeek();
                query('[data-snap-status]').textContent = 'Old browser draft loaded for review. Save the reviewed agreement to store it securely. Previous browser approvals are not imported.';
            } catch { query('[data-snap-status]').textContent = 'The old browser draft could not be imported.'; }
        });
        const approvalForm = query('[data-approval-form]');
        if (approvalForm) approvalForm.addEventListener('submit', async event => {
            event.preventDefault(); if (!approvalForm.reportValidity()) return;
            if (!state.agreement.start) { query('[data-approval-status]').textContent = 'Your coach must save an agreement start date before approval.'; return; }
            const current = terms(state), fields = { Username: approvalForm.elements.username.value, Password: approvalForm.elements.password.value, Agree: 'true', Terms: current, ExpectedRevision: serverRevision }, button = approvalForm.querySelector('button');
            approvalForm.elements.password.value = ''; button.disabled = true; query('[data-approval-status]').textContent = 'Verifying your credentials securely…';
            try {
                const result = await post('/approve', fields); fields.Password = '';
                if (current !== terms(state)) throw new Error('Terms changed during approval. Review and approve the new terms.');
                acceptEnvelope(result); render();
                query('[data-approval-status]').textContent = 'Credentials verified. Approval of these saved terms is recorded securely. Your password was not stored.';
            } catch (error) { query('[data-approval-status]').textContent = error.message || 'Approval could not be saved. No agreement was recorded.'; }
            finally { fields.Password = ''; button.disabled = false; }
        });
        window.addEventListener('storage', event => { if (event.key === key + ':server-change') { notice('A newer agreement was saved in another tab. Reload saved agreement before saving.'); query('[data-snap-status]').textContent = 'A newer save is available. Reload saved agreement before making further changes.'; } });
        window.addEventListener('pageshow', event => { if (event.persisted) boot(); });
    }
    document.querySelectorAll('[data-accountability]').forEach(initialize);
})();
