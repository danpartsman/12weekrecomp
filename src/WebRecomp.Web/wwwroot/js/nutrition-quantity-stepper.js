(() => {
    const stepQuantity = (value, direction) => Math.min(1000, Math.max(.01, Math.round(((Number(value) || 0) + direction) * 10000) / 10000));
    if (typeof module !== 'undefined') module.exports = { stepQuantity };
    if (typeof document === 'undefined') return;
    document.querySelectorAll('[data-quantity-stepper]').forEach(root => {
        const input = root.querySelector('input');
        const apply = direction => { input.value = String(stepQuantity(input.value, direction)); input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new Event('change', { bubbles: true })); };
        root.querySelectorAll('[data-quantity-direction]').forEach(button => button.addEventListener('click', () => apply(Number(button.dataset.quantityDirection))));
        input.addEventListener('keydown', event => { if (event.key === 'ArrowUp' || event.key === 'ArrowDown') { event.preventDefault(); apply(event.key === 'ArrowUp' ? 1 : -1); } });
    });
})();
