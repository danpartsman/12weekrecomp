(() => {
    for (const dialog of document.querySelectorAll('[data-coach-activity]')) {
        const endpoint = dialog.dataset.noticeEndpoint || '/coach/activity-notices';
        const list = dialog.querySelector('[data-coach-activity-list]');
        const status = dialog.querySelector('[role=status]');
        const postponed = new Set();
        let busy = false;
        let current = [];

        async function refresh() {
            if (busy || document.hidden || document.querySelector('dialog[open]')) return;
            busy = true;
            try {
                const response = await fetch(endpoint, {
                    credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
                });
                if (!response.ok) return;
                const data = await response.json();
                if (!Array.isArray(data.notices) || document.querySelector('dialog[open]')) return;
                current = data.notices.filter(notice => typeof notice.id === 'string'
                    && typeof notice.title === 'string' && typeof notice.url === 'string'
                    && /^\/(coach-workspace|clients)\//.test(notice.url) && !postponed.has(notice.id));
                if (!current.length) return;

                list.replaceChildren();
                for (const notice of current) {
                    const item = document.createElement('li');
                    const link = document.createElement('a');
                    link.textContent = notice.title;
                    link.href = notice.url;
                    item.append(link);
                    list.append(item);
                }
                status.textContent = '';
                dialog.showModal();
            } catch {
                // A failed lookup can be retried on the next check.
            } finally {
                busy = false;
            }
        }

        dialog.querySelector('[data-dismiss]').addEventListener('click', async () => {
            if (busy) return;
            busy = true;
            try {
                const body = new URLSearchParams();
                body.set('__RequestVerificationToken', dialog.querySelector('input[name=__RequestVerificationToken]').value);
                for (const notice of current) body.append('ids', notice.id);
                const response = await fetch(endpoint + '/dismiss', { method: 'POST', credentials: 'same-origin', body });
                if (!response.ok) throw new Error();
                dialog.close();
            } catch {
                status.textContent = 'Could not dismiss these updates. Please try again.';
            } finally {
                busy = false;
            }
        });
        dialog.querySelector('[data-close]').addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => {
            if (dialog.hasAttribute('data-notice-endpoint'))
                for (const notice of current) postponed.add(notice.id);
        });
        window.addEventListener('focus', refresh);
        document.addEventListener('visibilitychange', refresh);
        setInterval(refresh, 30000);
        refresh();
    }
})();
