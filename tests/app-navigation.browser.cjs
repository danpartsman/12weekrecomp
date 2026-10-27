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
  const html=`<!doctype html><html data-theme="dark"><head><meta charset="utf-8"><style>${css}</style></head><body class="app-shell test-live-preview"><header class="topbar"><div class="brand"><a href="/CommandCenter">12 WEEK RECOMP</a></div>${navigation({admin:true})}</header><main class="container"><h1>Command Center</h1><section class="card"><h2>Your daily plan</h2><button id="outside">View today's plan</button></section></main></body></html>`;
  await page.route('https://navigation.test/**',route=>route.fulfill({contentType:'text/html',body:html}));
  await page.goto('https://navigation.test/CommandCenter');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/app-navigation.js'});
  assert.deepEqual(await page.locator('.app-nav-group > summary').allTextContents(),['Command Center','Daily Entries','Recomp Tips','Profile']);
  assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  const expected=[['Command Center','Dashboard','Progress'],['Daily Entries','Recomp Tracking','Nutrition','Workout','Weekly Plan'],['Recomp Tips','How It Works','Evidence'],['Profile','Settings','My Coach','Notifications','Coach Center','Administration','Sign Out']];
  const groups=page.locator('.app-nav-group');
  for(let i=0;i<4;i++){
    await groups.nth(i).locator('summary').click();
    await page.waitForFunction(()=>document.querySelectorAll('.app-nav-group[open]').length===1);
    assert.deepEqual(await groups.nth(i).locator('a').allTextContents(),expected[i]);
    assert.equal(await groups.nth(i).locator('.app-nav-panel').isVisible(),true);
  }
  await page.keyboard.press('Escape');assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  assert.equal(await groups.nth(3).locator('summary').evaluate(n=>n===document.activeElement),true);
  await page.keyboard.press('ArrowDown');await page.keyboard.press('ArrowDown');
  assert.equal(await page.evaluate(()=>document.activeElement.textContent),'Settings');
  await page.keyboard.press('End');assert.equal(await page.evaluate(()=>document.activeElement.textContent),'Sign Out');
  await page.locator('#outside').click();assert.equal(await page.locator('.app-nav-group[open]').count(),0);
  await groups.nth(0).locator('summary').click();
  assert.equal(await page.locator('a[aria-current=page]').getAttribute('href'),'/CommandCenter');
  fs.mkdirSync('quantity-control-preview',{recursive:true});
  await page.screenshot({path:'quantity-control-preview/navigation-desktop.png'});
  for(const width of [820,390,320]){
    await page.setViewportSize({width,height:844});
    for(let i=0;i<4;i++){
      await groups.nth(i).locator('summary').click();
      if(await groups.nth(i).getAttribute('open')===null)await groups.nth(i).locator('summary').click();
      const panel=await groups.nth(i).locator('.app-nav-panel').boundingBox();
      assert.ok(panel.x>=0 && panel.x+panel.width<=width+1,'Dropdown must stay inside phone viewport');
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
    }
  }
  await page.setViewportSize({width:390,height:844});
  await groups.nth(1).locator('summary').click();
  await page.screenshot({path:'quantity-control-preview/navigation-mobile.png'});
  assert.deepEqual(errors,[]);
  assert.equal(navigation().includes('Coach Center'),false);
  assert.equal(navigation().includes('Administration'),false);
  assert.equal(navigation({demo:true}).includes('End Guided Demo'),true);
  assert.equal(navigation({demo:true}).includes('Sign Out'),false);
  console.log('Grouped navigation links, keyboard controls, outside dismissal, roles, demo exit and phone viewport checks passed.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
