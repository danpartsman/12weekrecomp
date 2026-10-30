(() => {
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const list=form.querySelector('[data-simple-exercises]');
    function renumber(){[...list.children].forEach((card,i)=>card.querySelectorAll('[name]').forEach(n=>n.name=n.name.replace(/Exercises\[\d+\]/,`Exercises[${i}]`)));}
    form.querySelector('[data-add-exercise]').addEventListener('click',()=>{
        if(list.children.length>=15)return;
        const copy=list.firstElementChild.cloneNode(true);
        copy.querySelectorAll('input').forEach(n=>{n.value=n.name.endsWith('.Name')?'':n.name.endsWith('.Sets')?'3':n.name.endsWith('.Reps')?'10':'0';});
        copy.querySelectorAll('details').forEach(n=>n.open=false);list.append(copy);renumber();copy.querySelector('input').focus();
    });
    list.addEventListener('click',event=>{if(event.target.closest('[data-remove-exercise]')&&list.children.length>1){event.target.closest('[data-exercise]').remove();renumber();}});
})();
