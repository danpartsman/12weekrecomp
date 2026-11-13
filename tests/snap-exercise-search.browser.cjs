const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}});
  const css=['site.css','coach-center.css','simple-coaching.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
  const markup=`<html data-theme="dark"><style>${css}</style><body class="coach-shell"><main class="container"><h1>Assign a Snap Workout</h1><form data-simple-assignment data-exercise-search-url="/lookup"><div data-simple-exercises><section data-exercise><label>Exercise · search or type your own<input name="Exercises[0].Name" required></label><label>Muscle focus (optional)<input name="Exercises[0].MuscleFocus"></label><input name="Exercises[0].Id" type="hidden" value=""><input name="Exercises[0].Sets" type="hidden" value="1"><input name="Exercises[0].Reps" type="hidden" value="10"><button type="button" data-remove-exercise>Remove exercise</button></section></div><button type="button" data-add-exercise>Add exercise</button><button type="submit">Assign workout</button></form></main></body></html>`;
  await page.route('https://snap.test/**',async route=>{
   const url=new URL(route.request().url());
   if(url.pathname!='/lookup')return route.fulfill({contentType:'text/html',body:markup});
   const q=url.searchParams.get('q');
   if(q==='offline')return route.fulfill({status:503,body:'unavailable'});
   if(q==='slow push')await new Promise(resolve=>setTimeout(resolve,500));
   const items=q==='Custom coach movement'?[]:q==='legs'?[{name:'Squat',muscles:'Quadriceps, Glutes'}]:[{name:'Push-Up',muscles:'Chest, Shoulders, Triceps'}];
   await route.fulfill({contentType:'application/json',body:JSON.stringify(items)}).catch(()=>{});
  });
  await page.goto('https://snap.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/simple-coaching.js'});
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/snap-exercise-search.js'});
  const name=page.locator('[name="Exercises[0].Name"]'),muscles=page.locator('[name="Exercises[0].MuscleFocus"]');
  await name.fill('push');await page.getByRole('button',{name:'Push-Up · Chest, Shoulders, Triceps',exact:true}).waitFor();
  if(process.env.SNAP_LOOKUP_SCREENSHOT)await page.screenshot({path:process.env.SNAP_LOOKUP_SCREENSHOT});
  await page.getByRole('button',{name:'Push-Up · Chest, Shoulders, Triceps',exact:true}).click();
  assert.equal(await name.inputValue(),'Push-Up');assert.equal(await muscles.inputValue(),'Chest, Shoulders, Triceps');
  await name.fill('Custom coach movement');await page.waitForFunction(()=>document.querySelector('[data-snap-exercise-status]').textContent.includes('No library match'));
  assert.equal(await name.inputValue(),'Custom coach movement');assert.equal(await muscles.inputValue(),'');assert.equal(await name.evaluate(n=>n.checkValidity()),true);
  await muscles.fill('Glutes');await name.fill('Push ups');await page.waitForFunction(()=>document.querySelector('[data-snap-exercise-status]').textContent.includes('Library match'));
  assert.equal(await name.inputValue(),'Push ups');assert.equal(await muscles.inputValue(),'Glutes');
  await muscles.fill('');await name.fill('Push-up');assert.equal(await muscles.inputValue(),'Chest, Shoulders, Triceps');
  await name.fill('offline');await page.waitForFunction(()=>document.querySelector('[data-snap-exercise-status]').textContent.includes('Lookup unavailable'));
  assert.equal(await name.inputValue(),'offline');assert.equal(await name.evaluate(n=>n.checkValidity()),true);
  await name.fill('slow push');await page.waitForTimeout(300);await name.fill('legs');await page.getByRole('button',{name:'Squat · Quadriceps, Glutes',exact:true}).waitFor();await page.waitForTimeout(600);
  assert.equal(await name.inputValue(),'legs');assert.equal(await muscles.inputValue(),'');assert.equal(await page.getByRole('button',{name:'Push-Up · Chest, Shoulders, Triceps',exact:true}).count(),0);
  await page.getByRole('button',{name:'Squat · Quadriceps, Glutes',exact:true}).click();assert.equal(await muscles.inputValue(),'Quadriceps, Glutes');
  await page.locator('[data-add-exercise]').click();await page.locator('[name="Exercises[1].Name"]').fill('Push ups');
  await page.waitForFunction(()=>document.querySelector('[name="Exercises[1].MuscleFocus"]').value==='Chest, Shoulders, Triceps');
  assert.equal(new Set(await page.locator('[data-snap-exercise-options]').evaluateAll(nodes=>nodes.map(n=>n.id))).size,2);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  console.log('Mobile exercise lookup, muscle autofill, spelling variants, custom text, manual edits, failed/stale requests and cloned rows passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exit(1);});
