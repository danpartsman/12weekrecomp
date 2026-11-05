const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}});
  const scanner=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/_FoodScanner.cshtml','utf8').replace(/<link[^>]*>|<script[\s\S]*?<\/script>/g,'').replace(/data-barcode-url="[\s\S]*?">/,'data-barcode-url="/nutrition/scan/barcode">');
  const fields=['foodName','brandName','energyKcal','proteinG','carbohydrateG','fatG','fiberG','servingSize','scanSource','scanBarcode'].map(name=>`<label>${name}<input name="${name}"></label>`).join('');
  const fixture=`<html><head><style>${fs.readFileSync('src/WebRecomp.Web/wwwroot/css/nutrition-scanner.css','utf8')}dialog{max-width:94vw;box-sizing:border-box}input{max-width:100%;box-sizing:border-box}</style></head><body><dialog id="nutrition-food-picker-dialog"><button data-food-scan="barcode">Scan barcode</button><button data-food-scan="label">Scan label</button><select data-picker-meal><option value="LUNCH">Lunch</option></select></dialog>${scanner}<dialog id="nutrition-custom-food-dialog"><form data-custom-food-form><div class="nutrition-custom-food-grid">${fields}<select name="servingUnitCode"><option>serving</option><option>g</option><option>ml</option></select><select name="mealSlotCode"><option>LUNCH</option></select></div></form></dialog></body></html>`;
  await page.route('https://scanner.test/**',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname.includes('barcode'))return route.fulfill({status:404,contentType:'application/json',body:JSON.stringify({message:'No exact product was found. Scan the nutrition label.'})});
   return route.fulfill({contentType:'text/html',body:fixture});
  });
  await page.route('https://cdn.jsdelivr.net/**',route=>route.fulfill({contentType:'application/javascript',body:''}));
  await page.goto('https://scanner.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-label-parser.js'});await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-scanner.js'});
  await page.evaluate(()=>{document.querySelector('#nutrition-food-picker-dialog').showModal();window.Tesseract={createWorker:async()=>({recognize:async()=>({data:{text:'Serving size 1 scoop (33 g)\nCalories 130\nTotal Fat 2.5g\nTotal Carbohydrate 3g\nProtein 25g'}}),terminate:async()=>{}})};});
  await page.getByRole('button',{name:'Scan barcode',exact:true}).click();await page.locator('[data-scan-code]').fill('012345678905');await page.getByRole('button',{name:'Look up barcode'}).click();await page.getByText('No exact product was found. Scan the nutrition label.',{exact:true}).waitFor();
  await page.locator('[data-scan-close]').click();await page.getByRole('button',{name:'Scan label',exact:true}).click();
  await page.locator('[data-scan-photo]').setInputFiles({name:'label.png',mimeType:'image/png',buffer:Buffer.from('test image')});
  await page.locator('#nutrition-custom-food-dialog[open]').waitFor();
  assert.equal(await page.locator('[name="proteinG"]').inputValue(),'25');assert.equal(await page.locator('[name="fatG"]').inputValue(),'2.5');assert.equal(await page.locator('[name="servingSize"]').inputValue(),'33');assert.equal(await page.locator('[name="servingUnitCode"]').inputValue(),'g');assert.equal(await page.locator('[name="mealSlotCode"]').inputValue(),'LUNCH');
  assert.equal(await page.locator('.nutrition-scan-review input').getAttribute('required'),'');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  console.log('Scanner mobile UI, missing barcode fallback, label review, serving basis, meal selection and required verification passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
