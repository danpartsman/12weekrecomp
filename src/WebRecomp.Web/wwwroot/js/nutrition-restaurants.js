(() => {
    const dialog = document.querySelector('#nutrition-restaurant-picker');
    const picker = document.querySelector('#nutrition-food-picker-dialog');
    if (!dialog || !picker) return;
    const name = dialog.querySelector('[data-restaurant-name]');
    const brands = dialog.querySelector('[data-restaurant-brands]');
    const menu = dialog.querySelector('[data-restaurant-menu]');
    const query = dialog.querySelector('[data-restaurant-item-search]');
    const items = dialog.querySelector('[data-restaurant-items]');
    const status = dialog.querySelector('[data-restaurant-status]');
    const more = dialog.querySelector('[data-restaurant-more]');
    const retry = dialog.querySelector('[data-restaurant-retry]');
    const back = dialog.querySelector('[data-restaurant-back]');
    let restaurant = '', nextPage = null, timer = null, request = null, generation = 0, lastAction = null;
    let seen = new Set();
    const cancel = () => { clearTimeout(timer); request?.abort(); request = null; generation++; };
    const message = value => { status.textContent = value; };
    const macro = value => value == null ? '—' : Number(value).toLocaleString(undefined, { maximumFractionDigits: 1 });
    const makeChoice = (title, detail, action) => {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'nutrition-restaurant-choice';
        const heading = document.createElement('strong'); heading.textContent = title; button.appendChild(heading);
        if (detail) { const text = document.createElement('span'); text.textContent = detail; button.appendChild(text); }
        button.addEventListener('click', action); return button;
    };
    async function fetchResults(url, action) {
        cancel(); const current = generation;
        request = new AbortController(); lastAction = action; retry.hidden = true; more.disabled = true;
        try {
            const response = await fetch(url, { signal: request.signal, credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Please sign in again to browse restaurants.');
            const payload = await response.json();
            if (current !== generation || !dialog.open) return null;
            if (!response.ok) throw new Error(payload.message || 'Restaurant lookup failed. Try again.');
            return payload;
        } catch (error) {
            if (current === generation && dialog.open && error.name !== 'AbortError') {
                message(error.message || 'Restaurant lookup failed. Try again.'); retry.hidden = false;
            }
            return null;
        } finally { if (current === generation) more.disabled = false; }
    }
    async function searchNames() {
        brands.replaceChildren();
        const text = name.value.trim();
        if (!text) { cancel(); message('Type a restaurant name to begin.'); return; }
        message('Finding restaurants…');
        const url = new URL(dialog.dataset.restaurantsUrl, location.origin); url.searchParams.set('q', text);
        const payload = await fetchResults(url, searchNames); if (!payload) return;
        for (const value of payload.restaurants || []) {
            brands.appendChild(makeChoice(value, '', () => chooseRestaurant(value)));
        }
        message(payload.restaurants?.length ? 'Choose a restaurant.' : 'No matching restaurant found. Try its full name or use regular food search.');
    }
    function chooseRestaurant(value) {
        cancel(); restaurant = value; query.value = ''; seen = new Set(); nextPage = null;
        dialog.querySelector('#restaurant-title').textContent = value;
        dialog.querySelector('[data-restaurant-name-field]').hidden = true;
        brands.hidden = true; menu.hidden = false; back.hidden = false;
        void loadMenu(true);
    }
    async function loadMenu(reset = false) {
        if (reset) { seen = new Set(); items.replaceChildren(); nextPage = 1; more.hidden = true; }
        const page = nextPage; if (page == null) return;
        message('Loading available menu items…');
        const url = new URL(dialog.dataset.menuUrl, location.origin);
        url.searchParams.set('restaurant', restaurant); url.searchParams.set('page', String(page)); url.searchParams.set('q', query.value.trim());
        const payload = await fetchResults(url, () => loadMenu(reset)); if (!payload) return;
        for (const food of payload.items || []) {
            const key = `${food.providerCode}|${food.sourceFoodId}`;
            if (seen.has(key)) continue; seen.add(key);
            const detail = `${food.serving} · ${macro(food.energyKcal)} kcal | P ${macro(food.proteinG)} g | C ${macro(food.carbohydrateG)} g | F ${macro(food.fatG)} g`;
            items.appendChild(makeChoice(food.description, detail, () => {
                dialog.close();
                picker.dispatchEvent(new CustomEvent('nutrition-restaurant-selected', { detail: { ...food, description: `${restaurant} — ${food.description}` } }));
            }));
        }
        nextPage = payload.nextPage ?? null; more.hidden = nextPage == null;
        message(seen.size ? 'Select an item to choose its portion.' : nextPage != null
            ? 'No matching items on this page. Load more or search for a menu item.'
            : 'No menu items found. Try another item search or use Add custom food.');
    }
    function reset() {
        cancel(); restaurant = ''; nextPage = null; seen = new Set();
        name.value = ''; query.value = ''; brands.replaceChildren(); items.replaceChildren();
        dialog.querySelector('#restaurant-title').textContent = 'Find restaurant';
        dialog.querySelector('[data-restaurant-name-field]').hidden = false;
        brands.hidden = false; menu.hidden = true; back.hidden = true; more.hidden = true; retry.hidden = true;
        message('Type a restaurant name to begin.');
    }
    document.querySelectorAll('[data-picker-restaurant-open]').forEach(button => button.addEventListener('click', () => {
        reset(); dialog.showModal(); name.focus();
    }));
    dialog.querySelector('[data-restaurant-close]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => { if (!dialog.open) cancel(); });
    back.addEventListener('click', () => { reset(); name.focus(); });
    name.addEventListener('input', () => { cancel(); timer = setTimeout(searchNames, 350); });
    query.addEventListener('input', () => { cancel(); timer = setTimeout(() => loadMenu(true), 350); });
    for (const field of [name, query]) field.addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); clearTimeout(timer); field === name ? void searchNames() : void loadMenu(true); }
    });
    more.addEventListener('click', () => { if (!more.disabled) void loadMenu(); });
    retry.addEventListener('click', () => { if (lastAction) void lastAction(); });
})();
