(() => {
  "use strict";

  const qs = (s, r = document) => r.querySelector(s);
  const qsa = (s, r = document) => [...r.querySelectorAll(s)];
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;

  const linkStyle = document.createElement("style");
  linkStyle.textContent = `
    .ws-hotspot,.ws-card{color:#fff;text-decoration:none}
    .ws-breakfree-zone a{padding:12px 14px;border-radius:999px;border:1px solid rgba(43,229,255,.35);background:rgba(0,9,14,.66);color:#fff;text-decoration:none;backdrop-filter:blur(12px);font-size:10px;font-weight:1000;letter-spacing:.12em;text-transform:uppercase}
    .ws-breakfree-zone a:hover{border-color:#7dff24;color:#a5ff62}
  `;
  document.head.appendChild(linkStyle);

  const scene = qs(".ws-scene");
  if (!scene) return;

  const bg = qs(".ws-bg", scene);
  const cursor = qs(".ws-cursor", scene);

  if (!reduced) {
    scene.addEventListener("pointermove", e => {
      const r = scene.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width;
      const y = (e.clientY - r.top) / r.height;
      if (bg) bg.style.transform = `scale(1.025) translate(${(x - .5) * -12}px, ${(y - .5) * -8}px)`;
      if (cursor) {
        cursor.style.left = `${e.clientX - r.left}px`;
        cursor.style.top = `${e.clientY - r.top}px`;
      }
    });
    scene.addEventListener("pointerleave", () => {
      if (bg) bg.style.transform = "scale(1.015)";
    });
  }

  const panel = qs(".ws-panel");
  const panelLabel = qs(".ws-panel .label");
  const panelTitle = qs(".ws-panel h2");
  const panelCopy = qs(".ws-panel p");
  const showPanel = (el) => {
    if (!panel) return;
    panelLabel.textContent = el.dataset.label || "EXPLORE";
    panelTitle.textContent = el.dataset.title || "12 WEEK RECOMP";
    panelCopy.textContent = el.dataset.copy || "Build strength, fuel the work, recover, and keep moving forward.";
    panel.classList.add("on");
  };
  qsa("[data-ws-panel]").forEach(el => {
    el.addEventListener("click", e => {
      e.stopPropagation();
      showPanel(el);
    });
    el.addEventListener("keydown", e => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        showPanel(el);
      }
    });
  });
  qs(".ws-panel .close")?.addEventListener("click", () => panel?.classList.remove("on"));

  const makeDraggable = el => {
    let dragging = false, moved = false, ox = 0, oy = 0;
    const down = e => {
      if (e.button !== undefined && e.button !== 0) return;
      dragging = true;
      moved = false;
      el.setPointerCapture?.(e.pointerId);
      const r = el.getBoundingClientRect();
      ox = e.clientX - r.left;
      oy = e.clientY - r.top;
      el.style.zIndex = "40";
    };
    const move = e => {
      if (!dragging) return;
      moved = true;
      const sr = scene.getBoundingClientRect();
      const er = el.getBoundingClientRect();
      const left = Math.max(0, Math.min(sr.width - er.width, e.clientX - sr.left - ox));
      const top = Math.max(68, Math.min(sr.height - er.height - 4, e.clientY - sr.top - oy));
      el.style.left = `${left}px`;
      el.style.top = `${top}px`;
      el.style.right = "auto";
      el.style.bottom = "auto";
    };
    const up = e => {
      if (!dragging) return;
      dragging = false;
      el.releasePointerCapture?.(e.pointerId);
      el.style.zIndex = "18";
      if (moved && el.tagName === "A") {
        el.dataset.wasDragged = "1";
        setTimeout(() => delete el.dataset.wasDragged, 0);
      }
    };
    el.addEventListener("click", e => {
      if (el.dataset.wasDragged === "1") e.preventDefault();
    });
    el.addEventListener("pointerdown", down);
    el.addEventListener("pointermove", move);
    el.addEventListener("pointerup", up);
    el.addEventListener("pointercancel", up);
  };
  qsa("[data-ws-drag]").forEach(makeDraggable);

  const deck = qs("[data-ws-deck]");
  if (deck) {
    let dragged = null;
    qsa(".ws-card", deck).forEach(card => {
      card.draggable = true;
      card.addEventListener("dragstart", () => { dragged = card; card.style.opacity = ".5"; });
      card.addEventListener("dragend", () => { card.style.opacity = ""; dragged = null; });
      card.addEventListener("dragover", e => e.preventDefault());
      card.addEventListener("drop", e => {
        e.preventDefault();
        if (!dragged || dragged === card) return;
        const cards = qsa(".ws-card", deck);
        const a = cards.indexOf(dragged), b = cards.indexOf(card);
        if (a < b) card.after(dragged); else card.before(dragged);
      });
    });
  }

  const phrase = qs("[data-ws-phrase]");
  qsa("[data-ws-phrase-button]").forEach(btn => {
    btn.addEventListener("click", () => {
      qsa("[data-ws-phrase-button]").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      if (phrase) {
        phrase.animate?.([{opacity:.15,transform:"translateY(8px)"},{opacity:1,transform:"translateY(0)"}],{duration:300});
        phrase.textContent = btn.dataset.wsPhraseButton;
      }
    });
  });

  qsa("[data-ws-ripple]").forEach(btn => {
    btn.addEventListener("click", e => {
      const r = scene.getBoundingClientRect();
      const ripple = document.createElement("span");
      ripple.className = "ws-ripple";
      ripple.style.left = `${e.clientX - r.left}px`;
      ripple.style.top = `${e.clientY - r.top}px`;
      scene.appendChild(ripple);
      setTimeout(() => ripple.remove(), 850);
    });
  });
})();
