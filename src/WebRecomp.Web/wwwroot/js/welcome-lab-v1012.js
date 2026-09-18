(() => {
  'use strict';

  const clamp = (n, min, max) => Math.max(min, Math.min(max, n));

  // Parallax for full-screen image concepts.
  document.querySelectorAll('[data-wl-parallax]').forEach(stage => {
    const bg = stage.querySelector('.wl-bg');
    if (!bg) return;
    stage.addEventListener('pointermove', e => {
      const r = stage.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width - .5;
      const y = (e.clientY - r.top) / r.height - .5;
      bg.style.transform = `scale(1.045) translate(${x * -10}px, ${y * -8}px)`;
    });
    stage.addEventListener('pointerleave', () => {
      bg.style.transform = 'scale(1.02)';
    });
  });

  // Hover/click hotspot tooltip.
  const tip = document.querySelector('.wl-tooltip');
  const showTip = (el) => {
    if (!tip) return;
    const title = el.dataset.title || 'Explore';
    const copy = el.dataset.copy || '';
    tip.innerHTML = `<strong>${title}</strong><p>${copy}</p>`;
    const r = el.getBoundingClientRect();
    const left = clamp(r.left + r.width + 14, 12, window.innerWidth - 280);
    const top = clamp(r.top + r.height / 2 - 35, 12, window.innerHeight - 150);
    tip.style.left = `${left}px`;
    tip.style.top = `${top}px`;
    tip.classList.add('is-on');
  };
  const hideTip = () => tip?.classList.remove('is-on');
  document.querySelectorAll('.wl-hotspot').forEach(el => {
    el.addEventListener('mouseenter', () => showTip(el));
    el.addEventListener('mouseleave', hideTip);
    el.addEventListener('focus', () => showTip(el));
    el.addEventListener('blur', hideTip);
    el.addEventListener('click', () => showTip(el));
  });

  // Drag-anything capability on elements tagged .wl-drag.
  document.querySelectorAll('.wl-drag').forEach(el => {
    let dragging = false;
    let sx = 0, sy = 0, ox = 0, oy = 0;
    el.addEventListener('pointerdown', e => {
      dragging = true;
      el.setPointerCapture(e.pointerId);
      sx = e.clientX;
      sy = e.clientY;
      ox = Number(el.dataset.tx || 0);
      oy = Number(el.dataset.ty || 0);
    });
    el.addEventListener('pointermove', e => {
      if (!dragging) return;
      const tx = ox + (e.clientX - sx);
      const ty = oy + (e.clientY - sy);
      el.dataset.tx = tx;
      el.dataset.ty = ty;
      el.style.translate = `${tx}px ${ty}px`;
    });
    const done = e => {
      dragging = false;
      try { el.releasePointerCapture(e.pointerId); } catch {}
    };
    el.addEventListener('pointerup', done);
    el.addEventListener('pointercancel', done);
  });

  // Clickable right rail swaps a punch phrase on the borderless concept.
  const phrase = document.querySelector('[data-wl-phrase]');
  document.querySelectorAll('[data-phrase]').forEach(btn => {
    btn.addEventListener('click', () => {
      if (!phrase) return;
      phrase.animate([{opacity:.2, transform:'translateY(8px)'},{opacity:1, transform:'translateY(0)'}], {duration:260, easing:'ease-out'});
      phrase.textContent = btn.dataset.phrase || phrase.textContent;
    });
  });

  // Card concept can be reordered by dragging cards horizontally/vertically.
  const grid = document.querySelector('[data-reorder-grid]');
  if (grid) {
    let dragged = null;
    grid.querySelectorAll('.wl-card').forEach(card => {
      card.draggable = true;
      card.addEventListener('dragstart', () => { dragged = card; card.style.opacity = '.45'; });
      card.addEventListener('dragend', () => { card.style.opacity = ''; dragged = null; });
      card.addEventListener('dragover', e => e.preventDefault());
      card.addEventListener('drop', e => {
        e.preventDefault();
        if (!dragged || dragged === card) return;
        const cards = [...grid.children];
        const from = cards.indexOf(dragged);
        const to = cards.indexOf(card);
        if (from < to) card.after(dragged); else card.before(dragged);
      });
    });
  }

  // Subtle scroll progress glow.
  const bar = document.querySelector('[data-scroll-progress]');
  if (bar) {
    const update = () => {
      const max = document.documentElement.scrollHeight - innerHeight;
      const p = max > 0 ? scrollY / max : 0;
      bar.style.transform = `scaleX(${p})`;
    };
    addEventListener('scroll', update, {passive:true});
    update();
  }
})();
