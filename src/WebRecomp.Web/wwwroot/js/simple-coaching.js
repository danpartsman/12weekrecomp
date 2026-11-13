(() => {
    const errors=document.querySelector('[data-snap-validation].validation-summary-errors');
    if(errors){errors.focus();errors.scrollIntoView({block:'center'});}
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const editing=Boolean(form.querySelector('[name="EditWorkoutId"]')?.value);
    form.querySelectorAll('[data-snap-fee-default]').forEach(input=>{
        if(!editing&&Number(input.value)===Number(input.dataset.snapFeeDefault))input.classList.add('snap-fee-default');
        input.addEventListener('input',()=>input.classList.remove('snap-fee-default'));
    });
    const picker=form.querySelector('.snap-workout-picker');
    const toggle=picker?.querySelector('[data-workout-toggle]');
    const history=picker?.querySelector('.snap-workout-history');
    function closeHistory(){if(history){history.hidden=true;toggle.setAttribute('aria-expanded','false');}}
    toggle?.addEventListener('click',()=>{history.hidden=!history.hidden;toggle.setAttribute('aria-expanded',String(!history.hidden));});
    document.addEventListener('click',event=>{if(picker&&!picker.contains(event.target))closeHistory();});
    picker?.addEventListener('keydown',event=>{if(event.key==='Escape'){closeHistory();toggle.focus();}if(event.key==='ArrowDown'&&event.target===toggle){event.preventDefault();history.hidden=false;toggle.setAttribute('aria-expanded','true');history.querySelector('a')?.focus();}});
    const list=form.querySelector('[data-simple-exercises]');
    function renumber(){[...list.children].forEach((card,i)=>card.querySelectorAll('[name]').forEach(n=>n.name=n.name.replace(/Exercises\[\d+\]/,`Exercises[${i}]`)));}
    form.querySelector('[data-add-exercise]').addEventListener('click',()=>{
        if(list.children.length>=15)return;
        const copy=list.firstElementChild.cloneNode(true);
        copy.querySelectorAll('input').forEach(n=>{n.value=(n.name.endsWith('.Id')||n.name.endsWith('.Name')||n.name.endsWith('.MuscleFocus')||n.name.endsWith('.Rpe')||n.name.endsWith('.MaxWeight'))?'':n.name.endsWith('.Sets')?'3':n.name.endsWith('.Reps')?'10':'0';});
        copy.querySelectorAll('details').forEach(n=>n.open=false);list.append(copy);renumber();copy.querySelector('input[name$=".Name"]').focus();
    });
    list.addEventListener('click',event=>{if(event.target.closest('[data-remove-exercise]')&&list.children.length>1){event.target.closest('[data-exercise]').remove();renumber();}});
})();
