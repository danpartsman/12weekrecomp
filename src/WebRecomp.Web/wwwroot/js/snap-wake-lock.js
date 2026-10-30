(() => {
 const forms=[...document.querySelectorAll('[data-snap-results]')];if(!forms.length)return;
 let lock=null,pending=false,pageActive=true,lastAttempt=0;
 const active=()=>pageActive&&!document.hidden&&forms.some(form=>form.dataset.finished!=='true'&&form.dataset.canRecord==='true'&&form.closest('details')?.open);
 function status(text,retry=false){for(const form of forms){const show=form.dataset.finished!=='true'&&form.dataset.canRecord==='true';const output=form.querySelector('[data-snap-wake-status]'),button=form.querySelector('[data-snap-wake-retry]');if(output)output.textContent=show?text:'';if(button)button.hidden=!show||!retry;}}
 async function release(){const current=lock;lock=null;if(current)try{await current.release();}catch{} }
 async function sync(gesture=false){
  if(!active()){await release();status('');return;}
  if(lock&&!lock.released||pending)return;
  if(!navigator.wakeLock){status('Screen wake lock is unavailable in this browser.');return;}
  if(!gesture&&Date.now()-lastAttempt<15000)return;
  lastAttempt=Date.now();pending=true;
  try{const acquired=await navigator.wakeLock.request('screen');if(!active()){await acquired.release();return;}lock=acquired;status('Screen stays awake while this workout is open.');acquired.addEventListener('release',()=>{if(lock!==acquired)return;lock=null;if(active())status('Screen may sleep.',true);},{once:true});}
  catch{status('Screen may sleep.',true);}finally{pending=false;}
 }
 for(const form of forms){form.closest('details')?.addEventListener('toggle',()=>sync(true));form.addEventListener('pointerdown',()=>sync(true));form.querySelector('[data-snap-wake-retry]')?.addEventListener('click',()=>sync(true));form.addEventListener('submit',()=>{pageActive=false;void release();});}
 document.addEventListener('visibilitychange',()=>sync(true));window.addEventListener('pagehide',()=>{pageActive=false;void release();});window.addEventListener('pageshow',()=>{pageActive=true;void sync(true);});
 new MutationObserver(()=>sync()).observe(document.body,{subtree:true,attributes:true,attributeFilter:['data-finished']});
 setInterval(()=>sync(),15000);void sync();
})();
