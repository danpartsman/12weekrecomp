(() => {
    const host = document.querySelector('[data-pending-snap-host]');
    if (!host) return;
    let busy = false;
    async function refresh() {
        if (busy || document.hidden) return;
        busy = true;
        try {
            const response = await fetch('/my-coach/pending-snaps', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
            const data = await response.json();
            if (!Array.isArray(data.workouts) || (data.workouts.length && !/^[a-f0-9-]{36}$/i.test(data.clientId))) return;
            const signature = JSON.stringify(data);
            if (host.dataset.signature === signature) return;
            host.dataset.signature = signature;
            if (!data.workouts.length) { host.replaceChildren(); return; }
            const banner = document.createElement('aside'); banner.className = 'pending-snap-banner'; banner.setAttribute('aria-label', 'Assigned Snap Workouts');
            const title = document.createElement('strong'); title.textContent = `${data.workouts.length} Snap Workout${data.workouts.length === 1 ? '' : 's'} to complete`;
            const text = document.createElement('p'); text.textContent = 'Your coach has assigned a workout. Record your results and press Finish workout to submit them to your coach.';
            const list = document.createElement('ul');
            for (const w of data.workouts) {
                const item = document.createElement('li'); item.dataset.pendingWorkout = w.id;
                const link = document.createElement('a'); link.className = 'button'; link.textContent = `Open ${w.name} · ${w.date}`;
                link.href = `/accountability-preview/clients/${encodeURIComponent(data.clientId)}/snap?assignment=${encodeURIComponent(w.id)}#snap-${encodeURIComponent(w.id)}`;
                item.append(link); list.append(item);
            }
            banner.append(title, text, list); host.replaceChildren(banner);
        } catch { /* Keep the existing notice during connection failures. */ }
        finally { busy = false; }
    }
    window.addEventListener('focus', refresh);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('snap-workouts-changed', refresh);
    setInterval(refresh, 30000);
    refresh();
})();
