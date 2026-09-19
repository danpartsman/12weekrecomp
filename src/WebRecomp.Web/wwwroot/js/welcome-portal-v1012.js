(() => {
    "use strict";
    const stage = document.querySelector("[data-wp-stage]");
    const hero = document.querySelector("[data-wp-hero]");
    const cursor = document.querySelector("[data-wp-cursor]");
    const energy = document.querySelector("[data-wp-energy]");
    const rail = [...document.querySelectorAll("[data-wp-scene]")];
    const cards = [...document.querySelectorAll(".wp-path")];
    if (!stage || !hero) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    let locked = "main";

    const swap = (scene, button = null) => {
        if (!scene) return;
        hero.style.opacity = ".2";
        window.setTimeout(() => {
            stage.dataset.scene = scene === "main" ? "" : scene;
            hero.style.opacity = "1";
        }, 105);
        if (button) {
            rail.forEach(x => x.classList.remove("is-active"));
            button.classList.add("is-active");
            locked = scene;
        }
    };

    rail.forEach(button => button.addEventListener("click", () => swap(button.dataset.wpScene, button)));
    cards.forEach(card => {
        card.addEventListener("mouseenter", () => swap(card.dataset.wpPreview));
        card.addEventListener("mouseleave", () => swap(locked));
        if (!reduced) {
            card.addEventListener("pointermove", event => {
                const rect = card.getBoundingClientRect();
                const x = (event.clientX - rect.left) / rect.width - .5;
                const y = (event.clientY - rect.top) / rect.height - .5;
                card.style.transform = `perspective(900px) rotateX(${(-y * 5).toFixed(2)}deg) rotateY(${(x * 6).toFixed(2)}deg) translateY(-8px)`;
            });
            card.addEventListener("pointerleave", () => {
                card.style.transform = "";
                swap(locked);
            });
        }
    });

    if (!reduced) {
        stage.addEventListener("pointermove", e => {
            if (cursor) { cursor.style.left = `${e.clientX}px`; cursor.style.top = `${e.clientY}px`; }
            const nx = (e.clientX / innerWidth) - .5;
            const ny = (e.clientY / innerHeight) - .5;
            hero.style.transform = `scale(${stage.classList.contains("energy") ? 1.055 : 1.018}) translate(${nx * -7}px,${ny * -5}px)`;
        }, {passive:true});
        stage.addEventListener("pointerleave", () => hero.style.transform = "");
    }

    energy?.addEventListener("click", () => {
        const active = stage.classList.toggle("energy");
        energy.setAttribute("aria-pressed", active ? "true" : "false");
        energy.textContent = active ? "ENERGY MAX" : "ENERGY +";
    });
})();
