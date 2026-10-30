(() => {
    const cells = [...document.querySelectorAll('[data-snap-client]')];
    if (!cells.length) return;
    let busy = false;
    async function refresh() {
        if (busy || document.hidden) return;
        busy = true;
        try {
            const response = await fetch('/coach/snap-status', { credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'} });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
            const data = await response.json(); if (!Array.isArray(data)) return;
            for (const cell of cells) {
                const snap = data.find(x=>x.clientId.toLowerCase()===cell.dataset.snapClient.toLowerCase())?.snap;
                const signature = JSON.stringify(snap ?? null); if (signature===cell.dataset.signature) continue;
                cell.dataset.signature = signature;
                if (!snap?.assigned) { cell.textContent = 'No Snap Workouts assigned'; continue; }
                const title = document.createElement('strong'); title.textContent = `${snap.status} · ${snap.latestName}`;
                const counts = document.createElement('p'); counts.className='muted'; counts.textContent=`${snap.submitted} submitted · ${snap.completed} completed · ${snap.assigned} assigned`;
                const link=document.createElement('a'); link.className='button'; link.textContent='Review Snap results';
                link.href=`/accountability-preview/clients/${encodeURIComponent(cell.dataset.snapClient)}/snap?assignment=${encodeURIComponent(snap.latestWorkoutId)}#snap-${encodeURIComponent(snap.latestWorkoutId)}`;
                cell.replaceChildren(title,counts,link);
            }
        } catch { /* Preserve the last known result if the connection fails. */ }
        finally { busy=false; }
    }
    window.addEventListener('focus',refresh); document.addEventListener('visibilitychange',refresh);
    setInterval(refresh,30000); refresh();
})();
