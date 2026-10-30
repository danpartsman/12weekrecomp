const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});try{
 const page=await browser.newPage({viewport:{width:390,height:844}});
 const css=['site.css','coach-center.css','simple-coaching.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
 await page.setContent(`<html data-theme="dark"><style>${css}</style><body class="app-shell coach-shell"><main class="container"><h1>Client · Snap Workouts</h1><form data-simple-assignment><div data-simple-exercises><section class="simple-exercise" data-exercise><div class="simple-fields"><label>Exercise<input name="Exercises[0].Name" required></label><label>Sets<input name="Exercises[0].Sets" value="3" type="number"></label><label>Reps<input name="Exercises[0].Reps" value="10" type="number"></label><label>Max weight<input name="Exercises[0].MaxWeight" value="0" type="number"></label></div><button type="button" data-remove-exercise>Remove exercise</button></section></div><button type="button" data-add-exercise>Add exercise</button></form></main></body></html>`);
 await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/simple-coaching.js'});
 await page.locator('[name="Exercises[0].Name"]').fill('Shoulder press');await page.locator('[data-add-exercise]').click();
 assert.equal(await page.locator('[data-exercise]').count(),2);assert.equal(await page.locator('[name="Exercises[0].Name"]').inputValue(),'Shoulder press');
 await page.locator('[name="Exercises[1].Name"]').fill('Squat');await page.locator('[data-remove-exercise]').first().click();
 assert.equal(await page.locator('[data-exercise]').count(),1);assert.equal(await page.locator('[name="Exercises[0].Name"]').inputValue(),'Squat');
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 console.log('Simple workout exercise entry, add/remove indexing, preserved inputs and mobile layout passed.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
