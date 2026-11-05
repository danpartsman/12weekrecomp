(()=>{
    const dialog=document.querySelector('#nutrition-scanner'),picker=document.querySelector('#nutrition-food-picker-dialog');
    if(!dialog||!picker)return;
    const photo=dialog.querySelector('[data-scan-photo]'),code=dialog.querySelector('[data-scan-code]'),lookup=dialog.querySelector('[data-scan-lookup]'),status=dialog.querySelector('[data-scan-status]');
    let mode='barcode',version=0,worker=null,controller=null;const loaders=new Map();
    function load(src){if(loaders.has(src))return loaders.get(src);const promise=new Promise((resolve,reject)=>{const tag=document.createElement('script');tag.src=src;tag.onload=resolve;tag.onerror=()=>{loaders.delete(src);tag.remove();reject(new Error('Could not download the scanning tools. Check your connection and try again.'));};document.head.append(tag);});loaders.set(src,promise);return promise;}
    function message(text){status.textContent=text;}
    function stop(){version++;controller?.abort();controller=null;if(worker){worker.terminate();worker=null;}photo.disabled=false;lookup.disabled=false;}
    dialog.addEventListener('close',stop);
    dialog.querySelector('[data-scan-close]').addEventListener('click',()=>dialog.close());
    document.querySelectorAll('[data-food-scan]').forEach(button=>button.addEventListener('click',()=>{
        stop();mode=button.dataset.foodScan;photo.value='';code.value='';message('');
        dialog.querySelector('[data-scan-title]').textContent=mode==='label'?'Scan nutrition label':'Scan barcode';
        dialog.querySelector('[data-scan-instructions]').textContent=mode==='label'?'Photograph only the nutrition facts panel, straight on and in good light. You will review the serving size and macros before adding.':'Photograph the barcode clearly, or type its printed number. We will only select an exact USDA product match.';
        dialog.querySelector('[data-scan-barcode-entry]').hidden=mode!=='barcode';dialog.showModal();
    }));
    async function findBarcode(value){
        const run=++version;controller?.abort();controller=new AbortController();lookup.disabled=true;message('Looking up the exact product…');
        const timeout=setTimeout(()=>controller?.abort(),25000);
        try{
            const url=new URL(dialog.dataset.barcodeUrl,location.origin);url.searchParams.set('code',value);
            const response=await fetch(url,{signal:controller.signal,headers:{Accept:'application/json'}});
            const data=await response.json();if(run!==version||!dialog.open)return;
            if(!response.ok)throw new Error(data.message||'Barcode lookup failed. Try scanning the nutrition label.');
            if(data.scanSource==='OPEN_FOOD_FACTS'){reviewLabel('',data);}else{dialog.close();picker.dispatchEvent(new CustomEvent('nutrition-barcode-selected',{detail:data}));}
        }catch(error){if(run===version&&dialog.open)message(error.name==='AbortError'?'USDA lookup timed out. Try again or scan the nutrition label.':error.message);}
        finally{clearTimeout(timeout);if(run===version)lookup.disabled=false;}
    }
    lookup.addEventListener('click',()=>findBarcode(code.value.trim()));
    code.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();findBarcode(code.value.trim());}});
    function reviewLabel(text,product=null){
        const values=product||window.NutritionLabelParser.parse(text),custom=document.querySelector('#nutrition-custom-food-dialog'),form=custom.querySelector('[data-custom-food-form]');
        form.reset();form.elements.foodName.value=product?.description||'';form.elements.brandName.value=product?.brandName||'';form.elements.scanSource.value=product?.scanSource||'';form.elements.scanBarcode.value=product?.scanBarcode||'';form.elements.mealSlotCode.value=picker.querySelector('[data-picker-meal]').value;
        for(const name of ['energyKcal','proteinG','carbohydrateG','fatG','fiberG','servingSize','servingUnitCode'])form.elements[name].value=values[name]??'';
        form.querySelector('.nutrition-scan-review')?.remove();
        const review=document.createElement('div');review.className='nutrition-scan-review';
        const note=document.createElement('p');note.textContent=`Check every value against your label. ${values.servingText?'Read serving: '+values.servingText+'. ':''}All macros must use the same serving basis. Missing values are left blank; enter the food name and brand.`;
        const details=document.createElement('details'),summary=document.createElement('summary'),raw=document.createElement('pre');summary.textContent='Recognized label text';raw.textContent=text;details.append(summary,raw);
        const label=document.createElement('label'),confirm=document.createElement('input');confirm.type='checkbox';confirm.required=true;label.append(confirm,document.createTextNode('I checked the serving size and nutrition values against the label.'));
        if(product){note.textContent='Open Food Facts values are per 100 g. Verify the product and nutrition basis against the package. Enter any missing values before saving.';const source=document.createElement('a');source.href=product.sourceUrl;source.target='_blank';source.rel='noopener';source.textContent='Source: Open Food Facts (ODbL)';review.append(source);}review.append(note);if(text)review.append(details);review.append(label);form.querySelector('.nutrition-custom-food-grid').prepend(review);
        dialog.close();picker.close();custom.showModal();form.elements.foodName.focus();
    }
    photo.addEventListener('change',async()=>{
        const file=photo.files?.[0];if(!file)return;if(file.size>20000000){message('Choose an image smaller than 20 MB.');return;}
        const run=++version;photo.disabled=true;message(mode==='label'?'Reading the label on your device…':'Reading barcode…');let objectUrl;
        try{
            if(mode==='barcode'){
                objectUrl=URL.createObjectURL(file);
                await load('https://cdn.jsdelivr.net/npm/@zxing/browser@0.1.5/umd/zxing-browser.min.js');
                if(run!==version||!dialog.open)return;
                const result=await new window.ZXingBrowser.BrowserMultiFormatReader().decodeFromImageUrl(objectUrl);
                if(run!==version||!dialog.open)return;
                code.value=result.getText();await findBarcode(code.value);
            }else{
                await load('https://cdn.jsdelivr.net/npm/tesseract.js@6.0.1/dist/tesseract.min.js');
                if(run!==version||!dialog.open)return;
                const localWorker=await window.Tesseract.createWorker('eng',1,{workerPath:'https://cdn.jsdelivr.net/npm/tesseract.js@6.0.1/dist/worker.min.js',logger:event=>{if(run===version&&event.status==='recognizing text')message(`Reading label… ${Math.round(event.progress*100)}%`);}});
                if(run!==version||!dialog.open){await localWorker.terminate();return;}
                worker=localWorker;const result=await localWorker.recognize(file);await localWorker.terminate();worker=null;
                if(run!==version||!dialog.open)return;
                if(!result.data.text?.trim())throw new Error('No readable text found. Retake a clear, close photo of the nutrition facts panel.');
                reviewLabel(result.data.text);
            }
        }catch(error){if(dialog.open&&run===version)message(mode==='barcode'?'Could not read that barcode. Retake a close, sharp photo or type the number below.':error.message||'Could not read the label. Try a clearer photo.');}
        finally{if(objectUrl)URL.revokeObjectURL(objectUrl);if(worker){await worker.terminate();worker=null;}photo.disabled=false;photo.value='';}
    });
})();
