(() => {
    const host = document.querySelector('[data-pending-agreements-host]');
    if (!host) return;
    let busy = false;
    async function refresh() {
        if (busy || document.hidden) return;
        busy = true; host.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch('/my-coach/pending-agreements', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) return;
            const data = await response.json();
            if (!Array.isArray(data.agreements) || !data.agreements.every(x => /^[a-f0-9-]{36}$/i.test(x.clientId) && /^[a-f0-9-]{36}$/i.test(x.coachId))) return;
            const signature = JSON.stringify(data); if (host.dataset.signature === signature) return;
            host.dataset.signature = signature;
            if (!data.agreements.length) { host.replaceChildren(); return; }
            const banner = document.createElement('aside'); banner.className = 'pending-snap-banner'; banner.setAttribute('aria-label', 'Coach agreements to review');
            const title = document.createElement('strong'); title.textContent = `${data.agreements.length} agreement${data.agreements.length === 1 ? '' : 's'} to review`;
            const text = document.createElement('p'); text.textContent = 'Your coach sent new agreement terms. Review them and accept, request changes, or decline.';
            const list = document.createElement('ul');
            for (const agreement of data.agreements) {
                const item = document.createElement('li'), link = document.createElement('a');
                link.className = 'button'; link.textContent = `Review agreement · ${agreement.coachName}`;
                link.href = `/coach-workspace/${encodeURIComponent(agreement.clientId)}/${encodeURIComponent(agreement.coachId)}#financial-agreement`;
                item.appendChild(link); list.appendChild(item);
            }
            banner.append(title, text, list); host.replaceChildren(banner);
        } catch { /* Keep a visible notice when the connection is interrupted. */ }
        finally { busy = false; host.setAttribute('aria-busy', 'false'); }
    }
    window.addEventListener('focus', refresh);
    window.addEventListener('coach-agreements-changed', refresh);
    document.addEventListener('visibilitychange', refresh);
    setInterval(refresh, 30000);
    refresh();
})();
