(() => {
    for (const dialog of document.querySelectorAll('[data-coach-activity]')) {
        const endpoint = dialog.dataset.noticeEndpoint || '/coach/activity-notices';
        const list = dialog.querySelector('[data-coach-activity-list]');
        const feeMessage = dialog.querySelector('[data-fee-message]');
        const ledgerLink = dialog.querySelector('[data-fee-ledger-link]');
        const openedFees = new Set((document.querySelector('[data-fee-ledger-entries]')
            ?.dataset.feeLedgerEntries || '').split(' '));
        const status = dialog.querySelector('[role=status]');
        const postponed = new Set();
        let busy = false;
        let current = [];

        async function markRead(notices) {
            const body = new URLSearchParams();
            body.set('__RequestVerificationToken', dialog.querySelector('input[name=__RequestVerificationToken]').value);
            for (const notice of notices) body.append('ids', notice.id);
            const response = await fetch(endpoint + '/dismiss', { method: 'POST', credentials: 'same-origin', body });
            if (!response.ok) throw new Error();
        }

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

                if (feeMessage) {
                    const viewed = current.filter(notice => openedFees.has(notice.id));
                    if (viewed.length) {
                        await markRead(viewed);
                        for (const notice of viewed) postponed.add(notice.id);
                        current = current.filter(notice => !openedFees.has(notice.id));
                    }
                    if (!current.length || document.querySelector('dialog[open]')) return;
                    const first = current[0];
                    current = current.filter(notice => notice.url === first.url);
                    feeMessage.textContent = typeof first.message === 'string' ? first.message : 'Automatic fee posted';
                    ledgerLink.href = first.url;
                    dialog.querySelector('[data-fee-cash]').hidden = !first.forCoach;
                    dialog.querySelector('[data-fee-portrait]').hidden = !!first.forCoach;
                }

                list.replaceChildren();
                for (const notice of current) {
                    const item = document.createElement('li');
                    if (feeMessage) {
                        item.textContent = notice.title;
                    } else {
                        const link = document.createElement('a');
                        link.textContent = notice.title;
                        link.href = notice.url;
                        item.append(link);
                    }
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

        dialog.querySelector('[data-dismiss]')?.addEventListener('click', async () => {
            if (busy) return;
            busy = true;
            try {
                await markRead(current);
                dialog.close();
            } catch {
                status.textContent = 'Could not dismiss these updates. Please try again.';
            } finally {
                busy = false;
            }
        });
        ledgerLink?.addEventListener('click', () => dialog.close());
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
