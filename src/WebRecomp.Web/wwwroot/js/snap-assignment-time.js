(() => {
    document.querySelectorAll('[data-snap-deadline]').forEach(el => {
        try { el.textContent = new Intl.DateTimeFormat(undefined, {timeZone:el.dataset.timeZone,dateStyle:'medium',timeStyle:'short'}).format(new Date(el.dataset.snapDeadline)) + ' (' + el.dataset.timeZone + ')'; } catch (_) { /* Keep server-rendered time. */ }
    });
    const form=document.querySelector('[data-simple-assignment]');if(!form)return;
    const output=form.querySelector('[data-required-completion]'),date=form.elements.Date,hours=form.elements.TimeAllowed;
    const initial=Date.parse(output.dataset.serverNow),started=performance.now(),zone=output.dataset.timeZone;
    function midnight(day) {
        const target=Date.parse(day+'T00:00:00Z');let value=target;
        const formatter=new Intl.DateTimeFormat('en-US',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'});
        for(let i=0;i<4;i++) {
            const p=Object.fromEntries(formatter.formatToParts(new Date(value)).map(p=>[p.type,p.value]));
            const shown=Date.UTC(+p.year,+p.month-1,+p.day,+p.hour,+p.minute,+p.second);
            value+=target-shown;
        }
        return value;
    }
    function update() {
        const amount=Number(hours.value);if(!date.value||!(amount>0&&amount<=168)){output.textContent='Enter a valid date and hours';return;}
        try {
            const now=initial+performance.now()-started;
            const parts=Object.fromEntries(new Intl.DateTimeFormat('en-US',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(now)).map(p=>[p.type,p.value]));
            const today=`${parts.year}-${parts.month}-${parts.day}`;
            const start=date.value>today?midnight(date.value):now;
            const end=new Date(start+Math.ceil(amount*60)*60000);
            output.textContent=new Intl.DateTimeFormat(undefined,{timeZone:zone,dateStyle:'medium',timeStyle:'short'}).format(end)+' ('+zone+')';
        } catch (_) {output.textContent='Calculated when assigned';}
    }
    date.addEventListener('input',update);hours.addEventListener('input',update);update();setInterval(update,30000);
})();
