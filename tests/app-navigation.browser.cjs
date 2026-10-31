const assert=require('node:assert/strict');
const fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
function navigation({admin=false,demo=false}={}) {
 let html=fs.readFileSync('src/WebRecomp.Web/Views/Shared/_AppNavigation.cshtml','utf8').replace(/^@model.*$/m,'');
 html=html.replace(/@if \(([^\n]+)\)\s*\{([\s\S]*?)\n            \}/g,(_,condition,body)=>{
  const show=condition==='Model'?demo:condition==='!Model'?!demo:!demo&&admin;
  return show?body:'';
 });
 return html.replace(/<a([^>]*?) asp-controller="([^"]+)" asp-action="([^"]+)"([^>]*)>/g,(_,before,c,a,after)=>`<a${before} href="/${c}${a==='Index'?'':'/'+a}"${after}>`)
  .replace(/<form[^>]+>/g,'<form action="/Demo/Exit" method="post">');
}
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try {
  const page=await browser.newPage({viewport:{width:1440,height:900}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const css=['site.css','test-live-preview.css','design-system-v1012.css','app-navigation.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
  const script=fs.readFileSync('src/WebRecomp.Web/wwwroot/js/app-navigation.js','utf8');
  const html=`<!doctype html><html data-theme="dark"><head><meta charset="utf-8"><style>${css}</style></head><body class="app-shell test-live-preview"><header class="topbar"><div class="brand"><a href="/CommandCenter">12 WEEK RECOMP</a></div>${navigation({admin:true})}</header><main class="container"><h1>Section main page</h1><section class="card"><button id="outside">Outside</button></section></main><script>${script}</script></body></html>`;
  await page.route('https://navigation.test/**',route=>route.fulfill({contentType:'text/html',body:html.replace('Section main page',new URL(route.request().url()).pathname)}));
  await page.addInitScript(()=>{Object.defineProperty(window,'sessionStorage',{get(){throw new Error('Storage disabled for regression test');}});});
  await page.goto('https://navigation.test/CommandCenter');
  const groups=page.locator('.app-nav-group');
  assert.deepEqual(await page.locator('.app-nav-primary').allTextContents(),['Command Center','Daily Entries','Recomp Tips','Profile']);
  assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  const expected=[['Dashboard','Progress','Smart Intelligence'],['Recomp Tracking','Nutrition','Workout','Weekly Plan'],['How It Works','Evidence'],['Settings','My Coach','Notifications','Coach Center','Administration','Sign Out']];
  const paths=['/CommandCenter','/DailyEntry','/tips','/Profile'];
  async function navigate(i){const button=groups.nth(i).locator('summary');const box=await button.boundingBox();await Promise.all([page.waitForNavigation(),i%2?button.click({position:{x:box.width-10,y:box.height/2}}):groups.nth(i).locator('.app-nav-primary').click()]);assert.equal(new URL(page.url()).pathname,paths[i]);assert.equal(await page.locator('h1').textContent(),paths[i]);assert.equal(new URL(page.url()).searchParams.has('nav'),false);await page.waitForFunction(()=>document.querySelectorAll('.app-nav-group[open]').length===1);}
  for(let i=0;i<4;i++){
   await navigate(i);assert.deepEqual(await groups.nth(i).locator('.app-nav-panel a').allTextContents(),expected[i]);assert.equal(await groups.nth(i).locator('.app-nav-panel').isVisible(),true);
  }
  await page.keyboard.press('Escape');assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  await groups.nth(3).locator('.app-nav-primary').focus();await page.keyboard.press('ArrowDown');assert.equal(await page.evaluate(()=>document.activeElement.textContent),'Settings');
  await page.keyboard.press('End');assert.equal(await page.evaluate(()=>document.activeElement.textContent),'Sign Out');
  await page.evaluate(()=>document.querySelector('#outside').click());assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  await navigate(0);await page.mouse.move(700,700);await page.waitForFunction(()=>document.querySelectorAll('.app-nav-group[open]').length===0,{},{timeout:7000});
  await navigate(0);fs.mkdirSync('quantity-control-preview',{recursive:true});await page.screenshot({path:'quantity-control-preview/navigation-desktop.png'});
  for(const width of [820,390,320]){
   await page.setViewportSize({width,height:844});await page.evaluate(()=>document.querySelector('#outside').click());
   assert.equal(await page.locator('.app-nav-toggle').isVisible(),true);assert.equal(await groups.first().isVisible(),false);
   for(let i=0;i<4;i++){
    await page.locator('.app-nav-toggle').click();await navigate(i);
    assert.equal(await page.locator('.app-nav-toggle').getAttribute('aria-expanded'),'true');
    const panel=await groups.nth(i).locator('.app-nav-panel').boundingBox();assert.ok(panel.x>=0&&panel.x+panel.width<=width+1);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
    await page.evaluate(()=>document.querySelector('#outside').click());assert.equal(await page.locator('.app-nav-toggle').getAttribute('aria-expanded'),'false');
   }
  }
  await page.setViewportSize({width:390,height:844});await page.locator('.app-nav-toggle').click();await navigate(1);await page.screenshot({path:'quantity-control-preview/navigation-mobile.png'});
  // Coach pages must render the same responsive menu, rather than the legacy public-nav.
  const coachLayout=fs.readFileSync('src/WebRecomp.Web/Views/Shared/_CoachLayout.cshtml','utf8');
  assert.match(coachLayout, /PartialAsync\("_AppNavigation", false\)/);assert.match(coachLayout,/app-navigation\.css/);assert.match(coachLayout,/app-navigation\.js/);assert.equal(coachLayout.includes('aria-label="Coach navigation"'),false);
  await page.evaluate(()=>{document.body.className='app-shell coach-shell';});
  await page.addStyleTag({path:'src/WebRecomp.Web/wwwroot/css/coach-center.css'});
  await page.evaluate(()=>document.querySelector('#outside').click());
  assert.equal(await page.locator('.app-nav-toggle').isVisible(),true);await page.locator('.app-nav-toggle').click();
  assert.equal(await groups.first().isVisible(),true);await navigate(0);
  assert.deepEqual(errors,[]);assert.equal(navigation().includes('Coach Center'),false);assert.equal(navigation().includes('Administration'),false);assert.equal(navigation({demo:true}).includes('End Guided Demo'),true);assert.equal(navigation({demo:true}).includes('Sign Out'),false);
  console.log('Primary navigation changes page and opens destination flyouts with storage blocked; timed dismissal, keyboard, mobile hamburger, viewport and roles passed.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
