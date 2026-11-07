(()=>{
 const dialog=document.querySelector('[data-coach-activity]');if(!dialog)return;
 const list=dialog.querySelector('[data-coach-activity-list]'),status=dialog.querySelector('[role=status]');let busy=false,current=[];
 async function refresh(){if(busy||document.hidden||dialog.open)return;busy=true;try{
  const response=await fetch('/coach/activity-notices',{credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});if(!response.ok)return;
  const data=await response.json();if(!Array.isArray(data.notices)||!data.notices.length)return;
  current=data.notices.filter(x=>typeof x.id==='string'&&typeof x.title==='string'&&typeof x.url==='string'&&/^\/(coach-workspace|clients)\//.test(x.url));if(!current.length)return;
  list.replaceChildren();for(const notice of current){const item=document.createElement('li'),link=document.createElement('a');link.textContent=notice.title;link.href=notice.url;item.append(link);list.append(item);}status.textContent='';dialog.showModal();
 }catch{}finally{busy=false;}}
 dialog.querySelector('[data-dismiss]').addEventListener('click',async()=>{
  if(busy)return;busy=true;try{const body=new URLSearchParams();body.set('__RequestVerificationToken',dialog.querySelector('input[name=__RequestVerificationToken]').value);for(const n of current)body.append('ids',n.id);
   const response=await fetch('/coach/activity-notices/dismiss',{method:'POST',credentials:'same-origin',body});if(!response.ok)throw Error();dialog.close();
  }catch{status.textContent='Could not dismiss these updates. Please try again.';}finally{busy=false;}
 });
 dialog.querySelector('[data-close]').addEventListener('click',()=>dialog.close());
 window.addEventListener('focus',refresh);document.addEventListener('visibilitychange',refresh);setInterval(refresh,30000);refresh();
})();
