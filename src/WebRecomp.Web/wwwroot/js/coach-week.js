(() => {
  const form = document.getElementById('coach-week-form');
  if (!form) return;
  const days = [...form.querySelectorAll('.coach-week-workout')];
  const counts = [...form.querySelectorAll('[data-week-count]')];
  const notice = document.getElementById('coach-mix-notice');
  const sum = () => {
    const number = name => Number(form.elements.namedItem(name)?.value || 0);
    const protein = number('Rules.DailyProteinTarget');
    const carbs = number('Rules.WeeklyCarbsTarget');
    const fat = number('Rules.WeeklyFatTarget');
    const calories = protein * 28 + carbs * 4 + fat * 9;
    document.getElementById('coach-goal-total').textContent = `Weekly goal: ${(protein * 7).toLocaleString(undefined,{maximumFractionDigits:1})} g protein · ${carbs.toLocaleString()} g carbs · ${fat.toLocaleString()} g fat · ${calories.toLocaleString(undefined,{maximumFractionDigits:0})} calories (${(calories / 7).toLocaleString(undefined,{maximumFractionDigits:0})} daily average).`;
  };
  const selectedGroup = day => day.selectedOptions[0]?.dataset.group;
  const recount = () => {
    counts.forEach(count => {count.value = days.filter(day => selectedGroup(day) === count.dataset.weekCount).length;});
  };
  days.forEach(day => day.addEventListener('change', () => {recount();notice.textContent = 'Session counts updated to match your daily choices. Preview to recalculate targets.';}));
  form.addEventListener('input', sum);
  document.getElementById('coach-distribute').addEventListener('click', () => {
    const remaining = new Map(counts.map(count => [count.dataset.weekCount, Number(count.value)]));
    if ([...remaining.values()].some(value => !Number.isInteger(value) || value < 0 || value > 7) || [...remaining.values()].reduce((a,b) => a+b, 0) !== 7) {
      notice.textContent = 'Session counts must be whole numbers from zero to seven and total seven days.';return;
    }
    // Keep existing placements where the requested mix permits them.
    const replace = [];
    days.forEach(day => {const group=selectedGroup(day);if ((remaining.get(group) || 0) > 0) remaining.set(group,remaining.get(group)-1);else replace.push(day);});
    replace.forEach(day => {
      const group = [...remaining].find(([,count]) => count > 0)?.[0];
      const preferred = {'Hard strength':'FULL_BODY_STRENGTH',Accessory:'ACCESSORY',Conditioning:'TRUE_HIIT',Cardio:'ZONE2_CARDIO',Recovery:'MOBILITY_RECOVERY',Rest:'REST'}[group];
      const option = [...day.options].find(option => option.value === preferred && option.dataset.group === group) || [...day.options].find(option => option.dataset.group === group);
      if (!option) return;
      day.value = option.value;remaining.set(group,remaining.get(group)-1);
    });
    recount();notice.textContent = 'Session mix placed. Review exercise choices and recovery spacing before previewing.';
  });
  // Saving always validates a server-generated preview token; local changes require another preview.
  sum();
})();
