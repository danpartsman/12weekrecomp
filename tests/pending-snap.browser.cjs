const assert=require('node:assert/strict');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage();
  await page.setContent('<main><div data-pending-snap-host aria-live="polite"></div></main>');
  await page.evaluate(()=>{
   window.__pending={clientId:'11111111-1111-1111-1111-111111111111',workouts:[]};
   window.__fail=false;window.__calls=[];
   window.setInterval=(callback,ms)=>{window.__poll=callback;window.__interval=ms;return 1;};
   window.fetch=async(url,options)=>{window.__calls.push({url,options});return {ok:!window.__fail,headers:{get:()=> 'application/json'},json:async()=>window.__pending};};
  });
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/pending-snap.js'});
  await page.waitForFunction(()=>window.__calls.length>0);
  assert.equal(await page.locator('.pending-snap-banner').count(),0);
  await page.evaluate(()=>{window.__pending.workouts=[{id:'snap-1',name:'Strength express',date:'2026-10-28'}];window.dispatchEvent(new Event('focus'));});
  await page.waitForSelector('[data-pending-workout="snap-1"]');
  assert.equal(await page.locator('a').getAttribute('href'),'/accountability-preview/clients/11111111-1111-1111-1111-111111111111/snap?assignment=snap-1#snap-snap-1');
  assert.equal(await page.evaluate(()=>window.__interval),30000);
  await page.evaluate(async()=>{window.__fail=true;await window.__poll();});
  assert.equal(await page.locator('.pending-snap-banner').count(),1,'A connection failure keeps the pending notice');
  await page.evaluate(async()=>{window.__fail=false;window.__pending.workouts.push({id:'snap-2',name:'<img src=x onerror=alert(1)>',date:'2026-10-28'});await window.__poll();});
  assert.equal(await page.locator('[data-pending-workout]').count(),2);assert.equal(await page.locator('img').count(),0);
  await page.evaluate(()=>{window.__pending.workouts=[];window.dispatchEvent(new Event('snap-workouts-changed'));});
  await page.waitForFunction(()=>!document.querySelector('.pending-snap-banner'));
  assert.ok(await page.evaluate(()=>window.__calls.every(x=>x.url==='/my-coach/pending-snaps'&&x.options.credentials==='same-origin'&&x.options.cache==='no-store')));
  console.log('Pending Snap notices refresh on arrival, focus, periodic checks and completion; connection failures preserve the notice.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
