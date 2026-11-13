const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');

class Element{
 constructor(){this.dataset={};this.value='';this.textContent='';this.children=[];this.listeners={};this.disabled=false;this.open=false;}
 addEventListener(name,fn){(this.listeners[name]??=[]).push(fn);}
 async emit(name,event={}){await Promise.all((this.listeners[name]||[]).map(fn=>fn(event)));}
 append(...children){this.children.push(...children);}
 replaceChildren(){this.children=[];}
 checkValidity(){return Number(this.value)>0&&Number(this.value)<=168;}
 showModal(){this.open=true;}
 close(){this.open=false;}
}
async function harness({conflict=true,canOverride=true,fail=false,reason='Work'}={}){
 const form=new Element(),dialog=new Element(),output=new Element();output.dataset.timeZone='UTC';
 const date=new Element(),hours=new Element(),override=new Element(),choice=new Element();
 date.value='2026-11-12';hours.value='2';override.value='false';
 form.elements={Date:date,TimeAllowed:hours,OverrideSchedule:override,ConflictChoice:choice};
 form.dataset.timingUrl='/timing';
 const nodes={};
 for(const key of ['conflict-list','conflict-extension','conflict-status','conflict-hours','conflict-extend','conflict-override','conflict-cancel'])nodes[key]=new Element();
 dialog.querySelector=selector=>nodes[selector.slice(6,-1)];
 form.querySelector=selector=>selector==='[data-required-completion]'?output:null;
 let submitted=0,timeout,requests=[];
 function dispatch(submitter={}){
  const event={submitter,prevented:false,preventDefault(){this.prevented=true;}};
  const pending=(form.listeners.submit||[]).map(fn=>fn(event));
  if(!event.prevented)submitted++;
  return Promise.all(pending);
 }
 form.requestSubmit=submitter=>dispatch(submitter);
 const data=()=>({deadlineUtc:'2026-11-12T20:00:00Z',days:[],conflict:{hasConflict:conflict,canOverride,
  requestedDeadlineUtc:'2026-11-12T12:00:00Z',extendedDeadlineUtc:'2026-11-12T20:00:00Z',overrideDeadlineUtc:'2026-11-12T12:00:00Z',extendedHours:10,
  conflicts:conflict?[{reason,start:'2026-11-12T10:00:00Z',end:'2026-11-12T18:00:00Z'}]:[]}});
 const context={document:{querySelectorAll:()=>[],querySelector:s=>s==='[data-simple-assignment]'?form:dialog,createElement:()=>new Element()},location:{origin:'https://12wr.test'},URL,Intl,Date,Math,Number,
  fetch:async url=>{requests.push(String(url));if(fail)throw Error('offline');return {ok:true,json:async()=>data()};},
  setTimeout:fn=>{timeout=fn;return 1;},clearTimeout:()=>{timeout=null;},setInterval:()=>1};
 vm.runInNewContext(fs.readFileSync('src/WebRecomp.Web/wwwroot/js/snap-assignment-time.js','utf8'),context);
 await new Promise(setImmediate);
 return {form,dialog,nodes,hours,override,choice,requests,submit:dispatch,count:()=>submitted,flush:async()=>{if(timeout){const fn=timeout;timeout=null;fn();await new Promise(setImmediate);}}};
}

test('No conflict assigns directly without opening the popup',async()=>{
 const h=await harness({conflict:false});await h.submit();
 assert.equal(h.count(),1);assert.equal(h.dialog.open,false);assert.equal(h.choice.value,'');
});
test('Conflict blocks assignment; extension keeps available hours and submits once',async()=>{
 const h=await harness();await h.submit();assert.equal(h.count(),0);assert.equal(h.dialog.open,true);
 assert.match(h.nodes['conflict-extension'].textContent,/10 total hours.*2 available workout hours/);
 await h.nodes['conflict-extend'].emit('click');
 assert.equal(h.count(),1);assert.equal(h.dialog.open,false);assert.equal(h.choice.value,'extend');assert.equal(h.override.value,'false');assert.equal(h.hours.value,'2');
});
test('Override is offered only inside the conflict popup',async()=>{
 const h=await harness();await h.submit();await h.nodes['conflict-override'].emit('click');
 assert.equal(h.count(),1);assert.equal(h.choice.value,'override');assert.equal(h.override.value,'true');
});
test('Cancel and Escape leave a conflicting workout unassigned',async()=>{
 const h=await harness();await h.submit();await h.nodes['conflict-cancel'].emit('click');
 assert.equal(h.count(),0);assert.equal(h.dialog.open,false);assert.equal(h.choice.value,'');
 await h.submit();await h.dialog.emit('cancel');assert.equal(h.count(),0);assert.equal(h.choice.value,'');
});
test('Changing hours inside the popup recalculates before a decision',async()=>{
 const h=await harness();await h.submit();h.nodes['conflict-hours'].value='3';await h.nodes['conflict-hours'].emit('input');
 assert.equal(h.hours.value,'3');assert.equal(h.nodes['conflict-extend'].disabled,true);
 await h.flush();assert.match(h.requests.at(-1),/minutes=180/);assert.equal(h.nodes['conflict-extend'].disabled,false);
 await h.nodes['conflict-extend'].emit('click');assert.equal(h.count(),1);
});
test('Illness and pain conflicts both offer override',async()=>{
 for(const reason of ['Illness','Pain']){
  const h=await harness({reason});await h.submit();assert.equal(h.nodes['conflict-override'].disabled,false);
  assert.match(h.nodes['conflict-list'].children[0].textContent,new RegExp(reason));
  await h.nodes['conflict-override'].emit('click');assert.equal(h.count(),1);assert.equal(h.choice.value,'override');
 }
});
test('A failed schedule check cannot assign',async()=>{
 const offline=await harness({fail:true});await offline.submit();assert.equal(offline.count(),0);assert.equal(offline.nodes['conflict-extend'].disabled,true);
});
