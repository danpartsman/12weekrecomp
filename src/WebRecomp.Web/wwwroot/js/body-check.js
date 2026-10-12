(() => {
 const root = document.querySelector('[data-body-check]'); if (!root) return;
 const checked = root.querySelector('[name="BodyChecked"]');
 function refresh() {
  const summary=[];
  root.querySelectorAll('[data-body-region]').forEach(spot => {
   const state=Number(root.querySelector(`[data-region-mark="${spot.dataset.bodyRegion}"]`).value);
   const label=state===1?'sore':state===2?'pain / injury':'clear';
   spot.classList.toggle('is-sore',state===1); spot.classList.toggle('is-pain',state===2);
   spot.textContent=state?'×':'□';
   spot.setAttribute('aria-label',`${spot.dataset.regionName}: ${label}`);
   spot.setAttribute('aria-pressed',String(state>0));
   if(state) summary.push(`${spot.dataset.regionName}: ${label}`);
  });
  root.querySelector('[data-mark-summary]').textContent=summary.join(' · ') || 'No regions marked.';
 }
 root.querySelectorAll('[data-body-region]').forEach(spot => spot.addEventListener('click', () => {
  const mark=root.querySelector(`[data-region-mark="${spot.dataset.bodyRegion}"]`);
  mark.value=String((Number(mark.value)+1)%3); if(checked) checked.checked=true; refresh();
 }));
 root.querySelector('[data-body-clear]').addEventListener('click', () => {
  root.querySelectorAll('[data-region-mark]').forEach(x=>x.value='0');
  root.querySelector('[name="KeepPain"][type="checkbox"]').checked=false;
  if(checked) checked.checked=true; refresh();
 });
 refresh();
})();
