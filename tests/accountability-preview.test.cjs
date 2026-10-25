const { test } = require('node:test');
const assert = require('node:assert/strict');
const { defaults, valid, terms, assess, lateFee } = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
function sample() {
    const s = defaults(); s.enabled = true; s.rules[1].assigned = true;
    const evidence = { days: Array.from({ length: 7 }, (_, i) => ({ date: `2026-10-${12 + i}`, completed: true, actual: { protein: 190 }, target: { protein: 200 } })) };
    return { s, evidence };
}
test('inclusive -5% and +5% boundaries; either side outside generates a fine', () => {
    const { s, evidence } = sample(); assert.equal(assess(s, evidence).reduce((n, r) => n + r.amount, 0), 0);
    evidence.days[0].actual.protein = 210; assert.equal(assess(s, evidence)[0].status, 'within');
    evidence.days[0].actual.protein = 189.9; assert.equal(assess(s, evidence)[0].amount, 2);
    evidence.days[0].actual.protein = 210.1; assert.equal(assess(s, evidence)[0].amount, 2);
});
test('missing evidence remains pending, zero actual intake is a known value, uncompleted days do not accrue', () => {
    const { s, evidence } = sample(); evidence.days[0].actual.protein = null; evidence.days[1].target = null; evidence.days[2].actual.protein = 0; evidence.days[3].completed = false;
    const rows = assess(s, evidence); assert.equal(rows[0].status, 'pending'); assert.equal(rows[1].status, 'pending'); assert.equal(rows[2].amount, 2); assert.equal(rows[3].amount, 0); assert.equal(rows[3].status, 'pending');
});
test('date-specific excuses remove fines and do not recur on another week', () => {
    const { s, evidence } = sample(); evidence.days[0].actual.protein = 150; s.excused['2026-10-12'] = true;
    assert.equal(assess(s, evidence)[0].status, 'excused'); assert.equal(assess(s, evidence)[0].amount, 0);
    evidence.days[0].date = '2026-10-19'; assert.equal(assess(s, evidence)[0].amount, 2);
});
test('weekly macros sum varying daily prescriptions; excuses remove actuals and targets', () => {
    const { s, evidence } = sample(); s.rules[1].frequency = 'weekly';
    evidence.days[0].actual.protein = 150; evidence.days[0].target.protein = 150;
    evidence.days[1].actual.protein = 500; s.excused[evidence.days[1].date] = true;
    const row = assess(s, evidence)[0]; assert.equal(row.value, 1100); assert.equal(row.target, 1150); assert.equal(row.status, 'within');
    evidence.days[6].completed = false; assert.equal(assess(s, evidence)[0].status, 'pending');
});
test('occurrence allowance of two: two passes, three incurs fine; blank count is pending', () => {
    const { s, evidence } = sample(); s.rules[1].assigned = false; const r = s.rules.find(r => r.id === 'cheat-foods'); r.assigned = true;
    s.observations[evidence.days[0].date] = { 'cheat-foods': 2 }; s.observations[evidence.days[1].date] = { 'cheat-foods': 3 };
    const rows = assess(s, evidence); assert.equal(rows[0].amount, 0); assert.equal(rows[1].amount, 2); assert.equal(rows[2].status, 'pending');
});
test('rule accumulation cap stops extra amounts; minimum never imposes a charge floor', () => {
    const { s, evidence } = sample(); s.rules[1].max = 5; s.rules[1].min = 5; evidence.days.forEach(d => d.actual.protein = 150);
    const rows = assess(s, evidence); assert.deepEqual(rows.map(r => r.amount), [2, 2, 1, 0, 0, 0, 0]); assert(rows.every(r => r.settle));
    evidence.days.slice(1).forEach(d => d.actual.protein = 200); const below = assess(s, evidence); assert.equal(below[0].amount, 2); assert.equal(below[0].settle, false);
});
test('changed terms invalidate matching approval; excused dates and reviewed counts are operational', () => {
    const { s } = sample(); const before = terms(s); s.excused['2026-10-12'] = true; s.observations['2026-10-12'] = { logging: 1 }; assert.equal(terms(s), before);
    s.rules[1].upper = 15; assert.notEqual(terms(s), before); const changed = terms(s); s.payment.amount = 50; assert.notEqual(terms(s), changed);
});
test('fixed and percentage late fees respect grace, full periods, no compounding and cap', () => {
    const p = defaults().payment; p.graceDays = 3; p.lateFrequency = 'weekly'; p.lateBasis = 'percent'; p.lateValue = 10; p.lateCap = 25;
    assert.equal(lateFee(p, 100, 3).fee, 0); assert.equal(lateFee(p, 100, 9).fee, 0); assert.equal(lateFee(p, 100, 10).fee, 10); assert.equal(lateFee(p, 100, 17).fee, 20); assert.equal(lateFee(p, 100, 31).fee, 25); assert.equal(lateFee(p, 0, 31).fee, 0);
    p.lateBasis = 'amount'; p.lateValue = 5; assert.equal(lateFee(p, 1000, 17).fee, 10); assert.equal(lateFee(p, -1, 17), null);
});
test('invalid criteria, late percentages and penny values are rejected; monthly evidence is not guessed', () => {
    const s = defaults(); assert(valid(s)); s.rules[0].min = 0.29; assert(valid(s)); s.rules[0].lower = 10; assert(!valid(s)); s.rules[0].lower = -5; s.payment.lateBasis = 'percent'; s.payment.lateValue = 101; assert(!valid(s));
    const sampleData = sample(); sampleData.s.rules[1].frequency = 'monthly'; assert.equal(assess(sampleData.s, sampleData.evidence)[0].status, 'pending');
});
