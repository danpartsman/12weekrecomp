const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}});
  const scanner=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/_FoodScanner.cshtml','utf8').replace(/<link[^>]*>|<script[\s\S]*?<\/script>/g,'').replace(/data-barcode-url="[\s\S]*?">/,'data-barcode-url="/nutrition/scan/barcode">');
  const fields=['foodName','brandName','energyKcal','proteinG','carbohydrateG','fatG','fiberG','servingSize','scanSource','scanBarcode'].map(name=>`<label>${name}<input name="${name}"></label>`).join('');
  const fixture=`<html><head><style>${fs.readFileSync('src/WebRecomp.Web/wwwroot/css/nutrition-scanner.css','utf8')}dialog{max-width:94vw;box-sizing:border-box}input{max-width:100%;box-sizing:border-box}</style></head><body><dialog id="nutrition-food-picker-dialog"><button data-food-scan="barcode">Scan barcode</button><button data-food-scan="label">Scan label</button><select data-picker-meal><option value="LUNCH">Lunch</option></select></dialog>${scanner}<dialog id="nutrition-custom-food-dialog"><form data-custom-food-form><div class="nutrition-custom-food-grid">${fields}<select name="servingUnitCode"><option>serving</option><option>g</option><option>ml</option></select><select name="mealSlotCode"><option>LUNCH</option></select></div></form></dialog></body></html>`;
  let barcodeRequests=0,barcodeResponse=null;
  await page.route('https://scanner.test/**',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname.includes('barcode')){barcodeRequests++;if(barcodeResponse)return route.fulfill({contentType:'application/json',body:JSON.stringify(barcodeResponse)});return route.fulfill({status:404,contentType:'application/json',body:JSON.stringify({message:'No exact product was found. Scan the nutrition label.'})});}
   return route.fulfill({contentType:'text/html',body:fixture});
  });
  await page.route('https://cdn.jsdelivr.net/**',route=>route.fulfill({contentType:'application/javascript',body:''}));
  await page.goto('https://scanner.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-label-parser.js'});await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-scanner.js'});
  await page.evaluate(()=>{document.querySelector('#nutrition-food-picker-dialog').showModal();window.Tesseract={createWorker:async()=>({recognize:async()=>({data:{text:'Serving size 1 scoop (33 g)\nCalories 130\nTotal Fat 2.5g\nTotal Carbohydrate 3g\nProtein 25g'}}),terminate:async()=>{}})};});
  await page.evaluate(()=>{
    window.stopped=0;window.cameraDenied=false;window.scanSubmits=0;
    Object.defineProperty(navigator.mediaDevices,'getUserMedia',{configurable:true,value:async constraints=>{window.cameraConstraints=constraints;if(window.cameraDenied)throw Object.assign(new Error('Denied'),{name:'NotAllowedError'});return{getTracks:()=>[{stop:()=>window.stopped++}]};}});
    window.ZXingBrowser={BrowserMultiFormatReader:class{async decodeFromStream(stream,video,callback){window.scanCallback=callback;return{stop(){}};}}};
    document.querySelector('[data-custom-food-form]').addEventListener('submit',event=>{event.preventDefault();window.scanSubmits++;});
  });
  await page.getByRole('button',{name:'Scan barcode',exact:true}).click();await page.locator('[data-scan-code]').fill('012345678905');await page.getByRole('button',{name:'Look up barcode'}).click();await page.getByText('No exact product was found. Scan the nutrition label.',{exact:true}).waitFor();
  await page.locator('[data-scan-close]').click();
  await page.evaluate(()=>{window.scanCallback=null;});
  barcodeResponse={success:true,scanSource:'OPEN_FOOD_FACTS',scanBarcode:'012345678905',description:'Test food',brandName:'Test brand',hasServing:true,servingSize:30,servingUnitCode:'g',energyKcal:120,proteinG:20,carbohydrateG:5,fatG:2,fiberG:0,sourceUrl:'https://world.openfoodfacts.org/product/012345678905'};
  await page.getByRole('button',{name:'Scan barcode',exact:true}).click();
  await page.waitForFunction(()=>window.scanCallback&&window.cameraConstraints);
  assert.equal(await page.evaluate(()=>window.cameraConstraints.video.facingMode.ideal),'environment');
  const before=barcodeRequests;
  await page.evaluate(()=>{window.scanCallback({getText:()=> '012345678905'},null,{stop(){}});window.scanCallback({getText:()=> '012345678905'},null,{stop(){}});});
  await page.waitForFunction(()=>window.scanSubmits===1);
  assert.equal(barcodeRequests-before,1);assert.equal(await page.evaluate(()=>window.scanSubmits),1);
  assert.ok(await page.evaluate(()=>window.stopped>0));
  assert.equal(await page.locator('[name="mealSlotCode"]').inputValue(),'LUNCH');
  assert.equal(await page.locator('[name="servingSize"]').inputValue(),'30');
  assert.equal(await page.locator('[name="proteinG"]').inputValue(),'20');
  await page.locator('[data-scan-close]').click();await page.getByRole('button',{name:'Scan label',exact:true}).click();
  await page.locator('[data-scan-photo]').setInputFiles({name:'label.png',mimeType:'image/png',buffer:Buffer.from('test image')});
  await page.locator('#nutrition-custom-food-dialog[open]').waitFor();
  assert.equal(await page.locator('[name="proteinG"]').inputValue(),'25');assert.equal(await page.locator('[name="fatG"]').inputValue(),'2.5');assert.equal(await page.locator('[name="servingSize"]').inputValue(),'33');assert.equal(await page.locator('[name="servingUnitCode"]').inputValue(),'g');assert.equal(await page.locator('[name="mealSlotCode"]').inputValue(),'LUNCH');
  assert.equal(await page.locator('.nutrition-scan-review input').getAttribute('required'),'');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await page.evaluate(()=>document.querySelector('#nutrition-custom-food-dialog').close());
  await page.evaluate(()=>document.querySelector('#nutrition-food-picker-dialog').showModal());
  await page.evaluate(()=>{window.cameraDenied=true;});
  await page.getByRole('button',{name:'Scan barcode',exact:true}).click();
  await page.getByText('Camera access was denied. Allow camera access and restart, or enter the barcode below.',{exact:true}).waitFor();
  const view=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/Index.cshtml','utf8');
  assert.equal(view.includes('Copy yesterday\'s meals to today'),false);assert.equal(view.includes('nutrition-daily-actions'),false);assert.equal(view.includes('nutrition-scan-actions'),false);
  console.log('Live camera, one-shot detection and automatic meal submission, camera cleanup, permission fallback, label review and mobile layout passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
