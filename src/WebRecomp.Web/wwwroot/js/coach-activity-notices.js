(() => {
    for (const dialog of document.querySelectorAll('[data-coach-activity]')) {
        const endpoint = dialog.dataset.noticeEndpoint || '/coach/activity-notices';
        const list = dialog.querySelector('[data-coach-activity-list]');
        const feeMessage = dialog.querySelector('[data-fee-message]');
        const ledgerLink = dialog.querySelector('[data-fee-ledger-link]');
        const feeSignIn = dialog.dataset.feeSignIn;
        const feeSignInKey = '12wr.fee-notices.sign-in';
        const status = dialog.querySelector('[role=status]');
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
            if (busy || (!feeMessage && (document.hidden || document.querySelector('dialog[open]')))) return;
            busy = true;
            try {
                const response = await fetch(endpoint, {
                    credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
                });
                if (!response.ok) return;
                const data = await response.json();
                if (!Array.isArray(data.notices) || (!feeMessage && document.querySelector('dialog[open]'))) return;
                current = data.notices.filter(notice => typeof notice.id === 'string'
                    && typeof notice.title === 'string' && typeof notice.url === 'string'
                    && /^\/(coach-workspace|clients)\//.test(notice.url));
                if (!current.length) {
                    if (feeMessage) rememberFeeSignIn();
                    return;
                }

                if (feeMessage) {
                    const first = current[0];
                    feeMessage.textContent = typeof first.message === 'string' ? first.message : 'Automatic fee posted';
                    ledgerLink.href = first.url;
                    dialog.querySelector('[data-fee-cash]').hidden = first.noFees || !first.forCoach;
                    dialog.querySelector('[data-fee-portrait]').hidden = first.noFees || !!first.forCoach;
                }

                list.replaceChildren();
                const multipleLedgers = feeMessage && current.some(notice => notice.url !== current[0].url);
                for (const notice of current) {
                    const item = document.createElement('li');
                    if (feeMessage) {
                        if (multipleLedgers) {
                            const link = document.createElement('a');
                            link.textContent = notice.title;
                            link.href = notice.url;
                            link.addEventListener('click', event => {
                                event.preventDefault();
                                acknowledgeAndClose(link.href);
                            });
                            item.append(link);
                        } else {
                            item.textContent = notice.title;
                        }
                    } else {
                        const link = document.createElement('a');
                        link.textContent = notice.title;
                        link.href = notice.url;
                        if (notice.actionLabel) {
                            const title = document.createElement('p'); title.textContent = notice.title;
                            link.textContent = notice.actionLabel; link.className = 'button'; item.append(title);
                            link.addEventListener('click', async event => {
                                event.preventDefault();
                                try { await markRead([notice]); } catch { /* Navigation remains available. */ }
                                window.location.assign(link.href);
                            });
                        }
                        item.append(link);
                    }
                    list.append(item);
                }
                status.textContent = '';
                dialog.showModal();
                if (feeMessage) rememberFeeSignIn();
            } catch {
                // A failed lookup can be retried on the next check.
            } finally {
                busy = false;
            }
        }

        function rememberFeeSignIn() {
            try { localStorage.setItem(feeSignInKey, feeSignIn); } catch { /* Storage may be disabled. */ }
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
        let acknowledging = false;
        async function acknowledgeAndClose(navigateTo) {
            if (acknowledging) return;
            acknowledging = true;
            try {
                await markRead(current);
                dialog.close();
                if (navigateTo) window.location.assign(navigateTo);
            } catch {
                status.textContent = 'Acknowledgement could not be saved; please try again.';
            } finally {
                acknowledging = false;
            }
        }
        ledgerLink?.addEventListener('click', event => {
            event.preventDefault();
            acknowledgeAndClose(ledgerLink.href);
        });
        dialog.querySelector('[data-close]').addEventListener('click', () => acknowledgeAndClose());
        dialog.addEventListener('cancel', event => {
            event.preventDefault();
            acknowledgeAndClose();
        });
        if (feeMessage) {
            if (!feeSignIn) continue;
            try { if (localStorage.getItem(feeSignInKey) === feeSignIn) continue; } catch { /* Storage may be disabled. */ }
        } else {
            window.addEventListener('focus', refresh);
            document.addEventListener('visibilitychange', refresh);
            setInterval(refresh, 10000);
        }
        refresh();
    }
})();
