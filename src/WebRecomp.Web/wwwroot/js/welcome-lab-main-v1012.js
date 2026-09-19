(() => {
    'use strict';

    const stage = document.querySelector('[data-wlx-stage]');
    const cursor = document.querySelector('[data-wlx-cursor]');
    if (!stage) return;

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (!reduceMotion && cursor) {
        stage.addEventListener('pointermove', (event) => {
            const rect = stage.getBoundingClientRect();
            cursor.style.left = `${event.clientX - rect.left}px`;
            cursor.style.top = `${event.clientY - rect.top}px`;
        }, { passive: true });
    }

    document.querySelectorAll('[data-wlx-drag]').forEach((node) => {
        let startX = 0;
        let startY = 0;
        let originLeft = 0;
        let originTop = 0;
        let moved = false;
        let dragging = false;

        const onMove = (event) => {
            if (!dragging || window.innerWidth <= 850) return;
            const dx = event.clientX - startX;
            const dy = event.clientY - startY;
            if (Math.abs(dx) + Math.abs(dy) > 8) moved = true;

            const stageRect = stage.getBoundingClientRect();
            const nodeRect = node.getBoundingClientRect();
            const maxLeft = Math.max(8, stageRect.width - nodeRect.width - 8);
            const maxTop = Math.max(78, stageRect.height - nodeRect.height - 8);
            const nextLeft = Math.min(maxLeft, Math.max(8, originLeft + dx));
            const nextTop = Math.min(maxTop, Math.max(78, originTop + dy));

            node.style.left = `${nextLeft}px`;
            node.style.top = `${nextTop}px`;
            node.style.right = 'auto';
            node.style.bottom = 'auto';
        };

        const onUp = (event) => {
            if (!dragging) return;
            dragging = false;
            try { node.releasePointerCapture(event.pointerId); } catch { }
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            window.setTimeout(() => { moved = false; }, 0);
        };

        node.addEventListener('pointerdown', (event) => {
            if (window.innerWidth <= 850) return;
            const rect = node.getBoundingClientRect();
            const stageRect = stage.getBoundingClientRect();
            startX = event.clientX;
            startY = event.clientY;
            originLeft = rect.left - stageRect.left;
            originTop = rect.top - stageRect.top;
            moved = false;
            dragging = true;
            node.setPointerCapture(event.pointerId);
            window.addEventListener('pointermove', onMove);
            window.addEventListener('pointerup', onUp, { once: true });
        });

        node.addEventListener('click', (event) => {
            if (moved) event.preventDefault();
        });
    });
})();
