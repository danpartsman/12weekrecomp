const { test } = require('node:test');
const assert = require('node:assert/strict');
const { defaults, valid, terms, assess, lateFee } = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
function sample() {
    const s = defaults(); s.enabled = true; s.agreement.start = '2026-01-01'; s.rules[1].assigned = true;
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
test('every cheat occurrence is paid from the first; blank count is pending', () => {
    const { s, evidence } = sample(); s.rules[1].assigned = false; const r = s.rules.find(r => r.id === 'cheat-foods'); r.assigned = true;
    s.observations[evidence.days[0].date] = { 'cheat-foods': 2 }; s.observations[evidence.days[1].date] = { 'cheat-foods': 3 };
    const rows = assess(s, evidence); assert.equal(rows[0].amount, 4); assert.equal(rows[1].amount, 6); assert.equal(rows[2].status, 'pending');
});
test('daily caps reset for each day; minimum never imposes a charge floor', () => {
    const { s, evidence } = sample(); s.rules[1].max = 5; s.rules[1].min = 5; evidence.days.forEach(d => d.actual.protein = 150);
    const rows = assess(s, evidence); assert.deepEqual(rows.map(r => r.amount), [2, 2, 2, 2, 2, 2, 2]); assert(rows.every(r => !r.settle));
    evidence.days.slice(1).forEach(d => d.actual.protein = 200); const below = assess(s, evidence); assert.equal(below[0].amount, 2); assert.equal(below[0].settle, false);
});
test('changed terms invalidate matching approval; excused dates and reviewed counts are operational', () => {
    const { s } = sample(); const before = terms(s); s.excused['2026-10-12'] = true; s.observations['2026-10-12'] = { logging: 1 }; assert.equal(terms(s), before);
    s.rules[1].upper = 15; assert.notEqual(terms(s), before); const changed = terms(s); s.payment.amount = 50; assert.notEqual(terms(s), changed);
});
test('fixed and percentage late fees respect grace, full periods, no compounding and cap', () => {
    const p = defaults().payment; p.graceDays = 3; p.lateFrequency = 'weekly'; p.lateBasis = 'percent'; p.lateValue = 10; p.lateCap = 25;
    assert.equal(lateFee(p, 100, 3).fee, 0); assert.equal(lateFee(p, 100, 9).fee, 0); assert.equal(lateFee(p, 100, 10).fee, 10); assert.equal(lateFee(p, 100, 17).fee, 20); assert.equal(lateFee(p, 100, 31).fee, 40); assert.equal(lateFee(p, 0, 31).fee, 0);
    p.lateBasis = 'amount'; p.lateValue = 5; assert.equal(lateFee(p, 1000, 17).fee, 10); assert.equal(lateFee(p, -1, 17), null);
});
test('invalid criteria, late percentages and penny values are rejected; monthly evidence is not guessed', () => {
    const s = defaults(); assert(valid(s)); s.rules[0].min = 0.29; assert(valid(s)); s.rules[0].lower = 10; assert(!valid(s)); s.rules[0].lower = -5; s.payment.lateBasis = 'percent'; s.payment.lateValue = 101; assert(!valid(s));
    const sampleData = sample(); sampleData.s.rules[1].frequency = 'monthly'; assert.equal(assess(sampleData.s, sampleData.evidence)[0].status, 'pending');
});

const { assessSnap } = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
function snapSample(basis = 'incomplete-reps') {
    const s = defaults(); s.agreement.start = '2026-01-01';
    s.snapWorkouts = [{ id: 'workout-1', date: '2026-10-14', name: 'Quick legs', cap: 100, exercises: [{ id: 'exercise-1', catalogId: 'catalog-1', name: 'Leg extension', sets: 3, reps: 10, weight: 50, type: 'fine', basis, rate: .25 }] }];
    s.snapResults = { 'workout-1': { finalized: true, sets: { 'exercise-1': [{ reps: 10, weight: 50 }, { reps: 8, weight: 50 }, { reps: 0, weight: 0 }] } } };
    const evidence = { today: '2026-10-17', weekStart: '2026-10-12', days: Array.from({ length: 7 }, (_, i) => ({ date: `2026-10-${12 + i}`, completed: true })) };
    return { s, evidence };
}
test('Snap completed and incomplete reps/sets use assigned targets and unit rates', () => {
    const expected = { 'completed-reps': [18, 4.5], 'completed-sets': [1, .25], 'incomplete-reps': [12, 3], 'incomplete-sets': [2, .5] };
    for (const [basis, [units, amount]] of Object.entries(expected)) {
        const { s, evidence } = snapSample(basis); assert(valid(s)); const row = assessSnap(s, evidence)[0]; assert.equal(row.units, units); assert.equal(row.amount, amount);
    }
});
test('Snap extra reps never increase units, and below-target weight does not qualify', () => {
    const { s, evidence } = snapSample('completed-reps');
    s.snapResults['workout-1'].sets['exercise-1'][0].reps = 100;
    s.snapResults['workout-1'].sets['exercise-1'][1] = { reps: 10, weight: 49.9 };
    const row = assessSnap(s, evidence)[0]; assert.equal(row.completedReps, 30); assert.equal(row.completedSets, 1); assert.equal(row.amount, 7.5);
});
test('Snap unknown or unfinished results stay pending, future work cannot accrue fees', () => {
    const { s, evidence } = snapSample(); s.snapResults['workout-1'].finalized = false;
    assert.equal(assessSnap(s, evidence)[0].status, 'pending');
    s.snapResults['workout-1'].finalized = true; s.snapResults['workout-1'].sets['exercise-1'][1].weight = null;
    assert(!valid(s)); assert.equal(assessSnap(s, evidence)[0].amount, 0);
    s.snapResults['workout-1'].sets['exercise-1'][1].weight = 50; evidence.today = '2026-10-13'; assert.equal(assessSnap(s, evidence)[0].status, 'pending');
});
test('dated exceptions waive Snap fees, and weekly selection never double-counts another week', () => {
    const { s, evidence } = snapSample(); s.excused['2026-10-14'] = true;
    assert.equal(assessSnap(s, evidence)[0].status, 'excused'); assert.equal(assessSnap(s, evidence)[0].amount, 0);
    delete s.excused['2026-10-14']; assert.equal(assessSnap(s, evidence)[0].amount, 3);
    evidence.days = evidence.days.map(d => ({ ...d, date: d.date.replace('10-', '11-') })); assert.equal(assessSnap(s, evidence).length, 0);
});
test('Snap cap applies across exercises separately for fines and incentives', () => {
    const { s, evidence } = snapSample(); const w = s.snapWorkouts[0]; w.cap = 4;
    w.exercises.push({ ...w.exercises[0], id: 'exercise-2' }, { ...w.exercises[0], id: 'exercise-3', type: 'incentive', basis: 'completed-reps' });
    s.snapResults['workout-1'].sets['exercise-2'] = structuredClone(s.snapResults['workout-1'].sets['exercise-1']);
    s.snapResults['workout-1'].sets['exercise-3'] = structuredClone(s.snapResults['workout-1'].sets['exercise-1']);
    assert(valid(s)); const rows = assessSnap(s, evidence); assert.deepEqual(rows.map(r => r.amount), [3, 1, 4]);
});
test('Snap assignment targets and rates require new approval; completion and exceptions do not', () => {
    const s = defaults(), before = terms(s); const old = { ...s }; delete old.snapWorkouts; delete old.snapResults;
    assert(valid(old)); assert.equal(terms(old), before);
    const sample = snapSample(); const contract = terms(sample.s); sample.s.snapResults['workout-1'].sets['exercise-1'][0].reps = 9; sample.s.excused['2026-10-14'] = true; assert.equal(terms(sample.s), contract);
    sample.s.snapWorkouts[0].exercises[0].rate = 1; assert.notEqual(terms(sample.s), contract);
});
test('Snap rejects zero sets, negative rates, invalid dates and duplicate assignment IDs', () => {
    const { s } = snapSample(); s.snapWorkouts[0].exercises[0].sets = 0; assert(!valid(s)); s.snapWorkouts[0].exercises[0].sets = 3;
    s.snapWorkouts[0].exercises[0].rate = -1; assert(!valid(s)); s.snapWorkouts[0].exercises[0].rate = .25;
    s.snapWorkouts[0].date = '2026-02-30'; assert(!valid(s)); s.snapWorkouts[0].date = '2026-10-14';
    s.snapWorkouts.push(structuredClone(s.snapWorkouts[0])); assert(!valid(s));
});

const { activeOn, agreementEnd, wallTime, wallToUtc } = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
test('agreement dates include the last day of chosen weeks and exclude dates outside the term', () => {
    const s=defaults();s.agreement={start:'2026-10-05',mode:'weeks',weeks:2,endedOn:null};
    assert.equal(agreementEnd(s),'2026-10-18');assert(activeOn(s,'2026-10-05'));assert(activeOn(s,'2026-10-18'));assert(!activeOn(s,'2026-10-19'));assert(!activeOn(s,'2026-10-04'));
    s.agreement.mode='open';assert.equal(agreementEnd(s),null);assert(activeOn(s,'2027-01-01'));s.agreement.endedOn='2026-10-20';assert(!activeOn(s,'2026-10-21'));
});
test('fees before agreement start and after termination never accrue', () => {
    const {s,evidence}=sample();s.agreement={start:'2026-10-14',mode:'open',weeks:12,endedOn:'2026-10-16'};evidence.days.forEach(d=>d.actual.protein=0);
    const rows=assess(s,evidence);assert.deepEqual(rows.map(r=>r.label),['2026-10-14','2026-10-15','2026-10-16']);assert.equal(rows.reduce((n,r)=>n+r.amount,0),6);
});
test('cheat caps reset per day and per week rather than across the entire program', () => {
    const {s,evidence}=sample();s.rules[1].assigned=false;const r=s.rules.find(r=>r.id==='cheat-foods');r.assigned=true;r.max=5;
    evidence.days.forEach(d=>s.observations[d.date]={'cheat-foods':4});assert.deepEqual(assess(s,evidence).map(r=>r.amount),[5,5,5,5,5,5,5]);
    r.frequency='weekly';assert.equal(assess(s,evidence)[0].amount,5);evidence.days=evidence.days.map(d=>{const date=new Date(d.date+'T12:00:00Z');date.setUTCDate(date.getUTCDate()+7);return {...d,date:date.toISOString().slice(0,10)};});evidence.days.forEach(d=>s.observations[d.date]={'cheat-foods':4});assert.equal(assess(s,evidence)[0].amount,5);
});
test('monthly caps apply once per month and enter only the week of period close', () => {
    const s=defaults();s.enabled=true;s.agreement.start='2026-01-01';const rule=s.rules.find(r=>r.id==='cheat-foods');rule.assigned=true;rule.frequency='monthly';rule.max=10;
    const all=Array.from({length:59},(_,i)=>{const d=new Date(Date.UTC(2026,0,1+i,12)).toISOString().slice(0,10);s.observations[d]={'cheat-foods':1};return{date:d,completed:true};});
    const e={today:'2026-03-02',days:all.filter(d=>d.date>='2026-01-26'&&d.date<='2026-02-01'),periodDays:all};const rows=assess(s,e);assert.deepEqual(rows.map(r=>r.amount),[10,10]);assert.deepEqual(rows.map(r=>r.countedInWeek),[true,false]);
    assert.equal(rows.filter(r=>r.countedInWeek).reduce((n,r)=>n+r.amount,0),10);
});
test('weekly macro caps and minimum thresholds apply to that single period', () => {
    const {s,evidence}=sample();const r=s.rules[1];r.frequency='weekly';r.amount=10;r.max=5;r.min=3;evidence.days.forEach(d=>d.actual.protein=0);
    assert.equal(assess(s,evidence)[0].amount,5);assert.equal(assess(s,evidence)[0].settle,true);
});
function rewardedSnap(){const sample=snapSample();const w=sample.s.snapWorkouts[0];w.createdAtUtc='2026-10-14T16:00:00Z';w.deadlineUtc='2026-10-14T18:00:00Z';w.exercises[0].completionReward=5;w.exercises[0].extraRepRate=.1;
    sample.s.snapResults[w.id].completedAtUtc='2026-10-14T17:00:00Z';sample.s.snapResults[w.id].sets['exercise-1']=Array.from({length:3},()=>({reps:12,weight:50}));sample.evidence.nowUtc='2026-10-17T16:00:00Z';return sample;}
test('Snap pays completion plus excess reps alongside a separate unfinished-work penalty', () => {
    const {s,evidence}=rewardedSnap();assert(valid(s));let rows=assessSnap(s,evidence);assert.equal(rows[0].amount,0);assert.equal(rows[1].amount,5.6);assert.equal(rows[1].units,6);
    s.snapResults['workout-1'].sets['exercise-1'][2]={reps:0,weight:0};rows=assessSnap(s,evidence);assert.equal(rows[0].amount,1.5);assert.equal(rows[1].amount,0);
});
test('extra completed sets count toward excess-rep reward at the assigned weight', () => {
    const {s,evidence}=rewardedSnap();s.snapResults['workout-1'].sets['exercise-1']=Array.from({length:4},()=>({reps:10,weight:50}));assert(valid(s));assert.equal(assessSnap(s,evidence)[1].amount,6);
    s.snapResults['workout-1'].sets['exercise-1'][3].weight=45;assert.equal(assessSnap(s,evidence)[1].amount,5);
});
test('late completion forfeits bonuses without inventing an additional late-workout fine', () => {
    const {s,evidence}=rewardedSnap();s.snapResults['workout-1'].completedAtUtc='2026-10-14T18:00:01Z';const rows=assessSnap(s,evidence);assert.equal(rows[0].amount,0);assert.equal(rows[1].amount,0);assert.equal(rows[0].onTime,false);
    s.snapResults['workout-1'].finalized=false;assert.equal(assessSnap(s,evidence)[0].status,'pending');assert.match(assessSnap(s,evidence)[0].reason,/Deadline passed/);
});
test('reward totals respect the workout incentive cap and excused dates', () => {
    const {s,evidence}=rewardedSnap();s.snapWorkouts[0].cap=4;assert.equal(assessSnap(s,evidence)[1].amount,4);s.excused['2026-10-14']=true;assert.equal(assessSnap(s,evidence)[0].amount,0);
});
test('deadline conversion uses client time zone, rejects missing DST times and resolves repeated times after assignment', () => {
    assert.equal(wallTime('2026-10-25T01:30:00Z','America/Los_Angeles'),'2026-10-24T18:30');
    assert.equal(wallToUtc('2026-10-24T20:30','America/Los_Angeles'),'2026-10-25T03:30:00.000Z');
    assert.equal(wallToUtc('2026-03-08T02:30','America/Los_Angeles'),null);
    assert.equal(wallToUtc('2026-11-01T01:30','America/Los_Angeles',Date.parse('2026-11-01T09:00:00Z')),'2026-11-01T09:30:00.000Z');
});
test('new agreement periods, deadlines and bonus rates are included in approval terms', () => {
    const {s}=rewardedSnap();const before=terms(s);s.agreement.mode='weeks';assert.notEqual(terms(s),before);const next=terms(s);s.snapWorkouts[0].deadlineUtc='2026-10-14T19:00:00Z';assert.notEqual(terms(s),next);
});
