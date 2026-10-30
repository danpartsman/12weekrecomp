const assert=require('node:assert/strict'),fs=require('node:fs');
module.exports=async browser=>{
 const page=await browser.newPage({viewport:{width:1280,height:960}});
 try{
 const css=['site.css','coach-center.css','simple-coaching.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
 await page.setContent(`<html data-theme="dark"><style>${css}</style><body class="app-shell"><main class="container"><details class="simple-workout" open><summary>Snap workout</summary><div class="snap-charge-summary"><span>Required completion time<strong>Oct 30, 12:00 PM</strong></span><span class="snap-countdown-card">Time remaining<strong>1h 20m</strong></span></div><form data-snap-results data-can-record="true" data-finished="false"><section class="snap-result-exercise"><h3 class="snap-exercise-title">Push ups</h3><p>3 × 10 reps · Coach RPE: 8</p><div class="simple-table-scroll"><table><thead><tr><th>Set</th><th>Completed reps</th><th>Weight used (lb)</th></tr></thead><tbody><tr><th>1</th><td><input value="20"></td><td><input value="0"></td></tr></tbody></table></div><label class="snap-rpe">Actual RPE (optional)<input value="8"></label></section><div class="snap-result-actions"><button class="button" type="button">Save progress</button><button class="button" type="button">Finish workout</button></div><p data-snap-wake-status></p><button class="button" data-snap-wake-retry hidden>Keep screen awake</button></form></details></main></body></html>`);
 await page.evaluate(()=>{
  window.__locks=[];Object.defineProperty(navigator,'wakeLock',{value:{request:async()=>{const lock={released:false,listeners:[],addEventListener(_,fn){this.listeners.push(fn)},async release(){this.released=true;this.listeners.forEach(fn=>fn())}};window.__locks.push(lock);return lock;}},configurable:true});
 });
 await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/snap-wake-lock.js'});
 await page.waitForFunction(()=>window.__locks.length===1);
 const metrics=await page.evaluate(()=>{
  const rect=s=>document.querySelector(s).getBoundingClientRect();const deadline=rect('.snap-charge-summary>span'),timer=rect('.snap-countdown-card');return{sideBySide:timer.left>deadline.left&&timer.top===deadline.top,full:rect('.simple-table-scroll').width>=rect('.snap-result-exercise').width-44,center:getComputedStyle(document.querySelector('td input')).textAlign,title:parseFloat(getComputedStyle(document.querySelector('h3')).fontSize),button:rect('.snap-result-actions button').height,rpeBelow:rect('.snap-rpe').top>rect('.simple-table-scroll').top};
 });
 assert.equal(metrics.sideBySide,true);assert.equal(metrics.full,true);assert.equal(metrics.center,'center');assert.ok(metrics.title>=24);assert.ok(metrics.button>=44);assert.equal(metrics.rpeBelow,true);
 await page.evaluate(()=>document.querySelector('details').open=false);await page.waitForFunction(()=>window.__locks[0].released);
 await page.evaluate(()=>document.querySelector('details').open=true);await page.waitForFunction(()=>window.__locks.length===2);
 await page.evaluate(()=>document.querySelector('form').dataset.finished='true');await page.waitForFunction(()=>window.__locks[1].released);
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 fs.mkdirSync('quantity-control-preview',{recursive:true});await page.screenshot({path:'quantity-control-preview/snap-client-mobile.png',fullPage:true});await page.setViewportSize({width:1280,height:960});await page.screenshot({path:'quantity-control-preview/snap-client-desktop.png',fullPage:true});
 console.log('Snap client desktop/mobile layout, centered fields, full-size buttons and wake-lock acquire/release checks passed.');
 }finally{await page.close();}
};
