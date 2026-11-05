(()=>{
    const dialog=document.querySelector('#nutrition-scanner'),picker=document.querySelector('#nutrition-food-picker-dialog');
    if(!dialog||!picker)return;
    const photo=dialog.querySelector('[data-scan-photo]'),code=dialog.querySelector('[data-scan-code]'),lookup=dialog.querySelector('[data-scan-lookup]'),status=dialog.querySelector('[data-scan-status]');
    let video=dialog.querySelector('[data-scan-video]');
    const retry=dialog.querySelector('[data-scan-start]'),add=dialog.querySelector('[data-scan-add]'),resultPanel=dialog.querySelector('[data-scan-result]');
    let mode='barcode',session=0,cameraAttempt=0,requestId=0,worker=null,controller=null,stream=null,cameraControls=null,lookupBusy=false,adding=false,pendingFood=null;
    const loaders=new Map();
    function load(src){if(loaders.has(src))return loaders.get(src);const promise=new Promise((resolve,reject)=>{const tag=document.createElement('script');tag.src=src;tag.onload=resolve;tag.onerror=()=>{loaders.delete(src);tag.remove();reject(new Error('Could not download the scanning tools. Check your connection and try again.'));};document.head.append(tag);});loaders.set(src,promise);return promise;}
    function message(text){status.textContent=text;}
    function stopCamera(){cameraAttempt++;cameraControls?.stop();cameraControls=null;const old=stream;stream=null;old?.getTracks().forEach(track=>track.stop());video.srcObject=null;}
    function cleanup(){session++;requestId++;stopCamera();controller?.abort();controller=null;if(worker){worker.terminate();worker=null;}lookupBusy=false;adding=false;pendingFood=null;photo.disabled=false;lookup.disabled=false;add.disabled=true;}
    function close(){cleanup();dialog.close();}
    // A queued close event from an earlier opening must not stop a fresh session.
    dialog.addEventListener('close',()=>{if(!dialog.open)cleanup();});
    dialog.addEventListener('cancel',cleanup);
    dialog.querySelector('[data-scan-close]').addEventListener('click',close);
    async function startCamera(){
        if(lookupBusy||adding)return;
        stopCamera();const current=session,attempt=cameraAttempt;let detected=false;
        // Old decoder cleanup must never clear a newly attached preview.
        const preview=video.cloneNode(false);video.replaceWith(preview);video=preview;preview.muted=true;preview.playsInline=true;
        pendingFood=null;resultPanel.hidden=true;add.disabled=true;video.hidden=false;retry.hidden=true;dialog.querySelector('[data-scan-detected]').textContent='';
        if(!navigator.mediaDevices?.getUserMedia){message('Live camera is unavailable. Enter the barcode manually.');return;}
        message('Starting camera…');
        let camera;
        try{
            // Request a NEW stream on every open/retry; never cache permission or a stopped stream.
            camera=await navigator.mediaDevices.getUserMedia({audio:false,video:{facingMode:{ideal:'environment'},width:{ideal:1280},height:{ideal:720}}});
            if(current!==session||attempt!==cameraAttempt||!dialog.open||document.hidden){camera.getTracks().forEach(track=>track.stop());return;}
            stream=camera;
            await load('https://cdn.jsdelivr.net/npm/@zxing/browser@0.1.5/umd/zxing-browser.min.js');
            if(current!==session||attempt!==cameraAttempt||!dialog.open||document.hidden){camera.getTracks().forEach(track=>track.stop());return;}
            const reader=new window.ZXingBrowser.BrowserMultiFormatReader();
            const controls=await reader.decodeFromStream(camera,preview,(result,error,controls)=>{
                if(!result||detected||current!==session||attempt!==cameraAttempt||!dialog.open)return;
                const value=result.getText();if(!/^(?:\d{8}|\d{12}|\d{13}|\d{14})$/.test(value))return;
                detected=true;controls.stop();stopCamera();code.value=value;findBarcode(value);
            });
            if(detected||current!==session||attempt!==cameraAttempt||!dialog.open){controls.stop();return;}
            cameraControls=controls;message('Point the camera at the barcode.');
        }catch(error){
            camera?.getTracks().forEach(track=>track.stop());
            if(current===session&&attempt===cameraAttempt&&dialog.open){stopCamera();retry.hidden=false;message(error.name==='NotAllowedError'?'Camera permission was not granted. Tap Retry camera to request access again. If your browser keeps blocking it, enable camera access in its site settings.':error.name==='NotFoundError'?'No camera was found. Enter the barcode manually.':'Could not start the camera. Retry or enter the barcode manually.');}
        }
    }
    retry.addEventListener('click',startCamera);
    window.addEventListener('pagehide',cleanup);
    document.addEventListener('visibilitychange',()=>{if(document.hidden&&dialog.open&&mode==='barcode'){stopCamera();if(!lookupBusy&&!pendingFood){retry.hidden=false;message('Camera paused. Tap Retry camera to continue.');}}});
    document.querySelectorAll('[data-food-scan]').forEach(button=>button.addEventListener('click',()=>{
        cleanup();mode=button.dataset.foodScan;photo.value='';code.value='';message('');resultPanel.hidden=true;dialog.querySelector('[data-scan-detected]').textContent='';
        dialog.querySelector('[data-scan-barcode-entry] details').open=false;
        dialog.querySelector('[data-scan-title]').textContent=mode==='label'?'Scan nutrition label':'Scan barcode';
        dialog.querySelector('[data-scan-instructions]').textContent=mode==='label'?'Photograph the nutrition facts panel, then review the values.':'Scan a barcode, check the food, then tap Add.';
        dialog.querySelector('[data-scan-barcode-entry]').hidden=mode!=='barcode';dialog.querySelector('[data-scan-live]').hidden=mode!=='barcode';dialog.querySelector('[data-scan-photo-field]').hidden=mode!=='label';add.hidden=mode!=='barcode';
        dialog.showModal();if(mode==='barcode')startCamera();
    }));
    function showFood(food){
        pendingFood=food;resultPanel.hidden=false;video.hidden=true;
        dialog.querySelector('[data-scan-food-name]').textContent=food.description||'Food details need completion';
        dialog.querySelector('[data-scan-food-brand]').textContent=food.brandName||'';
        dialog.querySelector('[data-scan-food-serving]').textContent=food.hasServing?`1 serving · ${food.servingSize} ${food.servingUnitCode}`:food.scanSource?'Serving details need review':'1 listed serving';
        dialog.querySelector('[data-scan-food-macros]').textContent=Number.isFinite(food.energyKcal)?`${food.energyKcal} kcal · P ${food.proteinG??'—'} g · C ${food.carbohydrateG??'—'} g · F ${food.fatG??'—'} g`:'';
        add.disabled=false;message('');retry.hidden=false;
    }
    async function findBarcode(value){
        if(lookupBusy||adding)return;
        lookupBusy=true;pendingFood=null;resultPanel.hidden=true;add.disabled=true;stopCamera();const current=session,id=++requestId;
        controller?.abort();const abort=new AbortController();controller=abort;lookup.disabled=true;message('Looking up the product…');dialog.querySelector('[data-scan-detected]').textContent=value;
        const timeout=setTimeout(()=>abort.abort(),25000);
        try{
            const url=new URL(dialog.dataset.barcodeUrl,location.origin);url.searchParams.set('code',value);
            const response=await fetch(url,{signal:abort.signal,headers:{Accept:'application/json'}});const data=await response.json();
            if(current!==session||id!==requestId||!dialog.open)return;
            if(!response.ok)throw new Error(data.message||'Barcode lookup failed. Try food search or scan the label.');
            showFood(data);
        }catch(error){if(current===session&&id===requestId&&dialog.open)message(error.name==='AbortError'?'Lookup timed out. Retry or use food search.':error.message);}
        finally{clearTimeout(timeout);if(current===session&&id===requestId){lookupBusy=false;lookup.disabled=false;retry.hidden=false;}}
    }
    lookup.addEventListener('click',()=>findBarcode(code.value.trim()));
    code.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();findBarcode(code.value.trim());}});
    add.addEventListener('click',()=>{
        if(!pendingFood||adding)return;adding=true;add.disabled=true;lookup.disabled=true;const food=pendingFood;
        if(food.scanSource==='OPEN_FOOD_FACTS'){reviewLabel('',food,true);}else{
            picker.dispatchEvent(new CustomEvent('nutrition-barcode-selected',{detail:{...food,autoAdd:true,onSubmit:close,onError:()=>{if(dialog.open){adding=false;add.disabled=false;lookup.disabled=false;message('Could not add this food. Retry or choose its serving through food search.');}}}}));
        }
    });
    function reviewLabel(text,product=null,addProduct=false){
        const values=product||window.NutritionLabelParser.parse(text),custom=document.querySelector('#nutrition-custom-food-dialog'),form=custom.querySelector('[data-custom-food-form]');
        form.reset();form.elements.foodName.value=product?.description||'';form.elements.brandName.value=product?.brandName||'';form.elements.scanSource.value=product?.scanSource||'';form.elements.scanBarcode.value=product?.scanBarcode||'';form.elements.mealSlotCode.value=picker.querySelector('[data-picker-meal]').value;
        for(const name of ['energyKcal','proteinG','carbohydrateG','fatG','fiberG','servingSize','servingUnitCode'])form.elements[name].value=values[name]??'';
        form.querySelector('.nutrition-scan-review')?.remove();
        const complete=product?.hasServing&&product.description&&['energyKcal','proteinG','carbohydrateG','fatG'].every(name=>Number.isFinite(product[name])&&product[name]>=0);
        if(addProduct&&complete&&form.checkValidity()){close();picker.close();form.requestSubmit();return;}
        const review=document.createElement('div');review.className='nutrition-scan-review';const note=document.createElement('p');note.textContent=`Check every value against your label. ${values.servingText?'Read serving: '+values.servingText+'. ':''}All macros must use the same serving basis. Missing values are left blank; enter the food name and brand.`;
        const details=document.createElement('details'),summary=document.createElement('summary'),raw=document.createElement('pre');summary.textContent='Recognized label text';raw.textContent=text;details.append(summary,raw);
        const label=document.createElement('label'),confirm=document.createElement('input');confirm.type='checkbox';confirm.required=true;label.append(confirm,document.createTextNode('I checked the serving size and nutrition values against the label.'));
        if(product){note.textContent=product.hasServing?'Some product details are missing. Complete them before adding.':'No reliable serving size was listed. Values below are per 100 g; choose your amount before adding.';const source=document.createElement('a');source.href=product.sourceUrl;source.target='_blank';source.rel='noopener';source.textContent='Source: Open Food Facts (ODbL)';review.append(source);}
        review.append(note);if(text)review.append(details);review.append(label);form.querySelector('.nutrition-custom-food-grid').prepend(review);close();picker.close();custom.showModal();form.elements.foodName.focus();
    }
    photo.addEventListener('change',async()=>{
        const file=photo.files?.[0];if(!file)return;if(file.size>20000000){message('Choose an image smaller than 20 MB.');return;}
        const current=session;photo.disabled=true;message('Reading the label on your device…');let localWorker;
        try{
            await load('https://cdn.jsdelivr.net/npm/tesseract.js@6.0.1/dist/tesseract.min.js');if(current!==session||!dialog.open)return;
            localWorker=await window.Tesseract.createWorker('eng',1,{workerPath:'https://cdn.jsdelivr.net/npm/tesseract.js@6.0.1/dist/worker.min.js',logger:event=>{if(current===session&&event.status==='recognizing text')message(`Reading label… ${Math.round(event.progress*100)}%`);}});
            if(current!==session||!dialog.open){await localWorker.terminate();return;}worker=localWorker;const result=await localWorker.recognize(file);await localWorker.terminate();if(worker===localWorker)worker=null;
            if(current!==session||!dialog.open)return;if(!result.data.text?.trim())throw new Error('No readable text found. Retake a clear photo of the nutrition facts panel.');reviewLabel(result.data.text);
        }catch(error){if(current===session&&dialog.open)message(error.message||'Could not read the label. Try a clearer photo.');}
        finally{if(worker===localWorker){await worker?.terminate();worker=null;}if(current===session){photo.disabled=false;photo.value='';}}
    });
})();
