(() => {
    const root = document.querySelector('[data-notification-settings]');
    if (!root) return;
    const status = root.querySelector('[data-phone-status]');
    const enable = root.querySelector('[data-enable-phone]');
    const disable = root.querySelector('[data-disable-phone]');
    const test = root.querySelector('[data-test-phone]');
    const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
    let registration, subscription, config;
    enable.disabled = true;
    const post = async (path, body, json = false) => {
        const response = await fetch('/notifications/' + path, { method: 'POST', credentials: 'same-origin',
            headers: { 'RequestVerificationToken': token, ...(json ? { 'Content-Type': 'application/json' } : {}) },
            body: json ? JSON.stringify(body) : new URLSearchParams(body) });
        if (!response.ok) throw new Error(await response.text());
        return response;
    };
    const show = text => { status.textContent = text; };
    const update = () => { enable.hidden = !!subscription; test.hidden = disable.hidden = !subscription; };
    const register = async () => registration ||= await navigator.serviceWorker.register('/notifications-sw.js', { scope: '/' });
    const key = value => Uint8Array.from(atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4)), c => c.charCodeAt(0));
    if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
        enable.disabled = true; show('Phone push is unavailable here. On iPhone, open 12WR from its Home Screen icon; otherwise use a supported browser.'); return;
    }
    enable.addEventListener('click', async () => {
        // Permission must be requested directly from the user's tap, never on page load.
        const permissionRequest = Notification.requestPermission();
        enable.disabled = true;
        try {
            if (await permissionRequest !== 'granted') throw new Error('Notifications were not allowed. You can enable them in this browser’s site settings.');
            await register();
            await navigator.serviceWorker.ready;
            subscription = await registration.pushManager.getSubscription() || await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key(config.publicKey) });
            await post('subscribe', subscription.toJSON(), true);
            show('Enabled on this device. Use “Send a test notification” to check your phone.'); update();
        } catch (e) { show(e.message); } finally { enable.disabled = false; }
    });
    disable.addEventListener('click', async () => {
        try {
            if (subscription) { await post('unsubscribe', { endpoint: subscription.endpoint }); await subscription.unsubscribe(); }
            subscription = null; update(); show('Notifications are disabled on this device. Email preferences are separate.');
        } catch (e) { show(e.message); }
    });
    test.addEventListener('click', async () => {
        test.disabled = true;
        try { await post('test', { endpoint: subscription.endpoint }); show('Test notification accepted for delivery. Check this device’s notifications.'); }
        catch (e) { show(e.message); } finally { test.disabled = false; }
    });
    (async () => {
        try {
            config = await (await fetch('/notifications/configuration', { credentials: 'same-origin' })).json();
            await register(); subscription = await registration.pushManager.getSubscription();
            if (subscription && !config.endpoints.includes(subscription.endpoint)) subscription = null;
            update(); enable.disabled = false;
            show(subscription ? 'This browser has a push subscription. Enable on each phone or device you want to use.' : 'Not enabled on this device yet.');
        } catch { enable.disabled = true; show('Notification setup is temporarily unavailable. Reload to try again.'); }
    })();
})();
