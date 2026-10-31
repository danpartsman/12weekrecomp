const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  const points=Array.from({length:42},(_,i)=>({date:new Date(Date.UTC(2026,8,20+i)).toISOString().slice(0,10),raw:i===18?null:55+Math.sin(i),estimate:55+i*.01,lower:54.5+i*.01,upper:55.5+i*.01,weeklySlope:.07,residual:i===18?null:Math.sin(i),downweighted:i===12,daysSinceReading:i===18?1:0,readings:i+1,average3:55+i*.02,average7:55+i*.01,change3:.1,change7:.1}));
  const report={version:'test',fingerprint:'abc123456789',start:points[0].date,end:points.at(-1).date,sensitivity:'balanced',days:points.map(p=>({date:p.date,completedDay:true,workouts:[],weight:210,calories:1900})),states:['fat','muscle','weight','water'].map(key=>({key,label:key,points}))};
  const css=['site.css','smart-intelligence.css'].map(x=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+x,'utf8')).join('\n');
  const script=fs.readFileSync('src/WebRecomp.Web/wwwroot/js/smart-intelligence.js','utf8');
  const html=`<html data-theme="dark"><head><style>${css}</style></head><body><main class="container"><div class="si" id="smart-intelligence" data-ai-ready="true"><h1>Smart Intelligence</h1><div id="si-stats" class="si-stats"></div><label><input type="checkbox" id="si-averages">Compare averages</label><div id="si-charts" class="si-chart-grid"></div><div id="si-daily" class="si-table-wrap"></div><div id="si-workouts"></div><button id="si-export">Export</button><form id="si-ai-form"><input name="__RequestVerificationToken" value="test-token"><textarea id="si-context"></textarea><input id="si-include-notes" type="checkbox"><button id="si-analyze">Analyze</button></form><p id="si-ai-status"></p><div id="si-ai-result"></div><script type="application/json" id="si-data">${JSON.stringify(report)}</script></div></main><script>${script}</script></body></html>`;
  await page.route('https://smart.test/**',route=>route.fulfill({contentType:'text/html',body:html}));
  await page.goto('https://smart.test/');assert.equal(await page.locator('.si-stat').count(),4);assert.equal(await page.locator('.si-chart svg').count(),4);assert.equal(await page.locator('.si-raw').count(),164);
  assert.equal(await page.locator('.si-chart svg path').count(),8);await page.locator('#si-averages').check();assert.equal(await page.locator('.si-chart svg path').count(),16);
  await page.evaluate(()=>{window.fetch=async(url,options)=>{window.analysisRequest=JSON.parse(options.body);return {ok:true,json:async()=>({summary:'<img src=x onerror=alert(1)>',confidence:'Limited',findings:[{title:'Review',evidence:'Jan 1',hypothesis:'Unknown',nextStep:'Compare'}],limitations:['Model uncertainty'],provider:'openai',model:'test',fingerprint:'abc123456789',generatedAtUtc:'2026-10-31T00:00:00Z'})};};});
  await page.locator('#si-context').fill('Felt tired');await page.locator('#si-analyze').click();await page.locator('#si-ai-result h3').first().waitFor();
  assert.equal(await page.locator('#si-ai-result img').count(),0);assert.match(await page.locator('#si-ai-result').innerText(),/<img/);
  assert.deepEqual(await page.evaluate(()=>window.analysisRequest),{days:42,sensitivity:'balanced',provider:'openai',context:'Felt tired',includeNotes:false});
  for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);}
  for(const theme of ['light','dark']){await page.evaluate(t=>document.documentElement.dataset.theme=t,theme);const palette=await page.locator('.si-stat').first().evaluate(n=>({bg:getComputedStyle(n).backgroundColor,ink:getComputedStyle(n.querySelector('strong')).color}));assert.equal(palette.bg,'rgb(16, 33, 48)');assert.equal(palette.ink,'rgb(230, 237, 244)');}
  assert.deepEqual(errors,[]);console.log('Smart Intelligence four charts, missing readings, averaging comparison, responsive table, structured analysis request and safe output rendering passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
