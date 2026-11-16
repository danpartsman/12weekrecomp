const {test}=require('node:test');
const assert=require('node:assert/strict');
const {JSDOM}=require(process.env.SNAP_JSDOM_MODULE||'jsdom');
const fs=require('node:fs');
const source=fs.readFileSync('src/WebRecomp.Web/wwwroot/js/app-navigation.js','utf8');
function setup({touch=true}={}){
 const dom=new JSDOM(`<nav data-app-navigation><button class="app-nav-toggle"></button><details class="app-nav-group"><summary><a class="app-nav-primary" href="/CommandCenter?nav=command-center">Command Center</a></summary><div class="app-nav-panel"><a href="/Dashboard">Dashboard</a><a href="/Progress">Progress</a></div></details><a class="app-nav-direct" href="/MyCoach">My Coach</a></nav><button id="outside">Outside</button>`,{url:'https://navigation.test/CommandCenter?nav=command-center',runScripts:'outside-only',pretendToBeVisual:true});
 const w=dom.window,d=w.document;let id=0;const timers=new Map();
 w.matchMedia=q=>({matches:q.includes('max-width')?touch:!touch});
 w.setTimeout=fn=>{timers.set(++id,fn);return id;};w.clearTimeout=id=>timers.delete(id);
 const group=d.querySelector('details'),panel=d.querySelector('.app-nav-panel'),link=panel.querySelector('a'),nav=d.querySelector('nav');
 const pointer=(target,type,pointerType)=>{const e=new w.Event(type,{bubbles:type!=='pointerenter'&&type!=='pointerleave'});Object.defineProperty(e,'pointerType',{value:pointerType});target.dispatchEvent(e);};
 const navigations=[];const current=new URL(w.location.href);
 w.Function('location',source)({href:current.href,pathname:current.pathname,assign(href){navigations.push({href,menuOpen:nav.dataset.menuOpen,flyoutOpen:group.open});}});
 return {dom,w,d,group,panel,link,nav,pointer,navigations,expire(){const entries=[...timers.values()];timers.clear();for(const fn of entries)fn();},timers};
}
test('A destination flyout opens without storage and strips its routing hint',t=>{
 const h=setup();t.after(()=>h.dom.window.close());assert.equal(h.group.open,true);assert.equal(h.nav.dataset.menuOpen,'true');assert.equal(h.w.location.search,'');h.expire();assert.equal(h.group.open,false);assert.equal(h.nav.dataset.menuOpen,'false');
});
test('Sticky touch hover and focus cannot hold a flyout open indefinitely',t=>{
 const h=setup();t.after(()=>h.dom.window.close());
 h.panel.matches=()=>true;h.pointer(h.panel,'pointerenter','touch');h.pointer(h.link,'pointerdown','touch');h.link.focus();
 h.expire();assert.equal(h.group.open,false);assert.equal(h.nav.dataset.menuOpen,'false');
});
test('Real mouse hover holds a desktop flyout until the pointer leaves',t=>{
 const h=setup({touch:false});t.after(()=>h.dom.window.close());h.pointer(h.panel,'pointerenter','mouse');h.expire();assert.equal(h.group.open,true);
 h.pointer(h.panel,'pointerleave','mouse');h.expire();assert.equal(h.group.open,false);
});
test('Keyboard users can work inside a flyout without losing focus',t=>{
 const h=setup();t.after(()=>h.dom.window.close());h.group.querySelector('summary').dispatchEvent(new h.w.KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true,cancelable:true}));
 assert.equal(h.d.activeElement,h.link);h.expire();assert.equal(h.group.open,true);h.d.querySelector('#outside').focus();h.expire();assert.equal(h.group.open,false);
});
test('Switching from keyboard to touch releases the focus hold',t=>{
 const h=setup();t.after(()=>h.dom.window.close());h.group.querySelector('summary').dispatchEvent(new h.w.KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true,cancelable:true}));h.pointer(h.link,'pointerdown','touch');h.expire();assert.equal(h.group.open,false);
});
test('Choosing a direct mobile link dismisses the menu immediately',t=>{
 const h=setup();t.after(()=>h.dom.window.close());const direct=h.d.querySelector('.app-nav-direct');direct.addEventListener('click',e=>e.preventDefault());direct.click();assert.equal(h.nav.dataset.menuOpen,'false');assert.equal(h.group.open,false);
});
test('Back-forward restoration clears a stale open menu',t=>{
 const h=setup();t.after(()=>h.dom.window.close());const e=new h.w.Event('pageshow');Object.defineProperty(e,'persisted',{value:true});h.w.dispatchEvent(e);assert.equal(h.group.open,false);assert.equal(h.nav.dataset.menuOpen,'false');
});

test('Choosing Command Center closes the old menu before requesting the new page',t=>{
 const h=setup();t.after(()=>h.dom.window.close());h.group.querySelector('summary').click();
 assert.equal(h.navigations.length,1);assert.equal(h.navigations[0].menuOpen,'false');assert.equal(h.navigations[0].flyoutOpen,false);
 assert.equal(new URL(h.navigations[0].href).pathname,'/CommandCenter');assert.equal(new URL(h.navigations[0].href).searchParams.get('nav'),'command-center');
});
