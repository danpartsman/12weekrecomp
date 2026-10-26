const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
  const browser = await chromium.launch({headless:true, executablePath:process.env.NUTRITION_CHROME_PATH});
  try {
    const page = await browser.newPage({viewport:{width:1100,height:800}});
    const css=['site.css','test-live-preview.css'].map(f=>fs.readFileSync('src/WebRecomp.Web/wwwroot/css/'+f,'utf8')).join('\n');
    await page.setContent(`<html data-theme="dark"><head><style>${css}[data-notification-settings] [hidden]{display:none!important}</style></head><body class="app-shell test-live-preview"><main class="container"><h1>Notifications</h1><section class="card" data-notification-settings><input type="hidden" name="__RequestVerificationToken" value="test-token"><h2>Phone notifications</h2><p>Enable this on the phone you want to receive alerts.</p><div style="display:flex;gap:12px;flex-wrap:wrap"><button class="primary" data-enable-phone>Enable phone notifications</button><button class="button" data-test-phone hidden>Send a test notification</button><button class="button" data-disable-phone hidden>Disable on this device</button></div><p data-phone-status role="status"></p></section></main></body></html>`);
    await page.evaluate(() => {
      window.__requests=[]; window.__permissions=0;
      const subscription={endpoint:'https://fcm.googleapis.com/wp/test',toJSON(){return {endpoint:this.endpoint,keys:{p256dh:'key',auth:'auth'}}},async unsubscribe(){window.__unsubscribed=true;}};
      Object.defineProperty(window,'Notification',{configurable:true,value:{async requestPermission(){window.__permissions++;return 'granted';}}});
      window.PushManager=class {};
      Object.defineProperty(navigator,'serviceWorker',{configurable:true,value:{async register(){return {pushManager:{async getSubscription(){return null},async subscribe(){return subscription}}}},ready:Promise.resolve()}});
      window.fetch=async (url,options)=>{window.__requests.push({url,options});return {ok:true,json:async()=>({publicKey:'AA',endpoints:[]}),text:async()=>''};};
    });
    await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/coach-notifications.js'});
    await page.waitForFunction(()=>!document.querySelector('[data-enable-phone]').disabled);
    assert.equal(await page.evaluate(()=>window.__permissions),0,'Never ask for permission on page load');
    assert.equal(await page.locator('[data-test-phone]').isVisible(),false);
    await page.locator('[data-enable-phone]').click();
    await page.waitForFunction(()=>document.querySelector('[data-enable-phone]').hidden);
    assert.equal(await page.evaluate(()=>window.__permissions),1);
    const subscribe=await page.evaluate(()=>window.__requests.find(r=>r.url.endsWith('/subscribe')));
    assert.equal(subscribe.options.headers.RequestVerificationToken,'test-token');
    assert.equal(JSON.parse(subscribe.options.body).endpoint,'https://fcm.googleapis.com/wp/test');
    await page.locator('[data-test-phone]').click();
    await page.waitForFunction(()=>document.querySelector('[data-phone-status]').textContent.includes('accepted'));
    fs.mkdirSync('quantity-control-preview',{recursive:true});
    await page.screenshot({path:'quantity-control-preview/notifications-desktop.png'});
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
    await page.screenshot({path:'quantity-control-preview/notifications-mobile.png'});
    await page.locator('[data-disable-phone]').click();
    await page.waitForFunction(()=>window.__unsubscribed===true);
    assert.equal(await page.locator('[data-enable-phone]').isVisible(),true);
    console.log('Phone notification opt-in, CSRF protection, test and device unsubscribe passed.');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
