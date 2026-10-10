(() => {
    const config = document.getElementById('device-sync-on-open');
    if (!config) return;
    const button = document.getElementById('pull-scale-readings');
    const status = document.getElementById('scale-pull-status');
    let edited = false;
    document.addEventListener('input', () => { edited = true; }, { once: true });
    document.addEventListener('change', () => { edited = true; }, { once: true });
    async function pull(manual) {
        if (button) button.disabled = true;
        if (manual && status) status.textContent = 'Pulling scale readings…';
        const body = new URLSearchParams();
        body.set(config.dataset.field, config.dataset.token);
        try {
            const response = await fetch(manual ? button.dataset.url : config.dataset.url,
                { method: 'POST', body, credentials: 'same-origin' });
            if (!response.ok) throw new Error('Scale pull unavailable');
            const result = await response.json();
            if (result.updated && !edited) { window.location.reload(); return; }
            if (status && (manual || result.updated)) {
                status.textContent = result.updated && edited
                    ? 'New scale readings synced. Finish saving your edits, then refresh to see them.'
                    : result.message || 'Your scale readings are up to date.';
            }
        } catch {
            if (manual && status) status.textContent = 'Scale readings could not be pulled. Check Device integrations or try again shortly.';
        } finally { if (button) button.disabled = false; }
    }
    button?.addEventListener('click', () => pull(true));
    pull(false);
})();
