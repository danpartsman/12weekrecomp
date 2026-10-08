(() => {
    const log = (card, interactionCode) => {
        const form = card?.querySelector('[data-learning-log-form]');
        if (!form) return;
        const body = new FormData(form);
        body.set('interactionCode', interactionCode);
        fetch(form.action, { method: 'POST', body, credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' } }).catch(() => {});
    };
    const openDialog = card => {
        const dialog = card.querySelector('.learning-dialog');
        if (!dialog) return;
        dialog.hidden = false;
        document.documentElement.style.overflow = 'hidden';
        dialog.querySelector('.learning-dialog-close')?.focus();
    };
    const closeDialog = dialog => {
        if (!dialog) return;
        dialog.hidden = true;
        document.documentElement.style.overflow = '';
        dialog.closest('[data-learning-coach]')?.querySelector('[data-learning-open]')?.focus();
    };
    const read = async card => {
        if (!card || card.dataset.busy === 'true') return;
        card.dataset.busy = 'true';
        card.setAttribute('aria-busy', 'true');
        const status = card.querySelector('.learning-request-status');
        if (status) status.hidden = true;
        try {
            const form = card.querySelector('[data-learning-log-form]');
            const body = new FormData(form);
            body.set('displayMode', card.dataset.displayMode);
            const response = await fetch(card.dataset.readUrl, { method: 'POST', body,
                credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Could not load lesson');
            const template = document.createElement('template');
            template.innerHTML = await response.text();
            const next = template.content.querySelector('[data-learning-coach]');
            if (!next) throw new Error('Missing lesson');
            card.replaceWith(next);
            if (next.dataset.displayMode === 'full') next.focus({ preventScroll: true });
            else openDialog(next);
        } catch (_) {
            if (status) {
                status.textContent = 'Could not load another tip. Please try again.';
                status.hidden = false;
            }
        } finally {
            delete card.dataset.busy;
            card.removeAttribute('aria-busy');
        }
    };
    document.addEventListener('click', event => {
        const closer = event.target.closest('[data-learning-close]');
        if (closer) { closeDialog(closer.closest('.learning-dialog')); return; }
        const hide = event.target.closest('[data-learning-hide]');
        if (hide) {
            const card = hide.closest('[data-learning-coach]');
            log(card, 'DISMISS');
            card?.classList.add('is-hidden');
            try { sessionStorage.setItem('12wr-learning-hidden-' + card.dataset.contentCode, '1'); } catch (_) {}
            return;
        }
        const opener = event.target.closest('[data-learning-open]');
        if (opener) { read(opener.closest('[data-learning-coach]')); return; }
        const full = event.target.closest('.learning-coach--full');
        if (full && !event.target.closest('a,button,input,select,textarea')) read(full);
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            closeDialog([...document.querySelectorAll('.learning-dialog:not([hidden])')].pop());
        } else if ((event.key === 'Enter' || event.key === ' ') && event.target.matches('.learning-coach--full')) {
            event.preventDefault(); read(event.target);
        }
    });
    document.querySelectorAll('[data-learning-coach]').forEach(card => {
        try {
            if (sessionStorage.getItem('12wr-learning-hidden-' + card.dataset.contentCode) === '1')
                card.classList.add('is-hidden');
        } catch (_) {}
    });
})();
