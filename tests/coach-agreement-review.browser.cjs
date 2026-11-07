const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  const view=fs.readFileSync('src/WebRecomp.Web/Views/CoachWorkspace/Index.cshtml','utf8');
  const inline=view.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll('@@media','@media');
  const css=['site.css','test-live-preview.css','coach-agreement.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n')+'\n'+inline;
  const partial=fs.readFileSync('src/WebRecomp.Web/Views/CoachWorkspace/_FinancialAgreementRules.cshtml','utf8');
  const article=partial.match(/<article class="coach-agreement-rule">[\s\S]*?<\/article>/)[0]
   .replace('@title','Protein outside target penalty')
   .replace('@rule.Amount.ToString("C",CultureInfo.GetCultureInfo("en-US"))','$5.00')
   .replace('@CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rule.Frequency)','Daily')
   .replace('@(i<4?"Daily nutrition limits":"When it applies")','Daily nutrition limits')
   .replace('@CoachAgreementLimits.Describe(rule,i)',"10% under to 10% over the system's daily prescription");
  const form=view.match(/<form class="coach-financial-response"[\s\S]*?<\/form>/)[0].replace('@Html.AntiForgeryToken()','').replace('@financial.Revision','1');
  const html=`<html data-theme="dark"><head><style>${css}</style></head><body class="app-shell test-live-preview"><main class="container"><div data-pending-agreements-host aria-live="polite"></div><div class="coach-workspace-page"><section class="card" id="financial-agreement"><h2>Penalties, incentives &amp; agreement</h2><div class="coach-agreement-rules">${article.repeat(3)}</div>${form}</section></div></main></body></html>`;
  let agreements=[],unavailable=false;
  await page.route('https://agreement.test/**',route=>{
   if(new URL(route.request().url()).pathname==='/my-coach/pending-agreements')return route.fulfill({status:unavailable?503:200,contentType:'application/json',body:JSON.stringify({agreements})});
   return route.fulfill({contentType:'text/html',body:html});
  });
  await page.goto('https://agreement.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/pending-agreements.js'});
  await page.locator('[data-pending-agreements-host][aria-busy="false"]').waitFor({state:'attached'});
  for(const width of [320,354,390,820,1280]){
   await page.setViewportSize({width,height:844});
   assert.equal(await page.locator('.coach-workspace-page').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true,`Agreement fits at ${width}px`);
   const title=page.locator('.coach-agreement-rule h4').first();
   assert.equal(await title.evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);
   assert.equal(await page.locator('.coach-agreement-amount').first().textContent(),'$5.00');
   assert.equal(await page.locator('button[value="accepted"]').evaluate(el=>el.getBoundingClientRect().height>=48),true);
   assert.equal(await page.locator('input[name="password"]').getAttribute('autocomplete'),'current-password');
  }
  const clientId='11111111-1111-1111-1111-111111111111',coachId='22222222-2222-2222-2222-222222222222';
  agreements=[{clientId,coachId,coachName:'Coach <Nina>',revision:1}];
  await page.evaluate(()=>window.dispatchEvent(new Event('focus')));
  const link=page.getByRole('link',{name:'Review agreement · Coach <Nina>'});await link.waitFor();
  assert.equal(await link.getAttribute('href'),`/coach-workspace/${clientId}/${coachId}#financial-agreement`);
  assert.equal(await page.locator('[data-pending-agreements-host] nina').count(),0);
  const failed=page.waitForResponse(response=>response.url().includes('/pending-agreements')&&response.status()===503);
  unavailable=true;await page.evaluate(()=>window.dispatchEvent(new Event('focus')));await failed;
  await page.locator('[data-pending-agreements-host][aria-busy="false"]').waitFor({state:'attached'});await link.waitFor();
  unavailable=false;agreements=[];await page.evaluate(()=>window.dispatchEvent(new Event('coach-agreements-changed')));
  await page.waitForFunction(()=>!document.querySelector('[data-pending-agreements-host]').children.length);
  assert.deepEqual(errors,[]);
  console.log('Agreement rule cards and approval controls fit phones; pending notice arrives, links safely to review, survives network errors and clears after response.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
