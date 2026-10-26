const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');

(async () => {
    const web = 'src/WebRecomp.Web';
    const view = fs.readFileSync(`${web}/Views/Nutrition/Index.cshtml`, 'utf8');
    const amount = view.match(/<span class="nutrition-food-edit-amount"[^>]*>[\s\S]*?<\/span>\s*<\/span>/)[0]
        .replace('@item.Quantity.ToString("0.####", CultureInfo.InvariantCulture)', '1')
        .replace('@item.QuantityUnitCode', 'serving');
    const viewStyles = view.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll('@@media', '@media');
    const css = ['site.css', 'test-live-preview.css'].map(file => fs.readFileSync(`${web}/wwwroot/css/${file}`, 'utf8')).join('\n') + viewStyles;
    const executablePath = process.env.NUTRITION_CHROME_PATH;
    const browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
    try {
        const page = await browser.newPage({ viewport: { width: 1200, height: 650 } });
        await page.setContent(`<html data-theme="dark"><head><style>${css}</style></head><body class="app-shell test-live-preview"><main class="container"><h1>Food Log</h1><h2>Dinner</h2><section class="card"><h3>Boneless Skinless Chicken Breast</h3><p>1 serving | 131 kcal | P 25.0 | C 0.0 | F 3.0</p><form class="nutrition-food-edit-form"><label>Meal<select><option>Dinner</option></select></label><label><span>Amount</span>${amount}</label><button type="submit" class="primary">Save</button></form></section></main></body></html>`);
        await page.addScriptTag({ path: `${web}/wwwroot/js/nutrition-quantity-stepper.js` });
        const input = page.locator('[data-quantity-stepper] input');
        assert.equal(await page.locator('[data-quantity-stepper] button').count(), 0, 'No detached arrow buttons');
        assert.equal(await input.evaluate(el => getComputedStyle(el).appearance), 'auto', 'Native control appearance');
        await input.press('ArrowUp');
        assert.equal(await input.inputValue(), '2');
        await input.press('ArrowDown');
        assert.equal(await input.inputValue(), '1');
        const rect = await input.boundingBox();
        await page.mouse.click(rect.x + rect.width - 18, rect.y + rect.height * .25);
        assert.equal(await input.inputValue(), '2', 'Native up-arrow click moves by one');
        await page.mouse.click(rect.x + rect.width - 18, rect.y + rect.height * .75);
        assert.equal(await input.inputValue(), '1', 'Native down-arrow click moves by one');
        await input.fill('0.5');
        await input.press('ArrowUp');
        assert.equal(await input.inputValue(), '1.5');
        assert.equal(await input.evaluate(el => el.checkValidity()), true, 'Fractional quantities remain valid');
        await input.fill('0.01');
        await input.press('ArrowDown');
        assert.equal(await input.inputValue(), '0.01');
        await input.fill('1000');
        await input.press('ArrowUp');
        assert.equal(await input.inputValue(), '1000');
        await input.fill('1');
        await input.focus();
        fs.mkdirSync('quantity-control-preview', { recursive: true });
        await page.screenshot({ path: 'quantity-control-preview/desktop.png' });
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Mobile page fits');
        await page.screenshot({ path: 'quantity-control-preview/mobile.png' });
        console.log('Native quantity controls: mouse, keyboard, decimals, bounds and mobile fit passed.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
