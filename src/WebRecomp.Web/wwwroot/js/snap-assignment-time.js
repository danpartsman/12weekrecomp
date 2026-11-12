(() => {
    document.querySelectorAll('[data-snap-deadline]').forEach(el => {
        try { el.textContent = new Intl.DateTimeFormat(undefined, {timeZone:el.dataset.timeZone,dateStyle:'medium',timeStyle:'short'}).format(new Date(el.dataset.snapDeadline)) + ' (' + el.dataset.timeZone + ')'; } catch (_) { /* Keep server-rendered time. */ }
    });
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const output=form.querySelector('[data-required-completion]'),date=form.elements.Date,hours=form.elements.TimeAllowed;
    const zone=output.dataset.timeZone;let sequence=0,debounce;
    async function update(){
        const current=++sequence,amount=Number(hours.value);
        if(!date.value||!(amount>0&&amount<=168)){output.textContent='Enter a valid date and hours';return;}
        if(!form.dataset.timingUrl){output.textContent='Calculated from calendar when assigned';return;}
        output.textContent='Checking available time…';
        try{
            const url=new URL(form.dataset.timingUrl,location.origin);
            url.searchParams.set('date',date.value);url.searchParams.set('minutes',String(Math.ceil(amount*60)));
            const response=await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok)throw new Error('Unavailable');
            const timing=await response.json();if(current!==sequence)return;
            output.textContent=timing.deadlineUtc
                ?new Intl.DateTimeFormat(undefined,{timeZone:zone,dateStyle:'medium',timeStyle:'short'}).format(new Date(timing.deadlineUtc))+' ('+zone+') · calendar adjusted'
                :'Waiting for available calendar time';
        }catch{if(current===sequence)output.textContent='Calculated from calendar when assigned';}
    }
    function schedule(){++sequence;clearTimeout(debounce);debounce=setTimeout(update,250);}
    date.addEventListener('input',schedule);hours.addEventListener('input',schedule);update();setInterval(update,30000);
})();
