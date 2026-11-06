// Exercise real barcode pixels and real video playback; only camera hardware and product lookup are substituted.
const assert=require('node:assert/strict'),fs=require('node:fs');
const {chromium}=require(process.env.NUTRITION_PLAYWRIGHT_MODULE||'playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.NUTRITION_CHROME_PATH});
 try{
  const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[],codes=[];
  page.on('pageerror',error=>errors.push(error.message));
  const scanner=fs.readFileSync('src/WebRecomp.Web/Views/Nutrition/_FoodScanner.cshtml','utf8').replace(/<link[^>]*>|<script[\s\S]*?<\/script>/g,'').replace(/data-barcode-url="[\s\S]*?">/,'data-barcode-url="/nutrition/scan/barcode">');
  const css=fs.readFileSync('src/WebRecomp.Web/wwwroot/css/nutrition-scanner.css','utf8');
  await page.route('https://scanner.test/**',route=>{
   const url=new URL(route.request().url());
   if(url.pathname.includes('/lib/zxing/'))return route.fulfill({contentType:'application/javascript',body:fs.readFileSync('src/WebRecomp.Web/wwwroot/lib/zxing/zxing-browser-0.1.5.min.js','utf8')});
   if(url.pathname.includes('/barcode')){codes.push(url.searchParams.get('code'));return route.fulfill({contentType:'application/json',body:JSON.stringify({scanSource:'USDA',description:'Video-frame food'})});}
   return route.fulfill({contentType:'text/html',body:`<html><head><style>${css}</style></head><body><dialog id="nutrition-food-picker-dialog"><button data-food-scan="barcode">Scan barcode</button><select data-picker-meal><option>LUNCH</option></select></dialog>${scanner}</body></html>`});
  });
  await page.goto('https://scanner.test/');
  await page.addScriptTag({path:'src/WebRecomp.Web/wwwroot/js/nutrition-scanner.js'});
  await page.evaluate(()=>{
   window.completedScans=0;window.cameraRequests=0;window.stoppedTracks=0;
   const picker=document.querySelector('#nutrition-food-picker-dialog');picker.showModal();
   picker.addEventListener('nutrition-barcode-selected',event=>{if(event.detail.autoAdd)window.completedScans++;});
   Object.defineProperty(navigator.mediaDevices,'getUserMedia',{configurable:true,value:async()=>{
    window.cameraRequests++;
    const canvas=document.createElement('canvas');canvas.width=1280;canvas.height=720;const ctx=canvas.getContext('2d');
    const L=['0001101','0011001','0010011','0111101','0100011','0110001','0101111','0111011','0110111','0001011'];
    const parity=['LLLLLL','LLGLGG','LLGGLG','LLGGGL','LGLLGG','LGGLLG','LGGGLL','LGLGLG','LGLGGL','LGGLGL'];
    const raw=window.testBarcode.length===12?'0'+window.testBarcode:window.testBarcode;
    const invert=bits=>bits.replace(/[01]/g,bit=>bit==='0'?'1':'0');
    let bits='101';for(let i=1;i<=6;i++){const l=L[Number(raw[i])];bits+=parity[Number(raw[0])][i-1]==='L'?l:invert([...l].reverse().join(''));}
    bits+='01010';for(let i=7;i<13;i++)bits+=invert(L[Number(raw[i])]);bits+='101';
    const draw=()=>{
     ctx.fillStyle='#eee';ctx.fillRect(0,0,1280,720);ctx.save();ctx.translate(640,360);ctx.rotate(window.testRotation*Math.PI/180);
     ctx.fillStyle='#fff';ctx.fillRect(-190,-90,380,180);ctx.fillStyle='#222';
     [...bits].forEach((bit,i)=>{if(bit==='1')ctx.fillRect(-142.5+i*3,-70,3,140);});ctx.restore();
    };
    draw();const interval=setInterval(draw,50),stream=canvas.captureStream(20);
    for(const track of stream.getTracks()){const original=track.stop.bind(track);track.stop=()=>{window.stoppedTracks++;clearInterval(interval);original();};}
    return stream;
   }});
  });
  const cases=[{code:'4006381333931',rotation:0,native:'none'},{code:'012345678905',rotation:90,native:'error'},{code:'5901234123457',rotation:0,native:'stalled'}];
  for(let i=0;i<cases.length;i++){
   await page.evaluate(test=>{
    window.testBarcode=test.code;window.testRotation=test.rotation;
    if(test.native==='none')delete window.BarcodeDetector;
    else window.BarcodeDetector=class{static async getSupportedFormats(){return ['ean_13','upc_a'];}detect(){return test.native==='stalled'?new Promise(()=>{}):Promise.reject(new Error('Native detector unavailable'));}};
   },cases[i]);
   await page.getByRole('button',{name:'Scan barcode',exact:true}).click();
   await page.waitForFunction(count=>window.completedScans===count,i+1,{timeout:15000});
   assert.equal(codes.at(-1),cases[i].code);
   assert.equal(codes.length,i+1,'Exactly one lookup per scan');
   assert.equal(await page.locator('#nutrition-scanner').evaluate(el=>el.open),false);
   await page.waitForFunction(()=>document.querySelector('[data-scan-video]').srcObject===null);
  }
  assert.equal(await page.evaluate(()=>window.cameraRequests),3);
  assert.ok(await page.evaluate(()=>window.stoppedTracks>=3));
  assert.deepEqual(errors,[]);
  console.log('Real live EAN/UPC video-frame decoding, rotated labels, native-error and stalled-native fallback, one-shot lookup, camera release and repeated openings passed.');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
