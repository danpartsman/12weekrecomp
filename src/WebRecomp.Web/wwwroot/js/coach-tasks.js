(() => {
    const editor = document.querySelector('[data-task-editor]');
    if (editor) {
        const mode = editor.querySelector('[data-task-billing]');
        const due = editor.querySelector('[data-task-due]');
        const unit = editor.querySelector('[data-task-unit]');
        const bounds = editor.querySelector('[data-task-bounds]');
        const suggestDue = () => {
            try {
                const formatter = new Intl.DateTimeFormat('en-GB', {
                    timeZone: editor.dataset.timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
                    hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
                });
                const parts = Object.fromEntries(formatter.formatToParts(Date.now() + 2 * 60 * 60 * 1000)
                    .map(({ type, value }) => [type, value]));
                return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
            } catch {
                return editor.dataset.defaultDue;
            }
        };
        const sync = () => {
            const quantity = mode.value === 'quantity';
            const timed = mode.value === 'minute' || mode.value === 'hour';
            unit.hidden = !quantity;
            unit.querySelector('input').disabled = !quantity;
            unit.querySelector('input').required = quantity;
            bounds.hidden = bounds.disabled = !quantity && !timed;
            for (const input of bounds.querySelectorAll('input')) input.required = quantity;
            editor.querySelector('[data-task-bounds-label]').textContent = timed ? 'Active minutes (optional)' : 'Quantity limits';
            editor.querySelector('[data-task-rate]').textContent = timed ? `Fee per ${mode.value} (USD)`
                : quantity ? 'Fee per unit (USD)' : 'Completion fee (Optional)';
            editor.querySelector('[data-task-rate-input]').required = timed || quantity;
        };
        if (!due.value) due.value = suggestDue();
        mode.addEventListener('change', sync);
        sync();
    }

    const proofDialog = document.querySelector('[data-task-proof-dialog]');
    if (typeof proofDialog?.showModal === 'function') {
        const enlarged = proofDialog.querySelector('img');
        for (const link of document.querySelectorAll('[data-task-proof]')) {
            link.addEventListener('click', event => {
                if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                event.preventDefault();
                enlarged.src = link.href;
                enlarged.alt = link.querySelector('img').alt;
                proofDialog.showModal();
            });
        }
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
