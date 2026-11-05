(() => {
    const form=document.querySelector('[data-simple-assignment]');
    if(!form?.dataset.exerciseSearchUrl)return;
    const states=new WeakMap();let sequence=0;
    function stateFor(input){
        let state=states.get(input);if(state)return state;
        const label=input.closest('label');label.querySelectorAll('datalist').forEach(n=>n.remove());
        const list=document.createElement('datalist');list.id=`snap-exercises-${++sequence}`;
        input.setAttribute('list',list.id);input.setAttribute('autocomplete','off');label.append(list);
        state={list,items:[],version:0,timer:null,controller:null};states.set(input,state);return state;
    }
    function fillMuscles(input,state){
        const match=state.items.find(x=>x.name.toLowerCase()===input.value.trim().toLowerCase());
        if(!match)return false;
        const focus=input.closest('[data-exercise]').querySelector('input[name$=".MuscleFocus"]');
        if(focus){focus.value=match.muscles||'';focus.dispatchEvent(new Event('input',{bubbles:true}));}
        return true;
    }
    function isExercise(input){return input.matches('input[name^="Exercises["][name$=".Name"]');}
    form.addEventListener('focusin',event=>{if(isExercise(event.target))stateFor(event.target);});
    form.addEventListener('input',event=>{
        const input=event.target;if(!isExercise(input))return;
        const state=stateFor(input);clearTimeout(state.timer);state.controller?.abort();
        const version=++state.version,query=input.value.trim();
        if(fillMuscles(input,state))return;
        state.list.replaceChildren();state.items=[];
        if(query.length<2)return;
        state.timer=setTimeout(async()=>{
            state.controller=new AbortController();
            try{
                const url=new URL(form.dataset.exerciseSearchUrl,window.location.origin);url.searchParams.set('q',query);
                const response=await fetch(url,{signal:state.controller.signal,headers:{Accept:'application/json'}});
                if(!response.ok)return;
                const items=await response.json();
                if(version!==state.version||!input.isConnected||!Array.isArray(items))return;
                state.items=items;
                state.list.replaceChildren(...items.map(item=>{const option=document.createElement('option');option.value=item.name;option.label=item.muscles;return option;}));
                fillMuscles(input,state);
            }catch(error){/* Free text remains usable when lookup is unavailable. */}
        },250);
    });
    form.addEventListener('change',event=>{if(isExercise(event.target))fillMuscles(event.target,stateFor(event.target));});
})();
