(() => {
 'use strict';
 document.querySelectorAll('[data-app-navigation]').forEach(nav=>{
  const groups=[...nav.querySelectorAll('.app-nav-group')],toggle=nav.querySelector('.app-nav-toggle'),mobile=()=>matchMedia('(max-width:820px)').matches;
  const storageKey='12wr-nav-flyout',timers=new Map();
  const summary=group=>group.querySelector('summary'),panel=group=>group.querySelector('.app-nav-panel');
  const items=group=>[...group.querySelectorAll('.app-nav-panel a,.app-nav-panel button')];
  function stop(group){clearTimeout(timers.get(group));}
  function menu(open){nav.dataset.menuOpen=String(open);toggle?.setAttribute('aria-expanded',String(open));if(!open)groups.forEach(g=>{g.open=false;stop(g);});}
  function closeOthers(keep){groups.forEach(g=>{if(g!==keep){g.open=false;stop(g);}});}
  function expire(group){stop(group);timers.set(group,setTimeout(()=>{
   if(panel(group).matches(':hover')||panel(group).contains(document.activeElement)){expire(group);return;}
   group.open=false;if(mobile())menu(false);
  },5000));}
  function open(group){closeOthers(group);group.open=true;if(mobile())menu(true);expire(group);}
  function remember(group){const link=group.querySelector('.app-nav-primary');try{sessionStorage.setItem(storageKey,JSON.stringify({path:new URL(link.href,location.href).pathname,at:Date.now()}));}catch{}}
  toggle?.addEventListener('click',()=>menu(nav.dataset.menuOpen!=='true'));
  groups.forEach(group=>{
   summary(group).addEventListener('click',event=>{
    if(event.button!==0||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;
    remember(group);
    if(!event.target.closest('a')){event.preventDefault();location.assign(group.querySelector('.app-nav-primary').href);}
   });
   group.addEventListener('toggle',()=>{if(group.open){closeOthers(group);expire(group);}else stop(group);});
   panel(group).addEventListener('pointerenter',()=>stop(group));panel(group).addEventListener('pointerleave',()=>expire(group));
   panel(group).addEventListener('focusin',()=>stop(group));panel(group).addEventListener('focusout',()=>expire(group));
   group.addEventListener('keydown',event=>{
    const links=items(group),index=links.indexOf(document.activeElement);
    if(event.key==='Escape'){event.preventDefault();group.open=false;stop(group);group.querySelector('.app-nav-primary').focus();}
    else if(event.key==='ArrowDown'||event.key==='ArrowUp'){
     event.preventDefault();open(group);const next=index<0?(event.key==='ArrowDown'?0:links.length-1):(index+(event.key==='ArrowDown'?1:-1)+links.length)%links.length;links[next]?.focus();
    }else if(index>=0&&(event.key==='Home'||event.key==='End')){event.preventDefault();links[event.key==='Home'?0:links.length-1]?.focus();}
   });
  });
  nav.addEventListener('click',event=>{if(event.target.closest('.app-nav-panel a')){try{sessionStorage.removeItem(storageKey);}catch{}closeOthers(null);menu(false);}});
  document.addEventListener('click',event=>{if(!nav.contains(event.target)){closeOthers(null);menu(false);}});
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!event.target.closest('.app-nav-group')){menu(false);toggle?.focus();}});
  document.addEventListener('focusin',event=>{if(!nav.contains(event.target))closeOthers(null);});
  const normalized=path=>path.replace(/\/+$/,'').toLowerCase()||'/';
  nav.querySelectorAll('a[href]').forEach(link=>{if(normalized(new URL(link.href,location.href).pathname)===normalized(location.pathname))link.setAttribute('aria-current','page');});
  try{const saved=JSON.parse(sessionStorage.getItem(storageKey)||'null');sessionStorage.removeItem(storageKey);
   if(saved&&Date.now()-saved.at<30000&&normalized(saved.path)===normalized(location.pathname)){
    const group=groups.find(g=>normalized(new URL(g.querySelector('.app-nav-primary').href,location.href).pathname)===normalized(saved.path));if(group)open(group);
   }
  }catch{}
 });
})();
