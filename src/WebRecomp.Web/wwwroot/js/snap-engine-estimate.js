(() => {
 const form=document.querySelector('[data-simple-assignment]');if(!form)return;
 const output=form.querySelector('[data-snap-estimate]');let timer,request,version=0;
 function schedule(){clearTimeout(timer);const current=++version;request?.abort();timer=setTimeout(async()=>{
  if(!form.querySelector('[data-simple-exercises] input[name$=".Name"]').value)return;
  request=new AbortController();output.textContent='Calculating…';
  try{const response=await fetch(form.dataset.estimateUrl,{method:'POST',body:new FormData(form),signal:request.signal,credentials:'same-origin'});const data=await response.json();if(current!==version)return;
   output.textContent=data.estimate?`Estimated ${Math.round(data.estimate.calories)} active calories · ${data.estimate.glycogen.toFixed(1)} g glycogen · ${data.estimate.minutes} estimated minutes`:data.message||'Estimate unavailable.';
  }catch(error){if(error.name!=='AbortError'&&current===version)output.textContent='Estimate unavailable. You can still assign this workout.';}
 },600);}
 form.addEventListener('input',schedule);form.addEventListener('click',event=>{if(event.target.closest('[data-add-exercise],[data-remove-exercise]'))schedule();});
})();
