const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');
const engine = require('../src/WebRecomp.Web/wwwroot/js/accountability-preview.js');
// Use the shipped partials for section structure; supply Razor's model flags and dates for this isolated browser test.
function fixture() {
  let html = fs.readFileSync('src/WebRecomp.Web/Views/Shared/_AccountabilityPreview.cshtml','utf8');
  html = html.replace('@await Html.PartialAsync("_SnapWorkout", Model)', fs.readFileSync('src/WebRecomp.Web/Views/Shared/_SnapWorkout.cshtml','utf8'));
  html = html.replace(/disabled="@\(ViewData\["OldCoaching"\] is true\)"/g,'');
  html = html.replace(/@for \(var week = 1; week <= 52; week\+\+\) \{.*?\}/s, '<option value="12">12 weeks</option>');
  html = html.replace(/^\s*var initialDefaults.*$/gm,'').replace(/@\{[^}]*\}/g,'').replace(/^\s*@(model|inject).*$/gm,'').replace(/@Html.AntiForgeryToken\(\)/g,'<input name="__RequestVerificationToken" type="hidden" value="test-token">');
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
  html=html.replace(/<section class="accountability card"[^>]+>/,'<section class="accountability card" data-accountability data-client-id="11111111-1111-1111-1111-111111111111" data-editable="true" data-review-page="true" data-can-assign-snap="true" data-snap-page="true">')
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
  const html = `<html data-theme="dark"><head><meta charset="utf-8"><style>${css}</style></head><body class="app-shell coach-shell"><main class="container"><h1>Client · Fines &amp; incentives</h1>${fixture()}</main></body></html>`;
  await page.route('https://coach-portal.test/**',route=>route.fulfill({contentType:'text/html',body:html}));
  await page.goto('https://coach-portal.test/');
  const initial=engine.defaults();initial.agreement.start='2026-10-27';
  const template={name:'Strength express',cap:75,exercises:[{id:'old-exercise',catalogId:'22222222-2222-2222-2222-222222222222',name:'Dumbbell curl',sets:2,reps:10,weight:20,type:'fine',basis:'incomplete-reps',rate:.25,completionReward:5,extraRepRate:.1}]};
  await page.evaluate(({initial,template})=>{
   window.__state=initial; window.__saves=[]; let revision=0;
   const json=data=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>data});
   window.fetch=async(url,options={})=>{
    if(options.method==='POST'){
      const fields=Object.fromEntries(options.body); window.__saves.push({...fields,__url:url});
      if(fields.StateJson)window.__state=JSON.parse(fields.StateJson);
      if(fields.ResultJson)window.__state.snapResults[fields.WorkoutId]=JSON.parse(fields.ResultJson);
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
  assert.equal(await page.evaluate(()=>window.__saves[0].__url),'/accountability-preview/clients/11111111-1111-1111-1111-111111111111/state/snap');
  assert.equal(saved.snapWorkouts.length,2);assert.equal(saved.snapWorkouts[0].cap,75);assert.equal(saved.snapWorkouts[0].exercises[0].rate,.25);
  assert.notEqual(saved.snapWorkouts[0].id,saved.snapWorkouts[1].id);assert.notEqual(saved.snapWorkouts[0].exercises[0].id,'old-exercise');
  assert.equal(saved.snapWorkouts[0].date,'2026-10-27');assert.ok(Date.parse(saved.snapWorkouts[0].deadlineUtc)>Date.parse(saved.snapWorkouts[0].createdAtUtc));
  assert.deepEqual(saved.snapResults,{});assert.equal(await page.locator('[name=template]:checked').count(),0);
  assert.equal(await page.evaluate(()=>window.__saves[0].__RequestVerificationToken),'test-token');
  const workoutId=saved.snapWorkouts[0].id, exerciseId=saved.snapWorkouts[0].exercises[0].id;
  await page.evaluate(({workoutId})=>{
    const banner=document.createElement('aside');banner.className='pending-snap-banner';
    banner.innerHTML=`<strong>1 Snap Workout to complete</strong><ul><li data-pending-workout="${workoutId}">Open workout</li></ul>`;
    document.querySelector('main').prepend(banner);
  },{workoutId});
  async function record(reps) {
    const article=page.locator('#snap-'+workoutId);
    if (await article.getAttribute('open') === null) await article.locator(':scope > summary').click();
    await article.locator('input[name="'+exerciseId+'-0-reps"]').fill(String(reps));
    await article.locator('input[name="'+exerciseId+'-0-weight"]').fill('20');
    await article.locator('input[name="'+exerciseId+'-1-reps"]').fill('10');
    await article.locator('input[name="'+exerciseId+'-1-weight"]').fill('20');
    const before=await page.evaluate(()=>window.__saves.length);
    await article.getByRole('button',{name:'Finish workout',exact:true}).click();
    await page.waitForFunction(before=>window.__saves.length>before,before);
    await page.waitForFunction(()=>document.querySelector('[data-snap-status]').textContent.includes('Workout results saved'));
  }
  await record(9);assert.equal(await page.locator('.pending-snap-banner').count(),1,'Incomplete final results retain the notice');
  await record(10);assert.equal(await page.locator('.pending-snap-banner').count(),0,'Completed prescribed work clears the notice immediately');

  await library.locator('summary').click();await panel.locator(':scope > summary').click();
  fs.mkdirSync('quantity-control-preview',{recursive:true});
  await page.screenshot({path:'quantity-control-preview/coach-panels-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await panel.locator(':scope > summary').click();await library.locator('summary').click();
  await page.screenshot({path:'quantity-control-preview/coach-workout-library-mobile.png',fullPage:true});
  const newPage=await browser.newPage();
  await newPage.route('https://new-coach-client.test/**',route=>route.fulfill({contentType:'text/html',body:html}));
  await newPage.goto('https://new-coach-client.test/');
  await newPage.evaluate(({initial})=>{
    const config={rules:initial.rules.map(r=>({id:r.id,amount:3.25,min:1.5,max:55})),paymentAmount:25,lateValue:5,lateCap:20,snapCap:75,snapRate:.27,completionReward:3,extraRepRate:.15};
    document.querySelector('[data-accountability]').dataset.defaults=JSON.stringify(config);
    window.fetch=async url=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>url.endsWith('/state')?{state:null,revision:0,terms:null,approval:null}:url.includes('/evidence')?{today:'2026-10-27',weekStart:'2026-10-26',nowUtc:'2026-10-27T18:00:00Z',timeZone:'America/Los_Angeles',days:[{date:'2026-10-27',completed:false,actual:null,target:null}]}:[]});
  },{initial});
  await newPage.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/accountability-preview.js'});
  await newPage.waitForFunction(()=>document.querySelector('[name=calories-amount]')?.value==='3.25');
  assert.equal(await newPage.locator('[name=calories-min]').inputValue(),'1.5');
  assert.equal(await newPage.locator('[name=calories-max]').inputValue(),'55');
  assert.equal(await newPage.locator('[name=snapCap]').inputValue(),'75');
  assert.equal(await newPage.locator('[name=paymentAmount]').inputValue(),'25');
  await newPage.close();
  const simplePage=await browser.newPage({viewport:{width:1280,height:900}}), simpleErrors=[];
  simplePage.on('pageerror',e=>simpleErrors.push(e.message));
  await simplePage.route('https://simple-snap.test/**',route=>route.fulfill({contentType:'text/html',body:html}));
  await simplePage.goto('https://simple-snap.test/');
  await simplePage.evaluate(()=>{
    const root=document.querySelector('[data-accountability]');root.dataset.simpleSnap='true';root.dataset.editable='false';
    document.querySelector('#snap-workout').open=true;
    window.__simpleSaved=[];
    const fresh={version:2,enabled:false,rules:[],payment:{},agreement:{start:'',mode:'open',weeks:12,endedOn:null},snapWorkouts:[],snapResults:{},excused:{},observations:{}};
    window.fetch=async(url,options={})=>{
      let data;
      if(options.method==='POST') {const fields=Object.fromEntries(options.body);window.__simpleSaved.push(fields);data={state:JSON.parse(fields.StateJson),revision:1,termsVersion:1,terms:fields.Terms,approval:null};}
      else if(url.endsWith('/state'))data={state:null,revision:0,terms:null,approval:null};
      else if(url.includes('/evidence'))data={today:'2026-10-28',weekStart:'2026-10-26',nowUtc:'2026-10-28T18:00:00Z',timeZone:'America/Los_Angeles',days:[{date:'2026-10-28',completed:false,actual:null,target:null}]};
      else data=[];
      return {ok:true,headers:{get:()=> 'application/json'},json:async()=>data};
    };
  });
  await simplePage.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/accountability-preview.js'});
  await simplePage.waitForFunction(()=>document.querySelector('[name=snapDeadline]').value);
  const cards=simplePage.locator('.snap-simple-exercise');
  assert.equal(await cards.count(),1);assert.equal(await simplePage.locator('[data-snap-search]').count(),0);
  await cards.nth(0).getByLabel('Exercise',{exact:true}).fill('Dumbbell curl');
  await cards.nth(0).getByLabel('Sets',{exact:true}).fill('3');
  await cards.nth(0).getByLabel('Reps per set',{exact:true}).fill('12');
  await cards.nth(0).getByLabel('Max weight in pounds',{exact:true}).fill('25');
  await cards.nth(0).locator('.snap-simple-fees > summary').click();
  await cards.nth(0).getByLabel('Dollars per assessed rep or set',{exact:true}).fill('0.35');
  await cards.nth(0).getByLabel('Reward for completing this exercise',{exact:true}).fill('5');
  await simplePage.locator('[data-snap-add]').click();
  assert.equal(await cards.count(),2);
  assert.equal(await cards.nth(0).getByLabel('Exercise',{exact:true}).inputValue(),'Dumbbell curl');
  await cards.nth(1).getByLabel('Exercise',{exact:true}).fill('Bodyweight squat');
  fs.mkdirSync('quantity-control-preview',{recursive:true});
  await simplePage.screenshot({path:'quantity-control-preview/simple-snap-desktop.png',fullPage:true});
  await simplePage.setViewportSize({width:390,height:844});
  assert.equal(await simplePage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await cards.nth(0).locator('.snap-simple-fees > summary').click();
  await simplePage.screenshot({path:'quantity-control-preview/simple-snap-mobile.png',fullPage:true});
  await simplePage.locator('[data-snap-assign] button[type=submit]').click();
  await simplePage.waitForFunction(()=>window.__simpleSaved.length===1);
  const manual=await simplePage.evaluate(()=>JSON.parse(window.__simpleSaved[0].StateJson));
  assert.equal(manual.snapWorkouts[0].exercises.length,2);
  assert.equal(manual.snapWorkouts[0].exercises[0].catalogId,'');
  assert.equal(manual.snapWorkouts[0].exercises[0].maxWeight,25);
  assert.equal(manual.snapWorkouts[0].exercises[0].weight,0);
  assert.equal(manual.snapWorkouts[0].exercises[0].rate,.35);
  assert.equal(manual.snapWorkouts[0].exercises[0].completionReward,5);
  assert.equal(manual.agreement.start,'2026-10-28');
  assert.deepEqual(simpleErrors,[]);await simplePage.close();
  assert.deepEqual(errors,[]);console.log('Coach disclosures, checkbox batch assignment, fresh identity/deadline, preserved rates and CSRF passed.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
