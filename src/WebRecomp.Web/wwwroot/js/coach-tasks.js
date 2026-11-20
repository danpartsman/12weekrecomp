(() => {
    const editor = document.querySelector('[data-task-editor]');
    if (editor) {
        const mode = editor.querySelector('[data-task-billing]');
        const sync = () => {
            const quantity = mode.value === 'quantity';
            const timed = mode.value === 'minute' || mode.value === 'hour';
            for (const field of editor.querySelectorAll('[data-task-unit], [data-task-bounds]')) {
                const enabled = quantity || (timed && !field.hasAttribute('data-task-unit'));
                field.hidden = !enabled;
                field.querySelector('input').disabled = !enabled;
                field.querySelector('input').required = enabled;
            }
            editor.querySelector('[data-task-minimum]').textContent = timed ? 'Minimum active minutes' : 'Minimum quantity';
            editor.querySelector('[data-task-maximum]').textContent = timed ? 'Maximum active minutes' : 'Maximum quantity';
            editor.querySelector('[data-task-rate]').textContent = timed ? `Fee per ${mode.value} (USD)`
                : quantity ? 'Fee per unit (USD)' : 'Flat fee (USD)';
        };
        mode.addEventListener('change', sync);
        sync();
    }

    const running = [...document.querySelectorAll('[data-task-clock][data-running="true"]')];
    if (!running.length) return;
    const start = performance.now();
    const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
    const pad = value => String(value).padStart(2, '0');
    const render = () => {
        const secondsSinceRender = (performance.now() - start) / 1000;
        for (const clock of running) {
            const cap = clock.dataset.cap === '' ? Infinity : Number(clock.dataset.cap);
            const seconds = Math.min(cap, Number(clock.dataset.seconds) + secondsSinceRender);
            const display = Math.floor(seconds);
            clock.querySelector('[data-elapsed]').textContent =
                `${pad(Math.floor(display / 3600))}:${pad(Math.floor(display / 60) % 60)}:${pad(display % 60)}`;
            const accrued = clock.querySelector('[data-accrued]');
            if (accrued) accrued.textContent = money.format(seconds * Number(clock.dataset.rate) / Number(clock.dataset.secondsPerUnit));
        }
    };
    setInterval(render, 1000);
})();
