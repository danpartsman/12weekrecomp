const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');
const engine = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
// Use the shipped partials for section structure; supply Razor's model flags and dates for this isolated browser test.
function fixture() {
  let html = fs.readFileSync('src/WebRecomp.Web/Views/Shared/_AccountabilityPreview.cshtml','utf8');
  html = html.replace('@await Html.PartialAsync("_SnapWorkout", Model)', fs.readFileSync('src/WebRecomp.Web/Views/Shared/_SnapWorkout.cshtml','utf8'));
  html = html.replace(/@for \(var week = 1; week <= 52; week\+\+\) \{.*?\}/s, '<option value="12">12 weeks</option>');
  html = html.replace(/@\{[^}]*\}/g,'').replace(/^@(model|inject).*$/gm,'').replace(/@Html.AntiForgeryToken\(\)/g,'<input name="__RequestVerificationToken" type="hidden" value="test-token">');
  // Remove Razor conditional delimiters, keeping the coach and approval form markup used by this fixture.
  const pattern = /@if\s*\(/g; let match;
  while ((match=pattern.exec(html))) {
    let pos=pattern.lastIndex, depth=1;
    while(depth && pos<html.length) { if(html[pos]==='(')depth++; if(html[pos]===')')depth--;pos++; }
    while(/\s/.test(html[pos]))pos++;
    if(html[pos]!=='{')throw new Error('Unexpected Razor fixture conditional');
    let end=pos+1, braces=1;
    while(braces && end<html.length) {if(html[end]==='{')braces++; if(html[end]==='}')braces--;end++;}
    html=html.slice(0,match.index)+html.slice(pos+1,end-1)+html.slice(end); pattern.lastIndex=0;
  }
  html=html.replace(/<section class="accountability card"[^>]+>/,'<section class="accountability card" data-accountability data-client-id="11111111-1111-1111-1111-111111111111" data-editable="true" data-review-page="true" data-can-assign-snap="true">')
    .replaceAll('open="@(!Model.Editable)"','').replaceAll('@Model.ClientId','11111111-1111-1111-1111-111111111111')
    .replace('@(Model.Editable ? "Client agreement" : "Your assigned arrangement")','Client agreement')
    .replace(/<script[^>]*>.*?<\/script>/gs,'');
  return html;
}
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try {
  const page=await browser.newPage({viewport:{width:1280,height:900}}), errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const css=['site.css','coach-center.css','accountability-preview.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
  await page.setContent(`<html data-theme="dark"><head><style>${css}</style></head><body class="app-shell coach-shell"><main class="container"><h1>Client · Fines &amp; incentives</h1>${fixture()}</main></body></html>`);
  const initial=engine.defaults();initial.agreement.start='2026-10-27';
  const template={name:'Strength express',cap:75,exercises:[{id:'old-exercise',catalogId:'22222222-2222-2222-2222-222222222222',name:'Dumbbell curl',sets:2,reps:10,weight:20,type:'fine',basis:'incomplete-reps',rate:.25,completionReward:5,extraRepRate:.1}]};
  await page.evaluate(({initial,template})=>{
   window.__state=initial; window.__saves=[]; let revision=0;
   const json=data=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>data});
   window.fetch=async(url,options={})=>{
    if(options.method==='POST'){
      const fields=Object.fromEntries(options.body); window.__saves.push(fields);
      if(fields.StateJson)window.__state=JSON.parse(fields.StateJson);
      return json({state:window.__state,revision:++revision,termsVersion:revision,terms:fields.Terms,approval:null});
    }
    if(url.endsWith('/snap-library'))return json([template,{...template,name:'Second strength workout'}]);
    if(url.includes('/exercises'))return json([{id:template.exercises[0].catalogId,name:'Dumbbell curl'}]);
    if(url.includes('/evidence'))return json({today:'2026-10-27',weekStart:'2026-10-26',nowUtc:'2026-10-27T18:00:00Z',timeZone:'America/Los_Angeles',days:[{date:'2026-10-27',completed:false,actual:null,target:null}]});
    return json({state:initial,revision:0,terms:null,approval:null});
   };
  },{initial,template});
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/accountability-preview.js'});
  await page.waitForFunction(()=>document.querySelector('[data-snap-library] input') && document.querySelector('[name=reuseDeadline]').value);
  assert.deepEqual(errors,[]);
  const panel=page.locator('#snap-workout');assert.equal(await panel.getAttribute('open'),null);
  await panel.locator(':scope > summary').click();
  const library=page.locator('.snap-library');await library.locator('summary').click();
  await page.locator('[name=template]').nth(0).check();await page.locator('[name=template]').nth(1).check();
  await page.locator('[data-snap-reuse] button[type=submit]').click();
  await page.waitForFunction(()=>window.__saves.length===1);
  await page.waitForFunction(()=>document.querySelector('[data-snap-library-status]').textContent.includes('2 workout(s) assigned'));
  const saved=await page.evaluate(()=>JSON.parse(window.__saves[0].StateJson));
  assert.equal(saved.snapWorkouts.length,2);assert.equal(saved.snapWorkouts[0].cap,75);assert.equal(saved.snapWorkouts[0].exercises[0].rate,.25);
  assert.notEqual(saved.snapWorkouts[0].id,saved.snapWorkouts[1].id);assert.notEqual(saved.snapWorkouts[0].exercises[0].id,'old-exercise');
  assert.equal(saved.snapWorkouts[0].date,'2026-10-27');assert.ok(Date.parse(saved.snapWorkouts[0].deadlineUtc)>Date.parse(saved.snapWorkouts[0].createdAtUtc));
  assert.deepEqual(saved.snapResults,{});assert.equal(await page.locator('[name=template]:checked').count(),0);
  assert.equal(await page.evaluate(()=>window.__saves[0].__RequestVerificationToken),'test-token');
  await library.locator('summary').click();await panel.locator(':scope > summary').click();
  fs.mkdirSync('quantity-control-preview',{recursive:true});
  await page.screenshot({path:'quantity-control-preview/coach-panels-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await panel.locator(':scope > summary').click();await library.locator('summary').click();
  await page.screenshot({path:'quantity-control-preview/coach-workout-library-mobile.png',fullPage:true});
  assert.deepEqual(errors,[]);console.log('Coach disclosures, checkbox batch assignment, fresh identity/deadline, preserved rates and CSRF passed.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
