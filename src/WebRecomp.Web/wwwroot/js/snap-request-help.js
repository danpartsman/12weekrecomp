(() => {
    const actions = document.querySelector('[data-snap-retry-seconds]');
    if (!actions) return;
    const until = Date.now() + Number(actions.dataset.snapRetrySeconds) * 1000;
    const button = actions.querySelector('[data-snap-retry]');
    const display = document.querySelector('[data-snap-wait]');
    const update = () => {
        const seconds = Math.max(0, Math.ceil((until - Date.now()) / 1000));
        if (display) display.textContent = String(seconds);
        if (button) button.disabled = seconds > 0;
        if (!seconds) clearInterval(timer);
    };
    const timer = setInterval(update, 1000);
    update();
    actions.querySelector('[data-snap-back]')?.addEventListener('click', () => history.back());
    actions.querySelector('form')?.addEventListener('submit', () => { if (button) button.disabled = true; });
})();
