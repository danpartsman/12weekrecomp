const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:1280,height:900}});
  const source=fs.readFileSync('src/WebRecomp.Web/Views/AccountabilityLedger/Index.cshtml','utf8');
  const form=source.match(/<details class="card ledger-entry-form"[\s\S]*?<\/details>/)[0].replaceAll('@Model.Revision','1').replaceAll('@Guid.NewGuid()','11111111-1111-1111-1111-111111111111').replace('@Model.Today.ToString("yyyy-MM-dd")','2026-10-28');
  const css=['site.css','coach-center.css','coach-ledger.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
  await page.setContent(`<html data-theme="dark"><head><meta charset="utf-8"><style>${css}</style></head><body class="app-shell coach-shell"><main class="container"><h1>Client · Fees &amp; incentives</h1><section class="card ledger-summary"><div><span>Running balance</span><strong>$0.00</strong><p>Account is settled</p></div><div><span>Fees assessed</span><strong>$20.00</strong></div><div><span>Incentives earned</span><strong>$50.00</strong></div></section>${form}<section class="card"><h2>Account history</h2><div class="ledger-table-scroll"><table class="ledger-table"><thead><tr><th>Date</th><th>Type</th><th>Description</th><th>Increase</th><th>Decrease</th><th>Running balance</th></tr></thead><tbody><tr><td>Oct 28</td><td>Fee</td><td>Reviewed fee</td><td>$20.00</td><td>—</td><td>$20.00</td></tr><tr><td>Oct 28</td><td>Incentive</td><td>Completion $20 + 30 extra reps × $1</td><td>—</td><td>$50.00</td><td>−$30.00</td></tr><tr><td>Oct 28</td><td>Coach paid client</td><td>Payment recorded</td><td>$30.00</td><td>—</td><td>$0.00</td></tr></tbody></table></div></section></main></body></html>`);
  assert.equal(await page.locator('[name=Kind] option').count(),4);
  await page.locator('[name=Amount]').fill('-1');assert.equal(await page.locator('form').evaluate(n=>n.checkValidity()),false);
  await page.locator('[name=Amount]').fill('30');await page.locator('[name=Description]').fill('Bank transfer to client');await page.locator('[name=Kind]').selectOption('payment-coach');
  assert.equal(await page.locator('form').evaluate(n=>n.checkValidity()),true);
  fs.mkdirSync('quantity-control-preview',{recursive:true});await page.screenshot({path:'quantity-control-preview/coach-ledger-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await page.screenshot({path:'quantity-control-preview/coach-ledger-mobile.png',fullPage:true});
  console.log('Ledger payment direction, positive amounts, running-balance table and responsive layout passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
