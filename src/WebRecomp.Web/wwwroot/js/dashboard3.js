(() => {
    'use strict';
    const root = document.querySelector('[data-dashboard3]');
    const team = root?.querySelector('[data-team-dialog]');
    root?.querySelector('[data-open-team]')?.addEventListener('click', () => team.showModal());
    root?.querySelector('[data-close-team]')?.addEventListener('click', () => team.close());
    team?.addEventListener('click', event => { if (event.target === team) { const r = team.getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) team.close(); } });
    const dataNode = document.getElementById('d3-data');
    if (!root || !dataNode) return;
    const data = JSON.parse(dataNode.textContent);
    const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const point = (t, radius = 78) => [110 + radius * Math.cos(Math.PI + t * Math.PI), 103 + radius * Math.sin(Math.PI + t * Math.PI)];
    const arc = (a, b) => { const x = point(a), y = point(b); return `M${x[0]},${x[1]} A78,78 0 0,1 ${y[0]},${y[1]}`; };
    function draw(key, value, maximum, zones, label, suffix = '') {
        const host = root.querySelector(`[data-dial="${key}"]`);
        if (!host) return;
        const max = Math.max(1, maximum), fraction = Math.max(0, Math.min(1, (value || 0) / max));
        const end = point(fraction, 62);
        host.innerHTML = `<svg viewBox="0 0 220 148" role="img" aria-label="${esc(label)}"><title>${esc(label)}</title>${zones.map(([a, b, color]) => `<path d="${arc(a, b)}" fill="none" stroke="var(--d3-${color})" stroke-width="12"/>`).join('')}${value == null ? '' : `<path d="M110,103 L${end[0]},${end[1]}" class="d3-needle"/><circle cx="110" cy="103" r="6" class="d3-hub"/>`}<text x="110" y="140" text-anchor="middle" class="d3-dial-value">${value == null ? '—' : Math.round(value)}${esc(suffix)}</text><text x="28" y="127" text-anchor="middle" class="d3-dial-tick">0</text><text x="192" y="127" text-anchor="middle" class="d3-dial-tick">${Math.round(max)}</text></svg>`;
    }
    [['score', data.Score.DisplayScore, 'Recomp Plan Score'], ['nutrition', data.Score.NutritionScore, 'Nutrition'], ['workout', data.Score.WorkoutScore, 'Workout']].forEach(([key, value, label]) => draw(key, value, 100, [[0, .6, 'red'], [.6, .8, 'yellow'], [.8, 1, 'green']], `${label}: ${value} out of 100, completed-evidence adherence`));
    data.Bands.forEach(b => {
        const maximum = Math.max(1, b.High * 1.3, (b.Eaten || 0) * 1.07);
        draw(b.Key, b.Eaten, maximum, [[0, b.Low / maximum, 'yellow'], [b.Low / maximum, b.High / maximum, 'green'], [b.High / maximum, 1, 'red']], `${b.Name}: ${b.Eaten == null ? 'not logged' : b.Eaten + ' grams'}, target ${b.Low} to ${b.High} grams`, ' g');
    });
    const needle = root.querySelector('[data-energy-needle]');
    if (needle && data.EnergyEaten != null && data.EnergyTarget > 0) {
        const max = Math.max(data.EnergyTarget * 1.25, data.EnergyEaten * 1.07);
        root.style.setProperty('--d3-energy-low', `${data.EnergyTarget * .95 / max * 100}%`);
        root.style.setProperty('--d3-energy-good', `${data.EnergyTarget * .10 / max * 100}%`);
        needle.style.left = `${Math.min(99, data.EnergyEaten / max * 100)}%`;
        needle.hidden = false;
    }
    const dialog = root.querySelector('[data-coach-dialog]');
    root.querySelectorAll('[data-open-coach]').forEach(button => button.addEventListener('click', () => {
        const key = button.dataset.openCoach;
        const panels = [...dialog.querySelectorAll('[data-coach-panel]')];
        const selected = panels.find(panel => panel.dataset.coachPanel === key);
        if (!selected) return;
        panels.forEach(panel => { panel.hidden = panel !== selected; });
        dialog.querySelector('#d3-coach-title').textContent = `${dialog.dataset.coachName} explains your ${selected.dataset.coachTitle}`;
        dialog.querySelector('[data-coach-summary-output]').textContent = selected.dataset.coachSummary;
        dialog.querySelector('[data-coach-intro-output]').textContent = selected.dataset.coachIntro;
        dialog.showModal();
        dialog.scrollTop = 0;
    }));
    root.querySelector('[data-close-coach]')?.addEventListener('click', () => dialog.close());
    dialog?.addEventListener('click', event => { if (event.target === dialog) { const r = dialog.getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) dialog.close(); } });
    const flexDate = root.querySelector('#d3-flex-date'), flexCount = root.querySelector('#d3-flex-count');
    flexDate?.addEventListener('change', () => { const text = flexDate.selectedOptions[0].textContent.split('·').pop().trim(); flexCount.value = /^\d+$/.test(text) ? text : ''; });
})();
