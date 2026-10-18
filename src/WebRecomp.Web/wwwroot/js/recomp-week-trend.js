(() => {
    'use strict';
    const root = document.querySelector('[data-week-trend]');
    if (!root) return;
    const points = JSON.parse(root.querySelector('[data-week-trend-data]').textContent);
    if (!points.length) return;
    const esc = v => String(v).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const date = d => new Date(`${d}T12:00:00`).toLocaleDateString(undefined, {month:'short',day:'numeric'});
    const num = (v, digits = 0) => v == null ? '—' : Number(v).toFixed(digits);
    const signed = v => v == null ? '—' : `${v > 0 ? '+' : ''}${num(v, 1)} lb`;
    const a = root.querySelector('[data-trend-slider="a"]'), b = root.querySelector('[data-trend-slider="b"]');
    const favorable = points.map((p,i) => ({p,i})).filter(x => x.p.Status === 'Favorable');
    a.value = favorable.length ? favorable[favorable.length - 1].i : Math.max(0, points.length - 8);
    const range = Math.max(1, ...points.flatMap(p => [Math.abs(p.FatChangeLb || 0), Math.abs(p.MuscleChangeLb || 0), Math.abs(p.WeightChangeLb || 0)]));
    const max = Math.ceil(range * 2) / 2, x = i => 50 + i / Math.max(1, points.length - 1) * 700, y = v => 105 - v / max * 75;
    function series(key, color) {
        let d = '', previous = -2, label = null;
        points.forEach((p,i) => {
            if (p[key] == null) return;
            const gap = i - previous - 1;
            const canBridge = previous >= 0 && gap <= 3
                && (key !== 'MuscleChangeLb' || label === p.MuscleLabel);
            d += `${canBridge ? 'L' : 'M'}${x(i)},${y(p[key])} `;
            previous = i; label = p.MuscleLabel;
        });
        return `<path d="${d}" fill="none" stroke="${color}" stroke-width="3"/>` + points.map((p,i) => p[key] == null ? '' : `<circle cx="${x(i)}" cy="${y(p[key])}" r="2.5" fill="${color}"/>`).join('');
    }
    function chart() {
        const marks = [['A',Number(a.value)],['B',Number(b.value)]].map(([name,i]) => `<line x1="${x(i)}" y1="22" x2="${x(i)}" y2="184" stroke="${name === 'A' ? '#88d3ff' : '#ffffff'}" stroke-dasharray="5 4"/><text x="${x(i)}" y="15" text-anchor="middle" fill="#ffffff">${name}</text>`).join('');
        const statuses = points.map((p,i) => `<circle cx="${x(i)}" cy="190" r="3" fill="${p.Band === 'green' ? '#4bff26' : p.Band === 'red' ? '#ff2944' : p.Band === 'gray' ? '#aabed4' : '#ffe500'}"><title>${esc(`${date(p.Date)}: ${p.Status}`)}</title></circle>`).join('');
        root.querySelector('[data-week-trend-plot]').innerHTML = `<svg viewBox="0 0 800 220" role="img" aria-label="Rolling one-week fat and muscle changes in pounds; selected periods A and B"><title>Rolling weekly fat and muscle results</title><line x1="50" y1="105" x2="750" y2="105" stroke="#9aa8b8"/><line x1="50" y1="30" x2="750" y2="30" stroke="#30445d"/><line x1="50" y1="180" x2="750" y2="180" stroke="#30445d"/><text x="4" y="35" fill="#aabed4">+${max} lb</text><text x="24" y="110" fill="#aabed4">0</text><text x="4" y="185" fill="#aabed4">−${max} lb</text>${series('FatChangeLb','#ff6685')}${series('MuscleChangeLb','#4bff26')}${series('WeightChangeLb','#ffe500')}${marks}${statuses}<text x="50" y="213" fill="#aabed4">${esc(date(points[0].Date))}</text><text x="750" y="213" text-anchor="end" fill="#aabed4">${esc(date(points[points.length-1].Date))}</text></svg>`;
    }
    const note = p => p.Status === 'Favorable' ? 'Fat and muscle estimates moved in a favorable combination. Compare the logged routine with your other selected period.'
        : p.Status === 'Mixed' && p.MuscleChangeLb > .2 ? 'Muscle improved while fat increased. Preserve muscle support and review food quantities, daily activity, workout reporting and the prescription together.'
        : p.Status === 'Mixed' ? 'Fat decreased while muscle also decreased. Review training, protein support and the size of the deficit together.'
        : p.Status === 'Needs review' ? 'One or both estimates moved away from the intended recomp direction. Compare the inputs before changing the plan.'
        : p.Status === 'Stable' ? 'Both changes are within the display tolerance. Compare consistency and longer-term direction.'
        : 'Both fat and muscle comparisons are needed to classify this period. Available values are shown.';
    function render() {
        const pa = points[Number(a.value)], pb = points[Number(b.value)];
        a.setAttribute('aria-valuetext', date(pa.Date)); b.setAttribute('aria-valuetext', date(pb.Date));
        root.querySelector('[data-trend-date="a"]').textContent = date(pa.Date);
        root.querySelector('[data-trend-date="b"]').textContent = date(pb.Date);
        const cells = (label, fn) => `<tr><th>${label}</th><td>${fn(pa)}</td><td>${fn(pb)}</td></tr>`;
        root.querySelector('[data-week-trend-comparison]').innerHTML = `<div class="d3-trend-result-grid">${[['A',pa],['B',pb]].map(([name,p]) => `<article><h3>Period ${name} · ${esc(date(p.Date))}</h3><strong class="d3-${p.Band}">${esc(p.Status)}</strong><p>${esc(note(p))}</p></article>`).join('')}</div><div class="d2-table-wrap"><table class="d3-trend-table"><thead><tr><th>Weekly evidence</th><th>Period A</th><th>Period B</th></tr></thead><tbody>${cells('Completed days',p=>`${esc(date(p.WeekStart))}–${esc(date(p.WeekEnd))}`)}${cells('Weight change',p=>signed(p.WeightChangeLb))}${cells('Fat change',p=>signed(p.FatChangeLb))}${cells('Muscle change',p=>`${signed(p.MuscleChangeLb)} · ${esc(p.MuscleLabel)}`)}${cells('Fat readings · latest / earlier',p=>`${p.FatLatestReadings} / ${p.FatEarlierReadings}`)}${cells('Muscle readings · latest / earlier',p=>`${p.MuscleLatestReadings} / ${p.MuscleEarlierReadings}`)}${cells('Complete nutrition days',p=>`${p.NutritionDays} / 7`)}${cells('Logged daily calories',p=>`${num(p.Calories)} kcal`)}${cells('Logged daily protein / carbs / fat',p=>`${num(p.Protein)} / ${num(p.Carbs)} / ${num(p.Fat)} g`)}${cells('Same-day prescription captures',p=>`${p.PrescriptionDays} / 7`)}${cells('Captured daily calorie target',p=>`${num(p.TargetCalories)} kcal`)}${cells('Captured daily protein / carbs / fat target',p=>`${num(p.TargetProtein)} / ${num(p.TargetCarbs)} / ${num(p.TargetFat)} g`)}${cells('Completed / planned training days',p=>`${p.WorkoutDays} / ${p.PlannedWorkoutDays}`)}${cells('Completed workout minutes',p=>num(p.WorkoutMinutes))}${cells('Reported workout RPE · weighted',p=>num(p.WorkoutRpe,1))}${cells('Treat portions · known days',p=>`${p.TreatItems == null ? '—' : p.TreatItems} · ${p.TreatKnownDays} / 7 days`)}</tbody></table></div><p class="d3-sub">Logged and captured averages can cover different numbers of days; check coverage before comparing them. Training days show recorded sessions and saved plans, not measured expenditure.</p>`;
        chart();
    }
    a.addEventListener('input',render); b.addEventListener('input',render);
    root.querySelector('[data-trend-favorable]').disabled = !favorable.length;
    root.querySelector('[data-trend-favorable]').addEventListener('click',()=>{a.value=favorable[favorable.length-1].i;render();});
    root.querySelector('[data-trend-latest]').addEventListener('click',()=>{b.value=points.length-1;render();});
    render();
})();
