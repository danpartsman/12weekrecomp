(() => {
    const stepQuantity = (value, direction) => Math.min(1000, Math.max(.01, Math.round(((Number(value) || 0) + direction) * 10000) / 10000));
    if (typeof module !== 'undefined') module.exports = { stepQuantity };
    if (typeof document === 'undefined') return;
    document.querySelectorAll('[data-quantity-stepper]').forEach(root => {
        const input = root.querySelector('input');
        const apply = direction => { input.value = String(stepQuantity(input.value, direction)); input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new Event('change', { bubbles: true })); };
        input.inputMode = 'decimal';
        // Keep the browser's arrows inside the field, matching the food picker.
        // step="any" allows fractions; these handlers make each arrow move by 1.
        input.addEventListener('mousedown', event => {
            if (event.button !== 0) return;
            const rect = input.getBoundingClientRect();
            const spinnerWidth = Math.min(34, rect.width * .28);
            if (event.clientX < rect.right - spinnerWidth) return;
            event.preventDefault();
            input.focus();
            apply(event.clientY < rect.top + rect.height / 2 ? 1 : -1);
        });
        input.addEventListener('keydown', event => { if (event.key === 'ArrowUp' || event.key === 'ArrowDown') { event.preventDefault(); apply(event.key === 'ArrowUp' ? 1 : -1); } });
    });
})();
