(() => {
    'use strict';
    const dataNode = document.getElementById('d2-data');
    if (!dataNode) return;
    const data = JSON.parse(dataNode.textContent);
    const ns = 'http://www.w3.org/2000/svg';
    const add = (svg, tag, attrs, text) => {
        const node = document.createElementNS(ns, tag);
        Object.entries(attrs).forEach(([key, value]) => node.setAttribute(key, String(value)));
        if (text !== undefined) node.textContent = text;
        svg.appendChild(node); return node;
    };
    data.Bands.forEach(b => {
        const svg = document.querySelector(`[data-macro="${b.Key}"]`);
        if (!svg) return;
        const max = Math.max(b.High * 1.3, (b.Eaten || 0) * 1.15, 1);
        const y = value => 118 - Math.max(0, value) / max * 102;
        add(svg, 'rect', { x: 8, y: y(b.High), width: 390, height: Math.max(2, y(b.Low) - y(b.High)), fill: b.Color, opacity: .18 });
        [b.Low, b.High].forEach(value => add(svg, 'line', { x1: 8, x2: 398, y1: y(value), y2: y(value), stroke: b.Color, opacity: .55, 'stroke-dasharray': '4 4' }));
        add(svg, 'text', { x: 402, y: y(b.High) + 4 }, Math.round(b.High));
        add(svg, 'text', { x: 402, y: y(b.Low) + 4 }, Math.round(b.Low));
        add(svg, 'line', { x1: 8, x2: 398, y1: 118, y2: 118, stroke: '#38516a' });
        if (b.Eaten === null) { add(svg, 'text', { x: 16, y: 100 }, 'Waiting for logged intake'); return; }
        let sum = 0;
        const field = { protein: 'Protein', carbs: 'Carbs', fat: 'Fat' }[b.Key];
        const values = data.Meals.length ? data.Meals.map(m => (sum += m[field])) : [b.Eaten];
        const points = [[8, y(0)], ...values.map((v, i) => [8 + (i + 1) * 390 / values.length, y(v)])];
        add(svg, 'polyline', { points: points.map(p => p.join(',')).join(' '), fill: 'none', stroke: b.Color, 'stroke-width': 3, 'stroke-linejoin': 'round' });
        points.slice(1).forEach((point, i) => {
            const dot = add(svg, 'circle', { cx: point[0], cy: point[1], r: 4, fill: b.Color });
            add(dot, 'title', {}, `${data.Meals[i]?.Label || 'Logged total'}: ${Math.round(values[i])} g cumulative`);
        });
        add(svg, 'text', { x: 8, y: 140 }, 'Start');
        add(svg, 'text', { x: 398, y: 140, 'text-anchor': 'end' }, 'Logged so far');
    });
    const lessons = [
        ['Protein protects the training investment', 'Resistance training provides a reason to retain and build muscle; protein supplies material for that response. In Mettler’s two-week restriction experiment, athletes eating 2.3 g/kg lost less lean mass than those eating 1.0 g/kg. That supports protecting protein while reducing food, but does not establish your exact optimum or mean unlimited protein is useful. Reach your personal target, then consider the other demands on today’s food allocation.', '20 young trained athletes; short study; lean mass is not exclusively muscle.', '19927027'],
        ['Build muscle while reducing fat: possible, not promised', 'Longland’s experiment combined high-volume training and energy restriction for four weeks. The higher-protein group gained more lean mass and lost more fat than the lower-protein group. This supports combining a muscle-building stimulus, sufficient protein and controlled intake. It does not promise the same rate of change for an experienced older lifter or prove that our particular carb-cycling schedule is superior.', '40 young men; demanding six-day training schedule; measured lean mass is not pure muscle.', '26817506'],
        ['A bigger protein serving is not automatically wasted', 'Trommelen’s experiment compared 0, 25 and 100 g protein after exercise. The 100 g serving produced a larger and longer protein-synthesis response than 25 g. That challenges a rigid per-meal absorption ceiling, but does not prove unlimited benefit or long-term extra growth. Plan portions that fit your remaining protein and energy allocation instead of adding shakes by habit.', 'Acute tracer experiment; tested doses cannot establish an unlimited upper benefit.', '38118410'],
        ['Space meals for practicality, not an absorption clock', 'Areta’s study distributed 80 g whey across 12 hours after resistance exercise. Among the tested schedules, 20 g every three hours produced the strongest integrated muscle-protein synthesis response. This gives a reason to spread protein across the day when convenient. It does not establish a compulsory three-hour gap for 50 g servings, mixed meals, or every age group.', 'Small short experiment in young trained men; food type and serving size differ from everyday meals.', '23459753'],
        ['Refuel for the next demand', 'A glycogen-recovery experiment in eight triathletes compared frequent small feedings with less-frequent large feedings. With the same high carbohydrate total, the researchers found no statistically significant difference in 24-hour glycogen storage. Total intake and upcoming training therefore deserve attention alongside timing. Dashboard2 uses estimated demand to organize the existing plan; it cannot measure your glycogen or prove that deliberately incomplete refill increases fat loss.', 'Small endurance study; lack of a significant difference is not proof of equivalence in every setting.', '8669406'],
        ['Covered macros are not permission for unlimited food', 'In an eight-week controlled overfeeding experiment, fat increased across low-, normal- and high-protein diets. Protein affected lean mass and energy expenditure, but higher protein did not prevent fat gain. This is why the app checks combined intake even when the macro cards look good. Once useful allocations are covered, eating to reach every upper edge can work against your goal.', '25 young adults; substantial overfeeding without prescribed training; not a direct test of this recomp program.', '22215165']
    ];
    const next = document.getElementById('d2-next-lesson');
    if (next) {
        let index = 0;
        try { index = Number(localStorage.getItem('12wr-dashboard2-strategy') || 0); } catch (_) { }
        if (!Number.isInteger(index) || index < 0) index = 0;
        const render = () => {
            const lesson = lessons[index % lessons.length];
            document.getElementById('d2-lesson-count').textContent = `${index % lessons.length + 1} of ${lessons.length}`;
            document.getElementById('d2-lesson-title').textContent = lesson[0];
            document.getElementById('d2-lesson-body').textContent = lesson[1];
            document.getElementById('d2-lesson-limit').textContent = lesson[2];
            document.getElementById('d2-lesson-source').href = 'https://pubmed.ncbi.nlm.nih.gov/' + lesson[3] + '/';
        };
        render();
        next.addEventListener('click', () => { index = (index + 1) % lessons.length; render(); try { localStorage.setItem('12wr-dashboard2-strategy', String(index)); } catch (_) { } });
    }
    window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
})();
