(() => {
    const config = document.getElementById('device-sync-on-open');
    if (!config) return;
    let edited = false;
    document.addEventListener('input', () => { edited = true; }, { once: true });
    document.addEventListener('change', () => { edited = true; }, { once: true });
    const body = new URLSearchParams();
    body.set(config.dataset.field, config.dataset.token);
    fetch(config.dataset.url, { method: 'POST', body, credentials: 'same-origin' })
        .then(response => response.ok ? response.json() : null)
        .then(result => {
            if (!result?.updated) return;
            if (!edited && /^\/(CommandCenter|Dashboard|DailyEntry|Progress|ExternalConnections)(\/|$)/i.test(window.location.pathname)) {
                window.location.reload();
                return;
            }
            const note = document.createElement('p');
            note.className = 'status-card';
            note.setAttribute('role', 'status');
            note.textContent = 'New scale readings synced. Refresh this page when you have finished your edits.';
            (document.querySelector('main') || document.body).prepend(note);
        }).catch(() => { /* The device page reports sync status; normal page use continues. */ });
})();
