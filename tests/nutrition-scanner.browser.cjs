const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:667}});
  const scanner=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/_FoodScanner.cshtml','utf8').replace(/<link[^>]*>|<script[\s\S]*?<\/script>/g,'').replace(/data-barcode-url="[\s\S]*?">/,'data-barcode-url="/nutrition/scan/barcode">');
  const fields=['foodName','brandName','energyKcal','proteinG','carbohydrateG','fatG','fiberG','servingSize','scanSource','scanBarcode'].map(name=>`<label>${name}<input name="${name}"></label>`).join('');
  const css=['nutrition-intelligence.css','nutrition-scanner.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
  const fixture=`<html><head><style>${css}dialog{max-width:94vw;box-sizing:border-box}input{max-width:100%;box-sizing:border-box}</style></head><body><dialog id="nutrition-food-picker-dialog" class="nutrition-picker-dialog"><div class="nutrition-picker-scroll"><section class="nutrition-picker-results-section nutrition-picker-food-actions"><div class="nutrition-picker-results-heading"><div><strong>Foods</strong><span class="small">Closest matches across your foods and the food library</span></div><div class="nutrition-picker-action-links"><button class="nutrition-picker-custom-link" data-food-scan="barcode">Scan barcode</button><button class="nutrition-picker-custom-link" data-food-scan="label">Scan label</button><button class="nutrition-picker-custom-link">+ Add custom food</button><button class="nutrition-picker-custom-link">+ Add a previous meal</button><button class="nutrition-picker-custom-link">+ Add a prep meal</button></div></div></section></div><select data-picker-meal><option value="LUNCH">Lunch</option></select></dialog>${scanner}<dialog id="nutrition-custom-food-dialog"><form data-custom-food-form><div class="nutrition-custom-food-grid">${fields}<select name="servingUnitCode"><option>serving</option><option>g</option><option>ml</option></select><select name="mealSlotCode"><option>LUNCH</option></select></div></form></dialog></body></html>`;
  let barcodeRequests=0,barcodeResponse=null;
  await page.route('https://scanner.test/**',route=>{
   if(new URL(route.request().url()).pathname.includes('barcode')){barcodeRequests++;return route.fulfill({status:barcodeResponse?200:404,contentType:'application/json',body:JSON.stringify(barcodeResponse||{message:'No exact product was found.'})});}
   return route.fulfill({contentType:'text/html',body:fixture});
  });
  await page.route('https://cdn.jsdelivr.net/**',route=>route.fulfill({contentType:'application/javascript',body:''}));await page.goto('https://scanner.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-label-parser.js'});await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-scanner.js'});
  await page.evaluate(()=>{
   document.querySelector('#nutrition-food-picker-dialog').showModal();window.cameraRequests=0;window.previews=[];window.stopped=0;window.scanSubmits=0;window.cameraDenied=false;
   Object.defineProperty(navigator.mediaDevices,'getUserMedia',{configurable:true,value:async constraints=>{window.cameraRequests++;window.cameraConstraints=constraints;if(window.cameraDenied)throw Object.assign(new Error('Denied'),{name:'NotAllowedError'});return{getTracks:()=>[{stop:()=>window.stopped++}]};}});
   window.ZXingBrowser={BrowserMultiFormatReader:class{async decodeFromStream(stream,video,callback){window.previews.push(video);window.scanCallback=callback;return{stop(){}};}}};
   window.Tesseract={createWorker:async()=>({recognize:async()=>({data:{text:'Serving size 1 scoop (33 g)\nCalories 130\nTotal Fat 2.5g\nTotal Carbohydrate 3g\nProtein 25g'}}),terminate:async()=>{}})};
   document.querySelector('[data-custom-food-form]').addEventListener('submit',event=>{event.preventDefault();window.scanSubmits++;});
  });
  const openBarcode=async()=>{await page.evaluate(()=>{window.scanCallback=null;});await page.getByRole('button',{name:'Scan barcode',exact:true}).click();};
  const detect=async()=>{await page.waitForFunction(()=>window.scanCallback);await page.evaluate(()=>{window.scanCallback({getText:()=> '012345678905'},null,{stop(){}});window.scanCallback({getText:()=> '012345678905'},null,{stop(){}});});};
  await openBarcode();await page.locator('[data-scan-barcode-entry] summary').click();await page.locator('[data-scan-code]').fill('012345678905');await page.getByRole('button',{name:'Look up barcode'}).click();await page.getByText('No exact product was found.',{exact:true}).waitFor();await page.locator('[data-scan-close]').click();
  barcodeResponse={success:true,scanSource:'OPEN_FOOD_FACTS',scanBarcode:'012345678905',description:'Test food',brandName:'Test brand',hasServing:true,servingSize:30,servingUnitCode:'g',energyKcal:120,proteinG:20,carbohydrateG:5,fatG:2,fiberG:0,sourceUrl:'https://world.openfoodfacts.org/product/012345678905'};
  await openBarcode();const before=barcodeRequests;await detect();await page.locator('[data-scan-result]:not([hidden])').waitFor();
  assert.equal(barcodeRequests-before,1);assert.equal(await page.evaluate(()=>window.scanSubmits),0);assert.equal(await page.locator('[data-scan-food-name]').textContent(),'Test food');
  assert.equal(await page.locator('#nutrition-scanner').evaluate(el=>el.scrollHeight<=el.clientHeight+1),true);assert.ok(await page.evaluate(()=>window.stopped>0));
  await page.locator('[data-scan-close]').click();assert.equal(await page.evaluate(()=>window.scanSubmits),0);
  // Fresh camera request on every reopen, and a delayed old close event cannot stop it.
  const requests=await page.evaluate(()=>window.cameraRequests);await openBarcode();await page.waitForFunction(()=>window.scanCallback);assert.equal(await page.evaluate(()=>window.cameraRequests),requests+1);assert.equal(await page.evaluate(()=>window.previews.length>1&&window.previews.at(-1)!==window.previews.at(-2)),true);
  const stopped=await page.evaluate(()=>window.stopped);await page.evaluate(()=>document.querySelector('#nutrition-scanner').dispatchEvent(new Event('close')));assert.equal(await page.evaluate(()=>window.stopped),stopped);
  await detect();await page.locator('[data-scan-add]:not([disabled])').waitFor();await page.evaluate(()=>{const add=document.querySelector('[data-scan-add]');add.click();add.click();});await page.waitForFunction(()=>window.scanSubmits===1);assert.equal(await page.locator('#nutrition-scanner').evaluate(el=>el.open),false);
  assert.equal(await page.locator('[name="mealSlotCode"]').inputValue(),'LUNCH');assert.equal(await page.locator('[name="servingSize"]').inputValue(),'30');
  await page.evaluate(()=>{document.querySelector('#nutrition-food-picker-dialog').showModal();window.cameraDenied=true;});await openBarcode();await page.getByText(/Camera permission was not granted/).waitFor();const deniedRequests=await page.evaluate(()=>window.cameraRequests);
  await page.evaluate(()=>{window.cameraDenied=false;window.scanCallback=null;});await page.locator('[data-scan-start]').click();await page.waitForFunction(()=>window.scanCallback);assert.equal(await page.evaluate(()=>window.cameraRequests),deniedRequests+1);await page.locator('[data-scan-close]').click();
  await page.getByRole('button',{name:'Scan label',exact:true}).click();await page.locator('[data-scan-photo]').setInputFiles({name:'label.png',mimeType:'image/png',buffer:Buffer.from('test image')});await page.locator('#nutrition-custom-food-dialog[open]').waitFor();assert.equal(await page.locator('[name="proteinG"]').inputValue(),'25');assert.equal(await page.locator('[name="servingSize"]').inputValue(),'33');assert.equal(await page.locator('.nutrition-scan-review input').getAttribute('required'),'');
  await page.evaluate(()=>{document.querySelector('#nutrition-custom-food-dialog').close();document.querySelector('#nutrition-food-picker-dialog').showModal();});
  for(const width of [320,390,820]){await page.setViewportSize({width,height:667});assert.equal(await page.locator('.nutrition-picker-scroll').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);}
  console.log('Compact scan review, Add/Close semantics, duplicate detection, fresh camera requests, late-close isolation, permission retry and label review passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
