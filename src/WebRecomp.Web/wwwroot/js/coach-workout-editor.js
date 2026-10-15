(() => {
    const form = document.getElementById('coach-workout-editor');
    const rows = document.getElementById('coach-exercises');
    if (!form || !rows) return;
    const customize = () => { const box = form.querySelector('[name="Customize"][type="checkbox"]'); if (box) box.checked = true; };
    const index = () => [...rows.children].forEach((row, i) => row.querySelectorAll('[data-field]').forEach(input => input.name = `Exercises[${i}].${input.dataset.field}`));
    rows.addEventListener('click', event => { if (event.target.closest('[data-remove-exercise]')) { event.target.closest('tr').remove(); index(); customize(); } });
    rows.addEventListener('input', customize);
    document.getElementById('add-coach-exercise')?.addEventListener('click', () => {
        const picker = document.getElementById('exercise-picker');
        if (!picker.value || rows.children.length >= 40) return;
        const name = picker.selectedOptions[0].textContent;
        const row = document.createElement('tr');
        const cell = row.insertCell(); cell.textContent = name;
        for (const [field,value] of [['ExerciseId',picker.value],['Name',name]]) { const input = document.createElement('input'); input.type='hidden'; input.dataset.field=field; input.value=value; cell.append(input); }
        for (const [field,type,value,min,max] of [['Sets','number','3','1','20'],['Reps','text','10','',''],['WeightLb','number','','0','2000']]) { const input=document.createElement('input'); input.dataset.field=field; input.type=type; input.value=value; input.style.width=field==='Reps'?'10em':'6em'; input.setAttribute('aria-label',`${name} ${field}`); if(min)input.min=min;if(max)input.max=max;if(field==='WeightLb')input.step='any';row.insertCell().append(input); }
        const remove=document.createElement('button');remove.type='button';remove.dataset.removeExercise='';remove.textContent='Remove';row.insertCell().append(remove);rows.append(row);index();customize();picker.value='';
    });
})();
