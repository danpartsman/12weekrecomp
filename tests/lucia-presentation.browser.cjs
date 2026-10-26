const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
  const root = 'src/WebRecomp.Web/wwwroot';
  const css = ['site.css', 'test-live-preview.css', 'learning-v1015c.css'].map(f => fs.readFileSync(`${root}/css/${f}`, 'utf8')).join('\n');
  const photo = 'data:image/webp;base64,' + fs.readFileSync(`${root}/images/coach/lucia-strength-v1015e-0.webp`).toString('base64');
  const browser = await chromium.launch({headless:true, executablePath:process.env.NUTRITION_CHROME_PATH});
  try {
    const page = await browser.newPage({viewport:{width:1440,height:1000}});
    await page.setContent(`<html data-theme="dark"><head><style>${css}</style></head><body class="app-shell test-live-preview"><section class="learning-coach coach-lucia"><div class="learning-dialog"><div class="learning-dialog-backdrop"></div><article class="learning-dialog-card"><button class="learning-dialog-close">×</button><div class="learning-dialog-coach"><img class="coach-photo" src="${photo}" alt="Lucía"></div><div class="learning-dialog-copy"><div class="learning-kicker">FOOD & FUEL · 16 OF 18</div><h2>Ultra-processed meals can make overeating easier</h2><p>Here's something you can use today. Food quality changes how easy it is to keep portions under control. In a controlled crossover trial, 20 adults ate about 500 more calories daily on an ultra-processed menu and gained about 2 pounds over two weeks; they lost about 2 pounds during the minimally processed phase. Start with one practical swap: replace a snack built around refined starch, added sugar and fat with a meal or snack containing recognizable protein and fiber-rich foods. This tested menus as a whole; it doesn't prove every packaged food or additive is harmful. Keep it practical and keep showing up.</p><p class="learning-takeaway"><strong>Remember:</strong> 20 adults, two weeks per menu; randomized controlled trial.</p><a class="learning-source" href="#">Research behind this tip ↗</a><div class="learning-byline">Lucía · Fun, upbeat fitness coach</div><small>Tap the coach again for another tip.</small></div></article></div></section></body></html>`);
    const image = page.locator('.learning-dialog-coach img');
    await image.evaluate(img => img.decode());
    assert.equal(await image.evaluate(img => img.naturalWidth === img.naturalHeight), true);
    assert.equal(await image.evaluate(img => getComputedStyle(img).objectFit), 'cover');
    assert.equal(await page.locator('.coach-photo-atlas').count(), 0);
    fs.mkdirSync('quantity-control-preview', {recursive:true});
    await page.screenshot({path:'quantity-control-preview/lucia-desktop.png'});
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({path:'quantity-control-preview/lucia-mobile.png'});
    console.log('Lucia portrait uses natural image proportions on desktop and mobile.');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode=1; });
