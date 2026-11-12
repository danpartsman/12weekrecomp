(() => {
    document.querySelectorAll('[data-snap-deadline]').forEach(el => {
        try { el.textContent = new Intl.DateTimeFormat(undefined, {timeZone:el.dataset.timeZone,dateStyle:'medium',timeStyle:'short'}).format(new Date(el.dataset.snapDeadline)) + ' (' + el.dataset.timeZone + ')'; } catch (_) { /* Keep server-rendered time. */ }
    });
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const output=form.querySelector('[data-required-completion]'),date=form.elements.Date,hours=form.elements.TimeAllowed;
    const scheduleDays=form.querySelector('[data-schedule-days]'),override=form.elements.OverrideSchedule,overrideButton=form.querySelector('[data-schedule-override]'),overrideStatus=form.querySelector('[data-schedule-override-status]');
    const zone=output.dataset.timeZone;let sequence=0,debounce;
    function renderDays(data){
        if(!scheduleDays)return;
        scheduleDays.replaceChildren();
        for(const [index,day] of (data.days||[]).entries()){
            const details=document.createElement('details'),summary=document.createElement('summary'),list=document.createElement('ul');
            details.open=index===0;
            const minutes=Math.round(day.reservedMinutes||0);
            summary.textContent=`${day.label} · ${day.workout} · ${Math.floor(minutes/60)}h ${minutes%60}m reserved`;
            details.append(summary);
            for(const block of day.reserved||[]){
                const item=document.createElement('li');
                const time=stamp=>new Intl.DateTimeFormat(undefined,{timeZone:data.timeZone||zone,hour:'numeric',minute:'2-digit'}).format(new Date(stamp));
                item.textContent=`${block.label} · ${block.allDay?'All day':time(block.start)+' – '+time(block.end)}`;
                list.append(item);
            }
            if(!list.children.length){const item=document.createElement('li');item.textContent='No reserved hours';list.append(item);}
            details.append(list);scheduleDays.append(details);
        }
    }
    async function update(){
        const current=++sequence,amount=Number(hours.value);
        if(!date.value||!(amount>0&&amount<=168)){output.textContent='Enter a valid date and hours';return;}
        if(!form.dataset.timingUrl){output.textContent='Calculated from calendar when assigned';return;}
        output.textContent='Checking available time…';
        try{
            const url=new URL(form.dataset.timingUrl,location.origin);
            url.searchParams.set('overrideSchedule',override?.value||'false');
            url.searchParams.set('date',date.value);url.searchParams.set('minutes',String(Math.ceil(amount*60)));
            const response=await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok)throw new Error('Unavailable');
            const timing=await response.json();if(current!==sequence)return;
            renderDays(timing);
            output.textContent=timing.deadlineUtc
                ?new Intl.DateTimeFormat(undefined,{timeZone:zone,dateStyle:'medium',timeStyle:'short'}).format(new Date(timing.deadlineUtc))+' ('+zone+') · '+(timing.overrideSchedule?'schedule override':'calendar adjusted')
                :'Waiting for available calendar time';
        }catch{if(current===sequence){output.textContent='Calculated from calendar when assigned';if(scheduleDays)scheduleDays.textContent='Schedule unavailable. Open Client calendar to review reserved time.';}}
    }
    function schedule(){++sequence;clearTimeout(debounce);debounce=setTimeout(update,250);}
    overrideButton?.addEventListener('click',()=>{
        const enabled=override.value!=='true';override.value=String(enabled);
        overrideButton.setAttribute('aria-pressed',String(enabled));
        overrideButton.textContent=enabled?'Use saved schedule':'Override schedule';
        if(overrideStatus)overrideStatus.textContent=enabled
            ?'This Snap ignores reserved sleep, work and workout hours. Illness days remain protected.'
            :'The completion clock respects the client’s reserved hours.';
        schedule();
    });
    date.addEventListener('input',schedule);hours.addEventListener('input',schedule);update();setInterval(update,30000);
})();
