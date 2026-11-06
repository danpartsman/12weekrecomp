(() => {
    document.querySelectorAll('[data-agreement-limit-row]').forEach(row => {
        const mode = row.querySelector('[data-limit-mode]');
        const lower = row.querySelector('[data-limit-lower]');
        const upper = row.querySelector('[data-limit-upper]');
        if (!mode || !lower || !upper) return;
        const update = () => {
            const actual = mode.value === 'actual';
            const unit = row.dataset.limitUnit;
            const maximum = unit === 'kcal' ? 30000 : 5000;
            row.querySelector('[data-limit-lower-cell]').dataset.label = actual ? `Minimum (${unit})` : 'Under system (%)';
            row.querySelector('[data-limit-upper-cell]').dataset.label = actual ? `Maximum (${unit})` : 'Over system (%)';
            lower.max = actual ? maximum : 100;
            upper.max = actual ? maximum : 1000;
            lower.setCustomValidity(actual && lower.value !== '' && upper.value !== '' && Number(lower.value) > Number(upper.value)
                ? 'Minimum cannot exceed maximum.' : '');
        };
        mode.addEventListener('change', update);
        lower.addEventListener('input', update);
        upper.addEventListener('input', update);
        update();
    });
})();
