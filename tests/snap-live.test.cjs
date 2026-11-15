const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require(process.env.SNAP_JSDOM_MODULE||'jsdom');
const source=fs.readFileSync('src/WebRecomp.Web/wwwroot/js/snap-live.js','utf8');
const flush=async()=>{for(let i=0;i<12;i++)await new Promise(setImmediate);};
function setup({coach=false,finished=false}={}){
 const dom=new JSDOM(`<details open><section data-snap-live data-live-url="/live" data-coach="${coach}">
 <span data-live-status></span><span data-live-saved></span><span data-live-lifecycle></span><span data-live-count></span>
 <button data-live-start>Start</button><button data-live-correct>Correct</button>
 <form data-snap-results data-finished="${finished}"><input name="Revision" value="1"><input name="ProgressRevision" value="0"><input name="AssignmentToken" value="token">
 <section data-live-exercise="e1"><table><tbody><tr><td><input type="number" name="Exercises[0].Sets[0].Reps"><input type="number" name="Exercises[0].Sets[0].Weight" value="0"></td></tr></tbody></table></section>
 <button type="submit" value="progress">Save</button><button type="submit" value="finish">Finish</button></form>
 <div data-chat-posts></div><button data-chat-earlier hidden>Earlier</button><span data-chat-status></span>
 <form data-snap-chat><input name="__RequestVerificationToken" value="csrf"><input name="RequestId" value="initial"><textarea name="Body" required></textarea><button type="submit">Send</button></form>
 </section></details>`,{url:'https://example.com',runScripts:'outside-only',pretendToBeVisual:true});
 const w=dom.window,d=w.document;w.CSS={escape:s=>s};w.setInterval=()=>0;
 let timeouts=[];w.setTimeout=fn=>{timeouts.push(fn);return timeouts.length;};w.clearTimeout=()=>{};
 let state={revision:1,progressRevision:0,assignmentToken:'token',startedUtc:null,savedUtc:null,finished,sets:{},messages:[],hasEarlierMessages:false};
 const calls=[];let fail=null,nativeFinishes=0;
 w.fetch=async(url,options)=>{calls.push({url,body:options.body});if(fail){const error=fail;fail=null;if(error instanceof Error)throw error;return {ok:false,status:error,headers:{get:()=>null},json:async()=>({message:'Reload to resolve conflict'})};}
 if(url.endsWith('/start'))state.startedUtc=new Date().toISOString();
 if(url.endsWith('/progress')){state.progressRevision++;state.savedUtc=new Date().toISOString();state.sets={e1:[{reps:Number(options.body.get('Exercises[0].Sets[0].Reps')),weight:Number(options.body.get('Exercises[0].Sets[0].Weight'))}]};}
 if(url.endsWith('/message'))state.messages.push({id:options.body.get('RequestId'),sequence:state.messages.length+1,body:options.body.get('Body'),authorName:'Client',coach:false,createdUtc:new Date().toISOString()});
 return {ok:true,status:200,headers:{get:()=>null},json:async()=>structuredClone(state)};};
 const form=d.querySelector('[data-snap-results]');if(coach)form.querySelectorAll('input[type=number]').forEach(f=>f.disabled=true);form.requestSubmit=()=>nativeFinishes++;
 w.eval(source);
 const input=d.querySelector('input[type=number]');
 return {dom,w,d,form,input,calls,get state(){return state;},set state(v){state=v;},fail(v){fail=v;},get finishes(){return nativeFinishes;},async start(){d.querySelector('[data-live-start]').click();await flush();},async edit(value){input.value=value;input.dispatchEvent(new w.Event('input',{bubbles:true}));await flush();},async save(){form.dispatchEvent(new w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:form.querySelector('[value=progress]')}));await flush();},async finish(){form.dispatchEvent(new w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:form.querySelector('[value=finish]')}));await flush();},async poll(){w.dispatchEvent(new w.Event('focus'));await flush();},async timers(){const tasks=timeouts;timeouts=[];for(const fn of tasks)fn();await flush();}};
}
test('Start is explicit, draft autosaves, and only Finish submits canonical results',async t=>{
 const h=setup();t.after(()=>h.dom.window.close());await flush();assert.equal(h.input.disabled,true);assert.equal(h.calls.filter(c=>c.url.endsWith('/start')).length,0);
 await h.start();assert.equal(h.input.disabled,false);await h.edit('12');await h.timers();assert.equal(h.state.sets.e1[0].reps,12);assert.equal(h.finishes,0);
 await h.edit('14');await h.finish();assert.equal(h.state.sets.e1[0].reps,14);assert.equal(h.finishes,1);assert.equal(h.form.querySelector('[name=ProgressRevision]').value,'2');
});
test('Offline save retains entries and retry saves them',async t=>{
 const h=setup();t.after(()=>h.dom.window.close());await flush();await h.start();await h.edit('9');h.fail(new Error('offline'));await h.save();assert.equal(h.input.value,'9');assert.match(h.d.querySelector('[data-live-status]').textContent,/not been confirmed saved/);
 await h.poll();assert.equal(h.input.value,'9');await h.save();assert.equal(h.state.sets.e1[0].reps,9);
});
test('Conflict preserves input, blocks overwrite, and blocks Finish',async t=>{
 const h=setup();t.after(()=>h.dom.window.close());await flush();await h.start();await h.edit('17');h.fail(409);await h.save();await h.poll();assert.equal(h.input.value,'17');const count=h.calls.length;await h.save();assert.equal(h.calls.length,count);await h.finish();assert.equal(h.finishes,0);
});
test('Coach view polls completed reps and weights without posting results',async t=>{
 const h=setup({coach:true});t.after(()=>h.dom.window.close());await flush();h.state.startedUtc=new Date().toISOString();h.state.sets={e1:[{reps:11,weight:25}]};await h.poll();assert.equal(h.input.value,'11');assert.equal(h.input.disabled,true);assert.equal(h.form.querySelector('[name$=".Weight"]').value,'25');assert.equal(h.calls.some(c=>c.body),false);
});
test('Chat is text-safe, deduplicated on polling, and remains usable after Finish',async t=>{
 const h=setup({finished:true});t.after(()=>h.dom.window.close());await flush();const textarea=h.d.querySelector('textarea');textarea.value='<img src=x onerror=alert(1)> Keep going';h.d.querySelector('[data-snap-chat]').dispatchEvent(new h.w.Event('submit',{bubbles:true,cancelable:true}));await flush();await h.poll();await h.poll();assert.equal(h.d.querySelectorAll('[data-message-id]').length,1);assert.equal(h.d.querySelector('[data-chat-posts] img'),null);assert.match(h.d.querySelector('[data-chat-posts]').textContent,/<img/);assert.equal(textarea.value,'');
});
test('Prescription change blocks autosave without erasing entries',async t=>{
 const h=setup();t.after(()=>h.dom.window.close());await flush();await h.start();await h.edit('19');h.state.assignmentToken='changed';await h.poll();assert.match(h.d.querySelector('[data-live-status]').textContent,/coach changed/);const count=h.calls.length;await h.save();assert.equal(h.calls.length,count);assert.equal(h.input.value,'19');
});
test('Submitted corrections require explicit submission and never autosave',async t=>{
 const h=setup({finished:true});t.after(()=>h.dom.window.close());await flush();assert.equal(h.input.disabled,true);h.d.querySelector('[data-live-correct]').click();assert.equal(h.input.disabled,false);await h.edit('7');await h.timers();assert.equal(h.calls.filter(c=>c.url.endsWith('/progress')).length,0);const event=new h.w.SubmitEvent('submit',{bubbles:true,cancelable:true,submitter:h.form.querySelector('[value=finish]')});h.form.dispatchEvent(event);assert.equal(event.defaultPrevented,false);
});

test('A delayed refresh cannot replace a newer saved draft',async t=>{
 const h=setup();t.after(()=>h.dom.window.close());await flush();await h.start();
 const normal=h.w.fetch;let resolve;const old=structuredClone(h.state);
 h.w.fetch=async(url,options)=>{if(!options.body)return new Promise(r=>resolve=r);return normal(url,options);};
 const pending=h.poll();await flush();await h.edit('23');await h.save();
 resolve({ok:true,status:200,headers:{get:()=>null},json:async()=>old});await pending;
 assert.equal(h.input.value,'23');assert.equal(h.form.querySelector('[name=ProgressRevision]').value,'1');
});
