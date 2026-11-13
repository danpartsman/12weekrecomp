const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
test('retry waits for the deadline and never submits automatically', () => {
    let now = 1000, tick, back = 0;
    const handlers = {};
    const button = { disabled: true }, display = { textContent: '' };
    const form = { addEventListener: (name, handler) => handlers[name] = handler };
    const actions = { dataset: { snapRetrySeconds: '60' }, querySelector: selector =>
        selector === '[data-snap-retry]' ? button : selector === 'form' ? form :
        { addEventListener: (_, handler) => handlers.back = handler } };
    vm.runInNewContext(fs.readFileSync('src/WebRecomp.Web/wwwroot/js/snap-request-help.js', 'utf8'), {
        document: { querySelector: selector => selector === '[data-snap-wait]' ? display : actions },
        Date: { now: () => now }, setInterval: callback => { tick = callback; return 1; }, clearInterval: () => {},
        history: { back: () => back++ }
    });
    assert.equal(display.textContent, '60'); assert.equal(button.disabled, true);
    now += 59000; tick(); assert.equal(display.textContent, '1'); assert.equal(button.disabled, true);
    now += 1000; tick(); assert.equal(display.textContent, '0'); assert.equal(button.disabled, false);
    handlers.submit(); assert.equal(button.disabled, true);
    handlers.back(); assert.equal(back, 1);
});
