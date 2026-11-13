(() => {
 const form=document.querySelector('[data-simple-assignment]');if(!form?.dataset.exerciseSearchUrl)return;
 const states=new WeakMap();let sequence=0;
 const key=value=>value.toLowerCase().replace(/[^a-z0-9]/g,'');
 const isExercise=input=>input instanceof HTMLInputElement&&input.matches('input[name^="Exercises["][name$=".Name"]');
 function stateFor(input){
  let state=states.get(input);if(state)return state;
  const label=input.closest('label');label.querySelectorAll('datalist,[data-snap-exercise-options],[data-snap-exercise-status]').forEach(n=>n.remove());
  input.removeAttribute('list');input.setAttribute('autocomplete','off');
  const list=document.createElement('div');list.id=`snap-exercises-${++sequence}`;list.className='snap-exercise-options';list.dataset.snapExerciseOptions='';list.hidden=true;
  list.setAttribute('role','group');list.setAttribute('aria-label','Exercise library matches');
  const status=document.createElement('span');status.className='snap-exercise-lookup-status';status.dataset.snapExerciseStatus='';status.setAttribute('role','status');status.id=`list-status-${sequence}`;
  input.setAttribute('aria-controls',list.id);input.setAttribute('aria-expanded','false');input.setAttribute('aria-describedby',status.id);label.append(list,status);
  state={list,status,items:[],version:0,timer:null,controller:null,autoFocus:null};states.set(input,state);return state;
 }
 function close(input,state){state.list.hidden=true;input.setAttribute('aria-expanded','false');}
 function fillMuscles(input,state,selected=null){
  const normal=key(input.value.trim());const match=selected||state.items.find(x=>key(x.name)===normal)||state.items.find(x=>key(x.name).replace(/s$/,'')===normal.replace(/s$/,''));
  if(!match)return false;
  const focus=input.closest('[data-exercise]').querySelector('input[name$=".MuscleFocus"]');
  // Background lookups preserve muscle edits made by the coach.
  if(focus&&(selected||!focus.value.trim()||focus.value===state.autoFocus)){focus.value=match.muscles||'';state.autoFocus=focus.value;focus.dispatchEvent(new Event('input',{bubbles:true}));}
  state.status.textContent=match.muscles?`Library match: ${match.name}. Muscles: ${match.muscles}. You can edit the muscles.`:`Library match: ${match.name}. No muscles listed; you can enter them manually.`;
  return true;
 }
 function search(input){
  const state=stateFor(input);clearTimeout(state.timer);state.controller?.abort();const version=++state.version,query=input.value.trim();
  if(fillMuscles(input,state)){close(input,state);return;}
  const focus=input.closest('[data-exercise]').querySelector('input[name$=".MuscleFocus"]');
  if(state.autoFocus!==null&&focus?.value===state.autoFocus){focus.value='';state.autoFocus=null;focus.dispatchEvent(new Event('input',{bubbles:true}));}
  state.list.replaceChildren();state.items=[];close(input,state);
  if(query.length<2){state.status.textContent='Type at least two characters to search, or use your own exercise name.';return;}
  state.status.textContent='Searching exercise library…';
  state.timer=setTimeout(async()=>{
   state.controller=new AbortController();
   try{
    const url=new URL(form.dataset.exerciseSearchUrl,window.location.origin);url.searchParams.set('q',query);
    const response=await fetch(url,{signal:state.controller.signal,headers:{Accept:'application/json'}});if(!response.ok)throw new Error('Lookup unavailable');
    const items=await response.json();if(version!==state.version||!input.isConnected)return;if(!Array.isArray(items))throw new Error('Invalid lookup response');
    state.items=items.filter(item=>typeof item.name==='string'&&typeof item.muscles==='string');
    for(const item of state.items){
     const button=document.createElement('button');button.type='button';button.textContent=`${item.name} · ${item.muscles||'Muscles not listed'}`;
     button.addEventListener('click',event=>{
      event.preventDefault();clearTimeout(state.timer);state.controller?.abort();state.version++;
      input.value=item.name;fillMuscles(input,state,item);close(input,state);
      input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));input.focus();
     });state.list.append(button);
    }
    if(!fillMuscles(input,state))state.status.textContent=state.items.length?'Choose a library match to fill muscles, or keep your typed name and enter muscles manually.':'No library match. Your typed name is accepted; muscles are optional.';
    state.list.hidden=state.items.length===0;input.setAttribute('aria-expanded',String(!state.list.hidden));
   }catch(error){if(error.name!=='AbortError'&&version===state.version){close(input,state);state.status.textContent='Lookup unavailable. Your typed name is accepted; you can enter muscles manually.';}}
  },250);
 }
 form.addEventListener('focusin',event=>{if(isExercise(event.target)&&!states.has(event.target))search(event.target);});
 form.addEventListener('input',event=>{if(isExercise(event.target))search(event.target);});
 form.addEventListener('change',event=>{if(isExercise(event.target))fillMuscles(event.target,stateFor(event.target));});
 form.addEventListener('keydown',event=>{
  if(!isExercise(event.target))return;const state=stateFor(event.target);
  if(event.key==='Escape')close(event.target,state);
  if(event.key==='ArrowDown'&&state.items.length){event.preventDefault();state.list.hidden=false;event.target.setAttribute('aria-expanded','true');state.list.querySelector('button')?.focus();}
 });
 document.addEventListener('pointerdown',event=>{form.querySelectorAll('input[name^="Exercises["][name$=".Name"]').forEach(input=>{const state=states.get(input);if(state&&!input.closest('label').contains(event.target))close(input,state);});});
 form.querySelectorAll('input[name^="Exercises["][name$=".Name"]').forEach(input=>{if(input.value.trim())search(input);});
})();
