(() => {
    'use strict';
    for (const root of document.querySelectorAll('[data-snap-live]')) {
        const url = root.dataset.liveUrl, coach = root.dataset.coach === 'true';
        const form = root.querySelector('[data-snap-results]'), chat = root.querySelector('[data-snap-chat]');
        if (!url || !chat) continue;
        const status = root.querySelector('[data-live-status]') || root.querySelector('[data-chat-status]'), saved = root.querySelector('[data-live-saved]');
        const start = root.querySelector('[data-live-start]'), lifecycle = root.querySelector('[data-live-lifecycle]');
        const posts = root.querySelector('[data-chat-posts]'), earlier = root.querySelector('[data-chat-earlier]');
        const chatStatus = root.querySelector('[data-chat-status]'), messageBox = chat.querySelector('textarea');
        const token = () => chat.querySelector('[name=__RequestVerificationToken]').value;
        const fields = () => [...(form?.querySelectorAll('input[type=number]') || [])];
        let correcting = false;
        let started = false, finished = form?.dataset.finished === 'true', ready = false, dirty = root.dataset.retry === 'true';
        let timer, posting = null, polling = false, blocked = false, finishing = false, editSequence = 0, mutation = 0;
        let oldest = null, messageSending = false, lastMessageBody = '', lastPoll = 0;
        const messages = new Map();
        async function request(path = '', body = null) {
            const response = await fetch(url + path, { method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, ...(body ? { body } : {}) });
            let data; try { data = await response.json(); } catch { throw new Error('Could not connect. Sign in again if your session expired. Your entries remain on this page.'); }
            if (!response.ok) {
                if (response.status === 409) blocked = true;
                const error = new Error(data.message || 'Could not update this workout. Your entries remain on this page.');
                error.retryAfter = Number(response.headers.get('Retry-After')) || 0; throw error;
            }
            return data;
        }
        function bodyFor(actionForm) { return new URLSearchParams(new FormData(actionForm)); }
        function rememberMeta(data) {
            form.querySelector('[name=Revision]').value = data.revision;
            form.querySelector('[name=ProgressRevision]').value = data.progressRevision;
            form.querySelector('[name=AssignmentToken]').value = data.assignmentToken;
        }
        function fill(data) {
            for (const section of form.querySelectorAll('[data-live-exercise]')) {
                const sets = data.sets?.[section.dataset.liveExercise] || [];
                section.querySelectorAll('tbody tr').forEach((row, index) => {
                    const r = row.querySelector('input[name$=".Reps"]'), w = row.querySelector('input[name$=".Weight"]');
                    if (r) r.value = sets[index]?.reps ?? '';
                    if (w && sets[index]) w.value = sets[index].weightRecorded === false ? '' : sets[index].weight ?? '';
                });
                const rpe = section.querySelector('input[name$=".Rpe"]');
                if (rpe && sets.length) rpe.value = sets[0].rpe ?? '';
            }
            let recorded = 0, total = 0;
            for (const section of form.querySelectorAll('[data-live-exercise]')) {
                const sets = data.sets?.[section.dataset.liveExercise] || [];
                total += section.querySelectorAll('tbody tr').length;
                recorded += sets.filter(s => s.reps != null).length;
            }
            root.querySelector('[data-live-count]').textContent = `${recorded} / ${total} sets have reps recorded. Recorded entries are not a verification of completed targets.`;
        }
        function addMessages(data, older = false) {
            const nearBottom = posts.scrollHeight - posts.scrollTop - posts.clientHeight < 70;
            const oldHeight = posts.scrollHeight; let added = 0;
            for (const message of data.messages || []) {
                if (!messages.has(message.id)) added++;
                messages.set(message.id, message);
            }
            const sorted = [...messages.values()].sort((a, b) => new Date(a.createdUtc) - new Date(b.createdUtc) || a.sequence - b.sequence);
            for (const message of sorted) {
                let article = posts.querySelector(`[data-message-id="${CSS.escape(message.id)}"]`);
                if (!article) {
                    article = document.createElement('article'); article.dataset.messageId = message.id;
                    article.className = `snap-chat-post ${message.coach ? 'from-coach' : 'from-client'}`;
                    const heading = document.createElement('strong'); heading.textContent = `${message.authorName} · ${message.coach ? 'Coach' : 'Client'}`;
                    const when = document.createElement('time'); when.dateTime = message.createdUtc;
                    when.textContent = new Date(message.createdUtc).toLocaleString();
                    const body = document.createElement('p'); body.textContent = message.body;
                    article.append(heading, when, body);
                }
                posts.append(article);
            }
            if (!sorted.length) posts.textContent = 'No notes yet. Add your first note below.';
            else {
                for (const node of [...posts.childNodes]) if (node.nodeType === Node.TEXT_NODE) node.remove();
                oldest = Math.min(...sorted.map(message => message.sequence));
            }
            if (older || messages.size <= 100) earlier.hidden = !data.hasEarlierMessages;
            if (older) posts.scrollTop += posts.scrollHeight - oldHeight;
            else if (nearBottom) posts.scrollTop = posts.scrollHeight;
            else if (added) chatStatus.textContent = `${added} new note${added === 1 ? '' : 's'} in this workout.`;
        }
        function apply(data, allowFill = true) {
            addMessages(data); lastPoll = Date.now();
            if (ready && form.querySelector('[name=AssignmentToken]').value !== data.assignmentToken) {
                blocked = true; status.textContent = 'Your coach changed this assignment. Your entries remain here; reload to review the updated workout.'; return;
            }
            started = !!data.startedUtc; finished = !!data.finished;
            lifecycle.textContent = finished ? 'Finished' : started ? 'In progress' : 'Not started';
            if (start) start.hidden = started || finished;
            if (allowFill && !dirty && !posting && !correcting) { fill(data); rememberMeta(data); }
            saved.textContent = data.savedUtc ? `Last saved: ${new Date(data.savedUtc).toLocaleString()} · last checked ${new Date().toLocaleTimeString()}`
                : `No draft sets saved yet · last checked ${new Date().toLocaleTimeString()}`;
            if (!ready && !coach && !finished) fields().forEach(f => { f.disabled = !started; });
            if (finished && form.dataset.finished !== 'true') {
                blocked = true; fields().forEach(f => { f.disabled = true; });
                form.querySelectorAll('button[type=submit]').forEach(b => { b.disabled = true; });
                status.textContent = 'Workout finished. Reload to review the submitted results and fee summary.';
            }
            ready = true;
        }
        async function poll(force = false) {
            const details = root.closest('details');
            if (polling || document.hidden || (!force && details && !details.open)) return;
            polling = true; const version = mutation;
            try { const data = await request(); if (version !== mutation) return;
                if (!form) { addMessages(data); lastPoll = Date.now(); return; }
                apply(data); if (!dirty && !blocked) status.textContent = coach ? 'Live view connected. Saved sets update automatically.' : finished ? 'Submitted results. Any correction requires an explicit Finish workout submission.' : started ? 'Automatic saving is on. Finish workout submits results and calculates fees.' : 'Press Start workout to notify your coach and record sets.'; }
            catch (error) { status.textContent = `Updates unavailable: ${error.message} Last saved data is still shown.`; }
            finally { polling = false; }
        }
        async function save() {
            clearTimeout(timer);
            if (posting) { await posting; if (dirty) return save(); return true; }
            if (!dirty) return true;
            if (coach || finished || blocked || !started || !ready || !form.reportValidity()) return false;
            const sequence = editSequence, body = bodyFor(form); mutation++;
            status.textContent = 'Saving reps and weights…';
            posting = (async () => {
                try {
                    const data = await request('/progress', body);
                    rememberMeta(data);
                    if (sequence === editSequence) { dirty = false; fill(data); }
                    saved.textContent = `Last saved: ${new Date(data.savedUtc).toLocaleString()}`;
                    status.textContent = 'Progress saved. Your coach can see these entries. Workout is not finished.';
                    return true;
                } catch (error) {
                    status.textContent = `${error.message} These entries have not been confirmed saved.`;
                    return false;
                }
            })();
            const ok = await posting; posting = null; mutation++;
            if (ok && dirty && !finishing) timer = setTimeout(save, 1200);
            return ok;
        }
        if (!coach) fields().forEach(f => { f.disabled = true; });
        if (finished && !coach) form.querySelectorAll('button[type=submit]').forEach(b => { b.disabled = true; });
        root.querySelector('[data-live-correct]')?.addEventListener('click', event => {
            correcting = true; event.target.hidden = true; fields().forEach(f => { f.disabled = false; });
            const finish = form.querySelector('button[value=finish]'); if (finish) { finish.disabled = false; finish.textContent = 'Save corrected results'; }
            status.textContent = 'Editing submitted results. Changes are applied only when you submit corrected results; fees may be recalculated.';
        });
        start?.addEventListener('click', async () => {
            start.disabled = true; mutation++;
            try { const body = new URLSearchParams({ __RequestVerificationToken: token() }); apply(await request('/start', body));
                fields().forEach(f => { f.disabled = false; }); status.textContent = 'Workout started. Your coach has been notified. Starting does not reset your assigned deadline.';
            } catch (error) { status.textContent = error.message; }
            finally { start.disabled = false; mutation++; }
        });
        form?.addEventListener('input', event => {
            if (coach || !event.target.matches('input[type=number]')) return;
            if (finished) { if (correcting) dirty = true; return; }
            dirty = true; editSequence++; clearTimeout(timer); timer = setTimeout(save, 1200);
            status.textContent = 'Unsaved changes…';
        });
        form?.addEventListener('change', () => { if (dirty) { clearTimeout(timer); timer = setTimeout(save, 350); } });
        let submitting = false;
        form?.addEventListener('submit', async event => {
            if (submitting || coach) return;
            if (finished) { if (!correcting) event.preventDefault(); else { dirty = false; submitting = true; } return; }
            event.preventDefault();
            if (!ready || blocked || !started) { status.textContent = 'Start the workout and resolve any save conflict before submitting.'; return; }
            if (event.submitter?.value !== 'finish') { await save(); return; }
            finishing = true;
            try {
                if (!await save()) return;
                const current = await request();
                if (current.finished) { status.textContent = 'Already finished in another tab. Reload to review the results.'; return; }
                if (current.assignmentToken !== form.querySelector('[name=AssignmentToken]').value
                    || current.progressRevision !== Number(form.querySelector('[name=ProgressRevision]').value)) {
                    blocked = true; status.textContent = 'Saved progress changed in another tab. Reload before finishing; your entries remain here.'; return;
                }
                rememberMeta(current); submitting = true; dirty = false;
                form.requestSubmit(event.submitter);
            } catch (error) { status.textContent = error.message; }
            finally { finishing = false; }
        });
        chat.addEventListener('submit', async event => {
            event.preventDefault(); if (messageSending || !chat.reportValidity()) return;
            const text = messageBox.value.trim(); if (!text) return;
            if (text !== lastMessageBody) chat.querySelector('[name=RequestId]').value = crypto.randomUUID();
            lastMessageBody = text; const body = bodyFor(chat); messageSending = true;
            const button = chat.querySelector('button[type=submit]'); button.disabled = true;
            try {
                const data = await request('/message', body); addMessages(data);
                if (messageBox.value.trim() === text) messageBox.value = '';
                lastMessageBody = ''; chat.querySelector('[name=RequestId]').value = crypto.randomUUID(); chatStatus.textContent = 'Note sent.';
            } catch (error) { chatStatus.textContent = `${error.message} Your note is retained; retry to send it.`; }
            finally { messageSending = false; button.disabled = false; }
        });
        earlier.addEventListener('click', async () => {
            if (oldest == null) return; earlier.disabled = true;
            try { addMessages(await request(`?before=${oldest}`), true); }
            catch (error) { chatStatus.textContent = error.message; }
            finally { earlier.disabled = false; }
        });
        root.closest('details')?.addEventListener('toggle', () => { if (root.closest('details').open) poll(true); });
        window.addEventListener('focus', () => poll());
        document.addEventListener('visibilitychange', () => { if (!document.hidden) { if (dirty) save(); poll(); } });
        window.addEventListener('online', () => { if (dirty && !blocked) save(); poll(); });
        window.addEventListener('beforeunload', event => { if (dirty || messageSending) { event.preventDefault(); event.returnValue = ''; } });
        setInterval(() => { if (Date.now() - lastPoll > 3000) poll(); }, 3000);
        if (!root.closest('details') || root.closest('details').open) poll(true);
    }
})();
