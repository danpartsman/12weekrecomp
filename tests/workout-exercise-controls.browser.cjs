const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require(process.env.NUTRITION_PLAYWRIGHT_MODULE || 'playwright');

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.NUTRITION_CHROME_PATH });
    try {
        for (const live of [false, true]) {
            const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const card = (id, name) => `<article class="wi-workout-exercise" data-exercise-card data-exercise-id="${id}">
                <input type="hidden" data-field="ExerciseId" value="${id}">
                <input type="hidden" data-field="ExerciseName" value="${name}">
                <input type="hidden" data-field="Mechanics" value="Compound">
                <div class="wi-workout-exercise-name"><strong>${name}</strong></div>
                <button type="button" class="wi-remove-exercise" data-remove-exercise>Remove</button>
            </article>`;
            const css = fs.readFileSync('src/WebRecomp.Web/wwwroot/css/workout-intelligence.css', 'utf8');
            const html = `<html><head><style>${css}</style></head><body>
                <main class="workout-intelligence-page ${live ? 'wi-live-page-minimal' : ''}">
                <form id="wi-detailed-workout-form">
                <div id="wi-exercise-search" data-search-url="/search">
                    <input id="wi-search-input" type="search"><div id="wi-search-status"></div>
                    <div id="wi-search-results" hidden></div>
                </div>
                <div id="wi-exercise-list">${card('squat', 'Squat')}${card('press', 'Press')}${card('row', 'Row')}</div>
                <p id="wi-empty-workout">No exercises</p>
                </form></main></body></html>`;
            let item = { exerciseId: 'curl', name: 'Curl', primaryMuscles: ['Biceps'] };
            await page.route('https://workout.test/**', route => {
                if (/\/search|\/ExchangeExercises/.test(new URL(route.request().url()).pathname)) {
                    return route.fulfill({ contentType: 'application/json', body: JSON.stringify([item]) });
                }
                return route.fulfill({ contentType: 'text/html', body: html });
            });
            await page.goto('https://workout.test/');
            await page.addScriptTag({ path: 'src/WebRecomp.Web/wwwroot/js/detailed-workout.js' });
            const cards = page.locator('[data-exercise-card]');
            const add = page.locator('[data-add-exercise-bottom]');
            assert.deepEqual(errors, [], 'All cards must initialize without JavaScript errors');
            assert.equal(await add.count(), 1);
            for (const width of [320, 390, 820, 1280]) {
                await page.setViewportSize({ width, height: 844 });
                for (let i = 0; i < 3; i++) {
                    const buttons = cards.nth(i).locator('.wi-exercise-action-stack > button');
                    assert.deepEqual(await buttons.allTextContents(), ['Edit Exercise', 'Remove Exercise', '↑', '↓']);
                    const boxes = await buttons.evaluateAll(elements => elements.map(el => {
                        const r = el.getBoundingClientRect();
                        return { top: r.top, left: r.left, right: r.right, height: r.height, clipped: el.scrollWidth > el.clientWidth + 1 };
                    }));
                    assert.ok(boxes.every(box => Math.abs(box.top - boxes[0].top) < 1 && box.height >= 44 && !box.clipped), `One readable action row at ${width}px (live=${live}): ${JSON.stringify(boxes)}`);
                    assert.ok(boxes[0].left >= 0 && boxes.at(-1).right <= width, `Actions fit viewport at ${width}px`);
                }
            }
            assert.equal(await cards.first().locator('[data-move-exercise="up"]').isDisabled(), true);
            assert.equal(await cards.last().locator('[data-move-exercise="down"]').isDisabled(), true);
            await cards.nth(1).locator('[data-move-exercise="up"]').click();
            assert.deepEqual(await cards.evaluateAll(elements => elements.map(el => el.dataset.exerciseId)), ['press', 'squat', 'row']);
            await cards.first().locator('[data-move-exercise="down"]').click();
            assert.deepEqual(await cards.evaluateAll(elements => elements.map(el => el.dataset.exerciseId)), ['squat', 'press', 'row']);
            await add.click();
            await page.locator('#wi-search-input').fill('curl');
            await page.locator('.wi-search-result').click();
            assert.equal(await cards.count(), 4);
            assert.equal(await page.locator('#wi-exercise-search').isVisible(), false);
            assert.equal(await cards.last().locator('.wi-exercise-action-stack > button').count(), 4);
            // Editing any card closes the picker for keyboard selection too.
            item = { exerciseId: 'lunge', name: 'Lunge', primaryMuscles: ['Quads'] };
            await cards.nth(1).locator('[data-exchange-exercise]').click();
            await page.locator('.wi-search-result').focus();
            await page.locator('.wi-search-result').press('Enter');
            assert.equal(await page.locator('#wi-exercise-search').isVisible(), false);
            assert.equal(await cards.nth(1).getAttribute('data-exercise-id'), 'lunge');
            // Add after edit must append a new exercise rather than replace the previous card.
            item = { exerciseId: 'plank', name: 'Plank', primaryMuscles: ['Abs'] };
            await add.click();
            await page.locator('#wi-search-input').fill('plank');
            await page.locator('.wi-search-result').click();
            assert.equal(await cards.count(), 5);
            assert.equal(await cards.last().getAttribute('data-exercise-id'), 'plank');
            await cards.nth(1).locator('[data-remove-exercise]').click();
            assert.equal(await cards.count(), 4);
            assert.deepEqual(await cards.locator('[data-field="ExerciseId"]').evaluateAll(elements => elements.map(el => el.name)), ['Exercises[0].ExerciseId', 'Exercises[1].ExerciseId', 'Exercises[2].ExerciseId', 'Exercises[3].ExerciseId']);
            assert.equal(await add.count(), 1);
            assert.deepEqual(errors, []);
            await page.close();
        }
        console.log('Workout cards: all action rows, mobile sizing, ordering, removal, bottom Add, search dismissal and edit-to-add flow passed.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
