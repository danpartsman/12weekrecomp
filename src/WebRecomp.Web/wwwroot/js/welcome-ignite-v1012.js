(() => {
  'use strict';

  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const nav = document.querySelector('[data-wi-nav]');
  const progress = document.querySelector('.wi-progress span');
  const reveals = [...document.querySelectorAll('[data-reveal]')];
  const tiltCards = [...document.querySelectorAll('[data-tilt]')];
  const hero = document.querySelector('.wi-hero');
  const heroPhoto = document.querySelector('.wi-hero-photo');
  const heroRing = document.querySelector('.wi-hero-ring');
  const orbs = [...document.querySelectorAll('.wi-orb')];

  const updateScroll = () => {
    const y = window.scrollY || 0;
    if (nav) nav.classList.toggle('is-scrolled', y > 24);

    if (progress) {
      const max = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
      progress.style.width = `${Math.min(100, (y / max) * 100)}%`;
    }

    if (!reduced && hero && y < window.innerHeight * 1.25) {
      const drift = Math.min(46, y * .045);
      if (heroPhoto) heroPhoto.style.transform = `scale(1.035) translate3d(0, ${drift}px, 0)`;
      if (heroRing) heroRing.style.transform = `translateY(calc(-50% + ${drift * .18}px)) rotate(${y * .012}deg)`;
    }
  };

  const revealObserver = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add('is-visible');
      revealObserver.unobserve(entry.target);
    });
  }, { threshold: .12, rootMargin: '0px 0px -6% 0px' });

  reveals.forEach((el, i) => {
    if (!reduced) el.style.transitionDelay = `${Math.min(i % 4, 3) * 70}ms`;
    revealObserver.observe(el);
  });

  if (!reduced) {
    tiltCards.forEach((card) => {
      card.addEventListener('pointermove', (event) => {
        const rect = card.getBoundingClientRect();
        const px = (event.clientX - rect.left) / rect.width;
        const py = (event.clientY - rect.top) / rect.height;
        const rx = (0.5 - py) * 4.5;
        const ry = (px - 0.5) * 5.5;
        card.style.transform = `perspective(1100px) rotateX(${rx}deg) rotateY(${ry}deg) translateY(-3px)`;
      });
      card.addEventListener('pointerleave', () => {
        card.style.transform = '';
      });
    });

    if (hero) {
      hero.addEventListener('pointermove', (event) => {
        const rect = hero.getBoundingClientRect();
        const px = (event.clientX - rect.left) / rect.width - .5;
        const py = (event.clientY - rect.top) / rect.height - .5;
        orbs.forEach((orb, index) => {
          const strength = 18 + (index * 8);
          orb.style.translate = `${px * strength}px ${py * strength}px`;
        });
      });
      hero.addEventListener('pointerleave', () => {
        orbs.forEach((orb) => { orb.style.translate = ''; });
      });
    }
  }

  let ticking = false;
  window.addEventListener('scroll', () => {
    if (ticking) return;
    ticking = true;
    requestAnimationFrame(() => {
      updateScroll();
      ticking = false;
    });
  }, { passive: true });

  updateScroll();
})();
