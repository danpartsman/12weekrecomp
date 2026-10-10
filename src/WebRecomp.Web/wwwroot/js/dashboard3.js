(() => {
    'use strict';
    const root = document.querySelector('[data-dashboard3]');
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
        host.innerHTML = `<svg viewBox="0 0 220 148" role="img" aria-label="${esc(label)}"><title>${esc(label)}</title>${zones.map(([a, b, color]) => `<path d="${arc(a, b)}" fill="none" stroke="var(--d3-${color})" stroke-width="12"/>`).join('')}${value == null ? '' : `<path d="M110,103 L${end[0]},${end[1]}" class="d3-needle"/><circle cx="110" cy="103" r="6" class="d3-hub"/>`}<text x="110" y="79" text-anchor="middle" class="d3-dial-value">${value == null ? '—' : Math.round(value)}${esc(suffix)}</text><text x="28" y="127" text-anchor="middle" class="d3-dial-tick">0</text><text x="192" y="127" text-anchor="middle" class="d3-dial-tick">${Math.round(max)}</text></svg>`;
    }
    draw('score', data.Score.Score, 100, [[0, .6, 'red'], [.6, .8, 'yellow'], [.8, 1, 'green']], `Weekly projected Recomp Score ${data.Score.Score} out of 100`);
    data.Bands.forEach(b => {
        const maximum = Math.max(1, b.High * 1.3, (b.Eaten || 0) * 1.07);
        draw(b.Key, b.Eaten, maximum, [[0, b.Low / maximum, 'yellow'], [b.Low / maximum, b.High / maximum, 'green'], [b.High / maximum, 1, 'red']], `${b.Name}: ${b.Eaten == null ? 'not logged' : b.Eaten + ' grams'}, target ${b.Low} to ${b.High} grams`, ' g');
    });
    data.Training.forEach(g => {
        const max = Math.max(1, g.Target, g.Completed);
        draw(g.Key, g.Target === 0 ? null : g.Completed, max, g.Target === 0 ? [[0, 1, 'neutral']]
            : [[0, Math.min(.94, g.Target / max), 'yellow'], [Math.min(.94, g.Target / max), 1, 'green']], `${g.Label}: ${g.Completed} completed out of ${g.Target} planned`);
    });
    const flex = data.Score.FlexibleItems;
    const flexMax = Math.max(6, flex || 0);
    draw('flex', flex, flexMax, data.Score.FlexibleDayOverMacros ? [[0, 1, 'red']]
        : [[0, 2.5 / flexMax, 'green'], [2.5 / flexMax, 4.5 / flexMax, 'yellow'], [4.5 / flexMax, 1, 'red']], `${flex == null ? 'Unknown' : flex} reported flexible items this week`);
    const needle = root.querySelector('[data-energy-needle]');
    if (needle && data.EnergyEaten != null && data.EnergyTarget > 0) {
        const max = Math.max(data.EnergyTarget * 1.25, data.EnergyEaten * 1.07);
        root.style.setProperty('--d3-energy-low', `${data.EnergyTarget * .95 / max * 100}%`);
        root.style.setProperty('--d3-energy-good', `${data.EnergyTarget * .10 / max * 100}%`);
        needle.style.left = `${Math.min(99, data.EnergyEaten / max * 100)}%`;
        needle.hidden = false;
    }
    const dialog = root.querySelector('[data-coach-dialog]');
    root.querySelectorAll('[data-open-coach]').forEach(button => button.addEventListener('click', () => dialog.showModal()));
    root.querySelector('[data-close-coach]')?.addEventListener('click', () => dialog.close());
    dialog?.addEventListener('click', event => { if (event.target === dialog) { const r = dialog.getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) dialog.close(); } });
    const flexDate = root.querySelector('#d3-flex-date'), flexCount = root.querySelector('#d3-flex-count');
    flexDate?.addEventListener('change', () => { const text = flexDate.selectedOptions[0].textContent.split('·').pop().trim(); flexCount.value = /^\d+$/.test(text) ? text : ''; });
})();
