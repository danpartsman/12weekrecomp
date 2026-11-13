(() => {
    document.querySelectorAll('[data-snap-deadline]').forEach(el => {
        try { el.textContent = new Intl.DateTimeFormat(undefined, {timeZone:el.dataset.timeZone,dateStyle:'medium',timeStyle:'short'}).format(new Date(el.dataset.snapDeadline)) + ' (' + el.dataset.timeZone + ')'; } catch (_) { /* Keep server-rendered time. */ }
    });
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const output=form.querySelector('[data-required-completion]'),date=form.elements.Date,hours=form.elements.TimeAllowed;
    const scheduleDays=form.querySelector('[data-schedule-days]'),override=form.elements.OverrideSchedule,choice=form.elements.ConflictChoice;
    const dialog=document.querySelector('[data-snap-conflict-dialog]');
    const conflictList=dialog?.querySelector('[data-conflict-list]'),extension=dialog?.querySelector('[data-conflict-extension]'),status=dialog?.querySelector('[data-conflict-status]'),conflictHours=dialog?.querySelector('[data-conflict-hours]');
    const extendButton=dialog?.querySelector('[data-conflict-extend]'),overrideButton=dialog?.querySelector('[data-conflict-override]'),cancelButton=dialog?.querySelector('[data-conflict-cancel]');
    const zone=output.dataset.timeZone;let sequence=0,debounce,allowSubmit=false,checking=false,submitter=null,currentConflict=null;
    const format=stamp=>new Intl.DateTimeFormat(undefined,{timeZone:zone,dateStyle:'medium',timeStyle:'short'}).format(new Date(stamp));
    function setBusy(busy){if(extendButton)extendButton.disabled=busy;if(overrideButton)overrideButton.disabled=busy;}
    function renderDays(data){
        if(!scheduleDays)return;
        scheduleDays.replaceChildren();
        for(const [index,day] of (data.days||[]).slice(0,1).entries()){
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
    function renderConflict(data){
        if(!dialog)return;
        const conflict=data.conflict;currentConflict=conflict;
        conflictList.replaceChildren();
        for(const block of (conflict?.conflicts||[]).slice(0,20)){
            const item=document.createElement('li');item.textContent=`${block.reason} · ${format(block.start)} – ${format(block.end)}`;conflictList.append(item);
        }
        extension.textContent=conflict?.extendedDeadlineUtc
            ?`Extended completion: ${format(conflict.extendedDeadlineUtc)} (${zone}). ${Math.round(conflict.extendedHours*100)/100} total hours, including reserved time; ${hours.value} available workout hours.`
            :'No available completion window was found. Change the date or available hours.';
        if(conflict&&!conflict.hasConflict)extension.textContent=`This window is available. Completion: ${format(data.deadlineUtc)} (${zone}).`;
        status.textContent=conflict?.canOverride&&conflict.overrideDeadlineUtc
            ?`Override completion: ${format(conflict.overrideDeadlineUtc)} (${zone}).`
            :conflict?.hasConflict?'No available override window was found. Change the date or hours.':'';
        extendButton.textContent=conflict?.hasConflict?'Extend until after conflict':'Assign workout';
        extendButton.disabled=!conflict?.extendedDeadlineUtc;
        overrideButton.disabled=!conflict?.hasConflict||!conflict?.canOverride;
    }
    async function update(){
        const current=++sequence,amount=Number(hours.value);
        if(!date.value||!(amount>0&&amount<=168)){output.textContent='Enter a valid date and hours';throw new Error('Invalid completion time');}
        if(!form.dataset.timingUrl)throw new Error('Schedule check unavailable');
        output.textContent='Checking available time…';
        if(dialog?.open)setBusy(true);
        try{
            const url=new URL(form.dataset.timingUrl,location.origin);
            url.searchParams.set('overrideSchedule','false');
            url.searchParams.set('date',date.value);url.searchParams.set('minutes',String(Math.ceil(amount*60)));
            const response=await fetch(url,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok)throw new Error('Schedule check unavailable');
            const timing=await response.json();if(current!==sequence)throw new Error('Completion time changed');
            renderDays(timing);
            output.textContent=timing.deadlineUtc?format(timing.deadlineUtc)+' ('+zone+') · calendar adjusted':'Waiting for available calendar time';
            if(dialog?.open)renderConflict(timing);
            return timing;
        }catch(error){
            if(current===sequence){output.textContent='Check the completion time and try again';if(scheduleDays)scheduleDays.textContent='Schedule unavailable. Open Client calendar to review reserved time.';if(dialog?.open){setBusy(true);status.textContent='Unable to check the schedule. Try again or cancel.';}}
            throw error;
        }
    }
    function backgroundUpdate(){update().catch(()=>{});}
    function resetChoice(){if(override)override.value='false';if(choice)choice.value='';}
    function schedule(){resetChoice();++sequence;clearTimeout(debounce);debounce=setTimeout(backgroundUpdate,250);}
    function showConflict(data){
        if(!dialog)return;
        conflictHours.value=hours.value;renderConflict(data);
        if(!dialog.open)dialog.showModal();
    }
    function submitChoice(selected){
        if(!currentConflict||!conflictHours.checkValidity())return;
        if(selected==='override'&&!currentConflict.canOverride)return;
        if(selected==='extend'&&!currentConflict.extendedDeadlineUtc)return;
        choice.value=selected;override.value=String(selected==='override');
        dialog.close();allowSubmit=true;
        form.requestSubmit(submitter||undefined);
    }
    form.addEventListener('submit',async event=>{
        if(allowSubmit){allowSubmit=false;return;}
        event.preventDefault();if(checking)return;
        checking=true;submitter=event.submitter||null;resetChoice();clearTimeout(debounce);
        try{
            const data=await update();
            if(data.conflict?.hasConflict)showConflict(data);
            else{allowSubmit=true;form.requestSubmit(submitter||undefined);}
        }catch{
            if(dialog){conflictHours.value=hours.value;conflictList.replaceChildren();extension.textContent='The schedule could not be checked. Nothing has been assigned.';status.textContent='Cancel and try again.';setBusy(true);if(!dialog.open)dialog.showModal();}
        }finally{checking=false;}
    });
    extendButton?.addEventListener('click',()=>submitChoice('extend'));
    overrideButton?.addEventListener('click',()=>submitChoice('override'));
    cancelButton?.addEventListener('click',()=>{resetChoice();dialog.close();});
    dialog?.addEventListener('cancel',resetChoice);
    conflictHours?.addEventListener('input',()=>{
        if(!conflictHours.checkValidity()){setBusy(true);status.textContent='Enter available hours between one minute and seven days.';return;}
        ++sequence;hours.value=conflictHours.value;resetChoice();setBusy(true);clearTimeout(debounce);debounce=setTimeout(backgroundUpdate,250);
    });
    date.addEventListener('input',schedule);hours.addEventListener('input',schedule);
    if(form.dataset.conflictRequired==='true')update().then(showConflict).catch(()=>{});else backgroundUpdate();
    setInterval(()=>{if(!checking&&!dialog?.open)backgroundUpdate();},30000);
})();
