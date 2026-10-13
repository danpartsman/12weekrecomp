(() => {
 const form=document.getElementById('scale-feel-form'); if(!form) return;
 const status=form.querySelector('#scale-feel-save-status');
 const retry=form.querySelector('[data-scale-retry]');
 window.RecompAutosave?.bind(form,{statusElement:status,delay:650,requireJson:true,afterSave:payload=>{
  if(typeof window.refreshDailyEntryCompletion === "function") void window.refreshDailyEntryCompletion();
  if(payload.renderedMeasurementId) form.querySelector('[name="RenderedMeasurementId"]').value=payload.renderedMeasurementId;
 }});
 if(status && retry) new MutationObserver(()=>{retry.hidden=status.dataset.state!=='error';}).observe(status,{attributes:true,attributeFilter:['data-state']});
 retry?.addEventListener('click',()=>form.requestSubmit());
})();
