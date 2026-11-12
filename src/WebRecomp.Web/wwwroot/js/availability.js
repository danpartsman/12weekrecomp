(() => {
    const form=document.querySelector('[data-availability-form]');if(!form)return;
    const kind=form.querySelector('[data-block-kind]'),repeat=form.querySelector('[data-repeat]');
    function update(){
        const illness=kind.value==='illness';
        for(const field of form.querySelectorAll('[data-block-time]')){field.hidden=illness;field.querySelector('input').disabled=illness;}
        form.querySelector('[data-block-repeat]').hidden=illness;
        if(illness)repeat.checked=false;
        form.querySelector('[data-repeat-days]').hidden=!repeat.checked;
        for(const day of form.querySelectorAll('[name=Days]'))day.disabled=illness||!repeat.checked;
        form.querySelector('[data-block-help]').textContent=illness
            ?'Illness protects the whole day. Set a last date to protect several consecutive days.'
            :'Choose the days the block starts. Overnight blocks end the following morning. Without Repeat weekly, only the first date is used.';
    }
    kind.addEventListener('change',update);repeat.addEventListener('change',update);update();
})();
