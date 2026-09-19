(() => {
  'use strict';
  const stage = document.querySelector('[data-bf-stage]');
  if (!stage) return;
  const cursor = document.querySelector('[data-bf-cursor]');
  const energy = document.querySelector('[data-bf-energy]');
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  if (!reduced && cursor && window.matchMedia('(pointer:fine)').matches) {
    stage.addEventListener('pointermove', (event) => {
      cursor.style.left = `${event.clientX}px`;
      cursor.style.top = `${event.clientY}px`;
    }, { passive: true });
  }

  energy?.addEventListener('click', () => {
    const active = stage.classList.toggle('energy-max');
    energy.setAttribute('aria-pressed', active ? 'true' : 'false');
  });
})();
