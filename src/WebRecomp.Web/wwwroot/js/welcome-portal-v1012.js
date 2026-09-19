(() => {
    "use strict";

    const scene = document.querySelector("[data-wp-scene]");
    if (!scene) return;

    const glow = document.querySelector("[data-wp-glow]");
    const energyButton = document.querySelector("[data-wp-energy]");
    const paths = [...document.querySelectorAll(".wp-path")];
    const identity = document.querySelector(".wp-identity");

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    if (!reduced) {
        scene.addEventListener("pointermove", (event) => {
            const x = event.clientX;
            const y = event.clientY;
            if (glow) {
                glow.style.left = `${x}px`;
                glow.style.top = `${y}px`;
            }

            const nx = (x / window.innerWidth) - .5;
            const ny = (y / window.innerHeight) - .5;
            if (identity) {
                identity.style.translate = `${nx * 7}px ${ny * 5}px`;
            }
        }, { passive:true });

        paths.forEach((card) => {
            card.addEventListener("pointermove", (event) => {
                const rect = card.getBoundingClientRect();
                const x = (event.clientX - rect.left) / rect.width - .5;
                const y = (event.clientY - rect.top) / rect.height - .5;
                card.style.transform = `perspective(800px) rotateX(${(-y * 5).toFixed(2)}deg) rotateY(${(x * 6).toFixed(2)}deg) translateY(-6px)`;
            });
            card.addEventListener("pointerleave", () => {
                card.style.transform = "";
            });
        });
    }

    energyButton?.addEventListener("click", () => {
        const active = scene.classList.toggle("energy");
        energyButton.setAttribute("aria-pressed", active ? "true" : "false");
        energyButton.textContent = active ? "ENERGY MAX" : "ENERGY +";
    });
})();
