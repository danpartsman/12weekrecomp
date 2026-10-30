const assert=require('node:assert/strict');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});try{
 const page=await browser.newPage();await page.setContent('<p data-snap-countdown data-workout-id="test" data-server-now="2026-10-30T10:00:00Z" data-deadline="2026-10-30T12:00:00Z" data-finished="false"><span data-time-remaining></span></p>');
 await page.evaluate(()=>{window.__now='2026-10-30T10:00:00Z';window.__finished=false;window.__callbacks=[];window.setInterval=(fn,delay)=>{window.__callbacks.push({fn,delay});return 1;};window.fetch=async()=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>({nowUtc:window.__now,workouts:[{id:'test',finished:window.__finished,finishedAtUtc:'2026-10-30T11:00:00Z'}]})});});
 await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/snap-countdown.js'});await page.waitForFunction(()=>document.querySelector('[data-time-remaining]').textContent.includes('remaining'));
 assert.match(await page.locator('[data-time-remaining]').textContent(),/2h 0m 0s|1h 59m/);
 await page.evaluate(async()=>{window.__now='2026-10-30T12:00:01Z';await window.__callbacks.find(c=>c.delay===30000).fn();});assert.match(await page.locator('[data-time-remaining]').textContent(),/Time expired/);
 await page.evaluate(async()=>{window.__finished=true;await window.__callbacks.find(c=>c.delay===30000).fn();});assert.equal(await page.locator('[data-time-remaining]').textContent(),'Finished on time');
 console.log('Server-clock countdown, deadline expiry and finalized status updates passed.');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1);});
