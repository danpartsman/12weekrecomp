// Notification-only worker: does not intercept requests or cache account pages.
self.addEventListener('push', event => {
    let message = {};
    try { message = event.data ? event.data.json() : {}; } catch { }
    const url = typeof message.url === 'string' && message.url.startsWith('/') && !message.url.startsWith('//') ? message.url : '/notifications';
    event.waitUntil(self.registration.showNotification(message.title || '12 Week Recomp', {
        body: message.body || 'There is a coaching update in your account.', tag: message.tag || '12wr-update',
        icon: '/favicon.svg', data: { url }
    }));
});
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL(event.notification.data?.url || '/notifications', self.location.origin);
    if (url.origin !== self.location.origin) return;
    event.waitUntil(clients.openWindow(url.href));
});
