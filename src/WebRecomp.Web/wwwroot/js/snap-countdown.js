(() => {
    const timers=[...document.querySelectorAll('[data-snap-countdown]')];if(!timers.length)return;
    let serverStart=Date.parse(timers[0].dataset.serverNow),anchor=performance.now(),busy=false,expiryChecked=false;
    const now=()=>serverStart+performance.now()-anchor;
    const zone=document.querySelector('[data-time-zone]')?.dataset.timeZone;
    function localDate(stamp){
        try{return new Intl.DateTimeFormat(undefined,{dateStyle:'medium',timeStyle:'short',timeZone:zone}).format(new Date(stamp));}
        catch{return new Date(stamp).toLocaleString();}
    }
    async function refresh(){
        if(busy||document.hidden)return;busy=true;
        try{
            const response=await fetch(location.pathname.replace(/\/$/,'')+'/status'+location.search,{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok||!response.headers.get('content-type')?.includes('application/json'))return;
            const data=await response.json();if(!Number.isFinite(Date.parse(data.nowUtc)))return;
            serverStart=Date.parse(data.nowUtc);anchor=performance.now();
            for(const timer of timers){
                const workout=data.workouts?.find(w=>w.id===timer.dataset.workoutId);if(!workout)continue;
                timer.dataset.deadline=workout.deadlineUtc||'';
                const timing=workout.timing;
                timer.dataset.remainingSeconds=timing?String(timing.remainingSeconds):'';
                timer.dataset.pausedUntil=timing?.pausedUntilUtc||'';
                timer.dataset.availableFrom=timing?.availableFromUtc||'';
                timer.dataset.pauseReason=timing?.reason||'';
                const card=document.getElementById('snap-'+workout.id);
                const deadline=card?.querySelector('[data-snap-deadline]');
                if(deadline){deadline.dataset.snapDeadline=workout.deadlineUtc||'';deadline.textContent=workout.deadlineUtc?localDate(workout.deadlineUtc):'Waiting for available time';}
                if(workout.finished){timer.dataset.finished='true';const form=card?.querySelector('[data-snap-results]');if(form)form.dataset.finished='true';const output=timer.querySelector('[data-time-remaining]');if(output)output.textContent=!workout.deadlineUtc||Date.parse(workout.finishedAtUtc)<=Date.parse(workout.deadlineUtc)?'Finished on time':'Finished after the deadline';}
            }
            draw();
        }catch{/* Server-side deadline processing remains authoritative. */}finally{busy=false;}
    }
    function draw(){
        let expired=false;
        for(const timer of timers){
            if(timer.dataset.finished==='true')continue;
            const pauseEnd=Math.max(Date.parse(timer.dataset.pausedUntil)||0,Date.parse(timer.dataset.availableFrom)||0);
            const paused=now()<pauseEnd||!timer.dataset.deadline;
            const supplied=timer.dataset.remainingSeconds;
            const remaining=Math.max(0,Math.ceil(supplied!==''&&supplied!==undefined
                ?Number(supplied)-(paused?0:(performance.now()-anchor)/1000)
                :(Date.parse(timer.dataset.deadline)-now())/1000));
            const duration=`${Math.floor(remaining/3600)}h ${Math.floor(remaining%3600/60)}m ${remaining%60}s`;
            const text=paused?`Paused · ${timer.dataset.pauseReason||'Unavailable'} · ${duration} available time${pauseEnd?` · resumes ${localDate(pauseEnd)}`:''}`
                :remaining?`${duration} available time remaining`:'Time expired · the late-completion fine applies.';
            const output=timer.querySelector('[data-time-remaining]');if(output)output.textContent=text;
            if(!remaining&&!paused)expired=true;
        }
        if(expired&&!expiryChecked){expiryChecked=true;refresh();}
    }
    setInterval(draw,1000);setInterval(refresh,30000);window.addEventListener('focus',refresh);document.addEventListener('visibilitychange',refresh);draw();refresh();
})();
