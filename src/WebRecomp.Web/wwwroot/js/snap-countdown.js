(() => {
    const timers=[...document.querySelectorAll('[data-snap-countdown]')];if(!timers.length)return;
    let serverStart=Date.parse(timers[0].dataset.serverNow),anchor=performance.now(),busy=false,expiryChecked=false;
    const now=()=>serverStart+performance.now()-anchor;
    async function refresh(){
        if(busy||document.hidden)return;busy=true;
        try{
            const response=await fetch(location.pathname.replace(/\/$/,'')+'/status',{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
            if(!response.ok||!response.headers.get('content-type')?.includes('application/json'))return;
            const data=await response.json();if(!Number.isFinite(Date.parse(data.nowUtc)))return;
            serverStart=Date.parse(data.nowUtc);anchor=performance.now();
            for(const timer of timers){const workout=data.workouts?.find(w=>w.id===timer.dataset.workoutId);if(workout?.finished){timer.dataset.finished='true';const output=timer.querySelector('[data-time-remaining]');if(output)output.textContent=Date.parse(workout.finishedAtUtc)<=Date.parse(timer.dataset.deadline)?'Finished on time':'Finished after the deadline';}}
            draw();
        }catch{/* Server-side deadline processing remains authoritative. */}finally{busy=false;}
    }
    function draw(){
        let expired=false;
        for(const timer of timers){
            if(timer.dataset.finished==='true')continue;
            const remaining=Math.max(0,Math.ceil((Date.parse(timer.dataset.deadline)-now())/1000));
            const text=remaining?`${Math.floor(remaining/3600)}h ${Math.floor(remaining%3600/60)}m ${remaining%60}s remaining`:'Time expired · the late-completion fine applies.';
            const output=timer.querySelector('[data-time-remaining]');if(output)output.textContent=text;
            if(!remaining)expired=true;
        }
        if(expired&&!expiryChecked){expiryChecked=true;refresh();}
    }
    setInterval(draw,1000);setInterval(refresh,30000);window.addEventListener('focus',refresh);document.addEventListener('visibilitychange',refresh);draw();refresh();
})();
