(() => {
    'use strict';
    document.querySelectorAll('[data-app-navigation]').forEach(nav => {
        const groups = [...nav.querySelectorAll('.app-nav-group')];
        const summary = group => group.querySelector('summary');
        const closeOthers = keep => groups.forEach(group => { if (group !== keep) group.open = false; });
        const items = group => [...group.querySelectorAll('.app-nav-panel a, .app-nav-panel button')];
        groups.forEach(group => {
            group.addEventListener('toggle', () => { if (group.open) closeOthers(group); });
            group.addEventListener('keydown', event => {
                const links = items(group), index = links.indexOf(document.activeElement);
                if (event.key === 'Escape' && group.open) {
                    event.preventDefault(); group.open = false; summary(group).focus();
                } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                    event.preventDefault(); closeOthers(group); group.open = true;
                    const next = index < 0 ? (event.key === 'ArrowDown' ? 0 : links.length - 1)
                        : (index + (event.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
                    links[next]?.focus();
                } else if (index >= 0 && (event.key === 'Home' || event.key === 'End')) {
                    event.preventDefault(); links[event.key === 'Home' ? 0 : links.length - 1]?.focus();
                }
            });
        });
        nav.addEventListener('click', event => { if (event.target.closest('a')) closeOthers(null); });
        document.addEventListener('click', event => { if (!nav.contains(event.target)) closeOthers(null); });
        document.addEventListener('focusin', event => closeOthers(event.target.closest('.app-nav-group')));
        const normalized = path => path.replace(/\/+$/, '').toLowerCase() || '/';
        nav.querySelectorAll('a[href]').forEach(link => {
            if (normalized(new URL(link.href, location.href).pathname) === normalized(location.pathname)) link.setAttribute('aria-current', 'page');
        });
    });
})();
