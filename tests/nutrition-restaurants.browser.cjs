const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  const view=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/Index.cshtml','utf8');
  const pickerScript=[...view.matchAll(/<script>([\s\S]*?)<\/script>/g)].find(match=>match[1].includes('const selectChoice'))[1];
  const restaurant=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/_RestaurantPicker.cshtml','utf8')
   .replace(/<link[^>]*>|<script[\s\S]*?<\/script>/g,'')
   .replace('@Url.Action("Search", "NutritionRestaurant")','/nutrition/restaurants')
   .replace('@Url.Action("Menu", "NutritionRestaurant")','/nutrition/restaurants/menu');
  const css=fs.readFileSync('src/WebRecomp.Web/wwwroot/css/nutrition-restaurants.css','utf8');
  const hiddenFields=['providerCode','sourceFoodId','amount','measureCode','mealSlotCode'].map(name=>`<input name="${name}" type="hidden">`).join('');
  const html=`<html><head><style>${css}dialog{max-width:94vw;box-sizing:border-box} [hidden]{display:none!important}</style></head><body>
   <dialog id="nutrition-food-picker-dialog" data-picker-data-url="/foods" data-picker-measures-url="/measures" data-picker-date="2026-11-06">
   <select data-picker-meal><option value="DINNER">Dinner</option><option value="LUNCH">Lunch</option></select><input data-picker-search>
   <div data-picker-results><div data-picker-past-meals></div><div data-picker-foods></div><button data-picker-restaurant-open>Restaurant</button></div>
   <section data-picker-selected hidden><div data-picker-selection></div><div data-picker-provider-controls hidden><select data-picker-measure></select><input data-picker-amount type="number" value="1"><div data-picker-amount-presets></div></div><button data-picker-add disabled>Add</button></section>
   <form data-picker-provider-form>${hiddenFields}</form></dialog>${restaurant}</body></html>`;
  let fail=false,menuRequests=0;
  await page.route('https://restaurant.test/**',route=>{
   const url=new URL(route.request().url());
   if(url.pathname==='/nutrition/restaurants')return route.fulfill({contentType:'application/json',status:fail?503:200,body:JSON.stringify(fail?{message:'Restaurant lookup is unavailable.'}:{restaurants:['Chipotle']})});
   if(url.pathname.endsWith('/menu')){menuRequests++;const second=url.searchParams.get('page')==='2';return route.fulfill({contentType:'application/json',body:JSON.stringify({items:[{providerCode:'FATSECRET',sourceFoodId:second?'222':'111',description:second?'Salad':'Chicken bowl <test>',serving:'1 bowl',energyKcal:600,proteinG:40,carbohydrateG:65,fatG:20}],nextPage:second?null:2})});}
   if(url.pathname==='/measures')return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,defaultMeasureCode:'serving:10',measures:[{code:'serving:10',description:'1 bowl',unitCode:'serving',energyKcal:600,proteinG:40,carbohydrateG:65,fatG:20}]})});
   return route.fulfill({contentType:'text/html',body:html});
  });
  await page.goto('https://restaurant.test/');
  await page.addScriptTag({content:pickerScript});
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-restaurants.js'});
  await page.evaluate(()=>{
   document.querySelector('#nutrition-food-picker-dialog').showModal();window.savedFood=null;
   document.querySelector('[data-picker-provider-form]').addEventListener('submit',event=>{event.preventDefault();window.savedFood=Object.fromEntries(new FormData(event.target));});
  });
  await page.getByRole('button',{name:'Restaurant',exact:true}).click();
  await page.locator('[data-restaurant-name]').fill('Chip');await page.locator('[data-restaurant-name]').press('Enter');
  await page.getByRole('button',{name:'Chipotle',exact:true}).click();
  await page.getByRole('button',{name:'Load more items'}).click();
  await page.getByRole('button',{name:/Salad/}).waitFor();assert.equal(menuRequests,2);
  assert.equal(await page.locator('[data-restaurant-more]').isVisible(),false);
  for(const width of [320,390,820]){await page.setViewportSize({width,height:844});assert.equal(await page.locator('#nutrition-restaurant-picker').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);}
  await page.getByRole('button',{name:/Chicken bowl/}).click();
  assert.equal(await page.locator('#nutrition-restaurant-picker').evaluate(el=>el.open),false);
  await page.waitForFunction(()=>!document.querySelector('[data-picker-add]').disabled);
  assert.equal(await page.locator('[data-picker-selection]').textContent(),'Chipotle — Chicken bowl <test>Choose the amount and serving, then Add.');
  assert.equal(await page.locator('[data-picker-selection] test').count(),0,'Provider text is not interpreted as HTML');
  await page.locator('[data-picker-amount]').fill('0.5');await page.locator('[data-picker-add]').click();
  assert.deepEqual(await page.evaluate(()=>window.savedFood),{providerCode:'FATSECRET',sourceFoodId:'111',amount:'0.5',measureCode:'serving:10',mealSlotCode:'DINNER'});
  await page.locator('[data-picker-results]').evaluate(el=>el.hidden=false);
  await page.getByRole('button',{name:'Restaurant',exact:true}).click();
  fail=true;await page.locator('[data-restaurant-name]').fill('Chip');await page.locator('[data-restaurant-name]').press('Enter');
  await page.getByText('Restaurant lookup is unavailable.',{exact:true}).waitFor();
  fail=false;await page.getByRole('button',{name:'Try again',exact:true}).click();
  await page.getByRole('button',{name:'Chipotle',exact:true}).click();
  await page.getByRole('button',{name:'Change restaurant',exact:true}).click();
  assert.equal(await page.locator('[data-restaurant-name]').inputValue(),'');
  await page.getByRole('button',{name:'Close',exact:true}).click();
  assert.equal(await page.locator('[data-picker-meal]').inputValue(),'DINNER');
  assert.deepEqual(errors,[]);
  console.log('Restaurant directory, paged menu, serving macros, safe selection, portion submission to selected meal, retry/back/close and mobile layout passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
