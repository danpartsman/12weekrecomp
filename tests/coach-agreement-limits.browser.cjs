const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');
(async () => {
    const browser = await chromium.launch({headless:true, executablePath:process.env.NUTRITION_CHROME_PATH});
    try {
        const page = await browser.newPage({viewport:{width:390,height:844}});
        const source = fs.readFileSync('src/WebRecomp.Web/Views/CoachWorkspace/Index.cshtml','utf8');
        const css = source.match(/<style>([\s\S]*?)<\/style>/)[1].replaceAll('@@media','@media');
        const rows = ['g','g','g','kcal'].map((unit,i) => `<tr data-agreement-limit-row data-limit-unit="${unit}">
            <td class="coach-financial-rule"><strong>Nutrition rule ${i}</strong></td>
            <td data-label="Amount ($)"><input name="Amount[${i}]" type="number" value="5"></td>
            <td data-label="Limit type"><select data-limit-mode name="LimitMode[${i}]"><option value="system-percent">% under/over system</option><option value="actual">Actual values (${unit})</option></select></td>
            <td data-limit-lower-cell><input data-limit-lower name="LowerLimit[${i}]" type="number" step=".01" min="0" value="5" required></td>
            <td data-limit-upper-cell><input data-limit-upper name="UpperLimit[${i}]" type="number" step=".01" min="0" value="10" required></td></tr>`).join('');
        await page.setContent(`<html><head><style>${css}</style></head><body><div class="coach-workspace-page"><section class="card"><form><div class="coach-financial-editor"><table class="coach-financial-table"><tbody>${rows}</tbody></table></div></form></section></div></body></html>`);
        await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/coach-agreement-limits.js'});
        const row = page.locator('[data-agreement-limit-row]').first();
        assert.equal(await row.locator('[data-limit-lower-cell]').getAttribute('data-label'),'Under system (%)');
        await row.locator('[data-limit-lower]').fill('101');
        assert.equal(await row.locator('[data-limit-lower]').evaluate(el=>el.checkValidity()),false);
        await row.locator('[data-limit-mode]').selectOption('actual');
        assert.equal(await row.locator('[data-limit-lower-cell]').getAttribute('data-label'),'Minimum (g)');
        await row.locator('[data-limit-lower]').fill('200');
        await row.locator('[data-limit-upper]').fill('150');
        assert.equal(await row.locator('[data-limit-lower]').evaluate(el=>el.checkValidity()),false);
        await row.locator('[data-limit-upper]').fill('220');
        assert.equal(await row.locator('[data-limit-lower]').evaluate(el=>el.checkValidity()),true);
        const calories = page.locator('[data-agreement-limit-row]').last();
        await calories.locator('[data-limit-mode]').selectOption('actual');
        assert.equal(await calories.locator('[data-limit-lower-cell]').getAttribute('data-label'),'Minimum (kcal)');
        const submitted = await page.locator('form').evaluate(form=>Object.fromEntries(new FormData(form)));
        assert.equal(submitted['LimitMode[0]'],'actual');
        assert.equal(submitted['LowerLimit[0]'],'200');
        assert.equal(submitted['UpperLimit[0]'],'220');
        assert.equal(submitted['Amount[0]'],'5');
        for(const width of [320,390,700,1024]) {
            await page.setViewportSize({width,height:844});
            assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);
        }
        console.log('Agreement nutrition modes, units, percent bounds, actual range validation, submitted values and mobile fit passed.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
