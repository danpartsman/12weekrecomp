const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');

test('Snap countdown freezes during protected time and resumes using available seconds',async()=>{
    const output={textContent:''},callbacks=[];
    const timer={dataset:{serverNow:'2026-11-12T10:00:00Z',workoutId:'snap',deadline:'2026-11-12T14:00:00Z',finished:'false',remainingSeconds:'7200',pausedUntil:'2026-11-12T12:00:00Z',availableFrom:'2026-11-12T12:00:00Z',pauseReason:'Work'},querySelector:()=>output};
    let ticks=0;
    const status={nowUtc:timer.dataset.serverNow,workouts:[{id:'snap',deadlineUtc:timer.dataset.deadline,timing:{remainingSeconds:7200,pausedUntilUtc:timer.dataset.pausedUntil,availableFromUtc:timer.dataset.availableFrom,reason:'Work'},finished:false}]};
    const context={document:{hidden:false,querySelectorAll:()=>[timer],querySelector:()=>null,getElementById:()=>null,addEventListener(){}},window:{addEventListener(){}},location:{pathname:'/clients/test/snap',search:''},performance:{now:()=>ticks},setInterval:(fn,delay)=>callbacks.push({fn,delay}),fetch:async()=>({ok:true,headers:{get:()=> 'application/json'},json:async()=>status}),Intl,Date,Math,Number};
    vm.runInNewContext(fs.readFileSync('src/WebRecomp.Web/wwwroot/js/snap-countdown.js','utf8'),context);
    await new Promise(setImmediate);
    assert.match(output.textContent,/Paused · Work · 2h 0m 0s/);
    ticks=60*60*1000;callbacks.find(c=>c.delay===1000).fn();
    assert.match(output.textContent,/Paused · Work · 2h 0m 0s/);
    status.nowUtc='2026-11-12T12:00:00Z';status.workouts[0].timing={remainingSeconds:7200,availableFromUtc:'2026-11-12T12:00:00Z'};
    await callbacks.find(c=>c.delay===30000).fn();
    ticks+=30000;callbacks.find(c=>c.delay===1000).fn();
    assert.match(output.textContent,/1h 59m 30s available time remaining/);
    status.nowUtc='2026-11-12T14:00:01Z';status.workouts[0].timing={remainingSeconds:0};
    await callbacks.find(c=>c.delay===30000).fn();
    assert.match(output.textContent,/Time expired/);
});
