(() => {
 const root = document.querySelector('[data-body-check]'); if (!root) return;
 const checked = root.querySelector('[name="BodyChecked"]');
 function refresh() {
  root.querySelectorAll('[data-region-row]').forEach(row => {
   const s = Number(row.querySelector('[data-soreness]').value), p = Number(row.querySelector('[data-pain]').value);
   row.querySelector('[data-region-label]').textContent = s || p ? `S ${s} · P ${p}` : '';
   root.querySelectorAll(`[data-body-region="${row.dataset.regionRow}"]`).forEach(spot => {
    spot.classList.toggle('is-sore', s > 0); spot.classList.toggle('is-pain', p > 0);
   });
  });
 }
 root.querySelectorAll('[data-body-region]').forEach(spot => spot.addEventListener('click', () => {
  const row = root.querySelector(`[data-region-row="${spot.dataset.bodyRegion}"]`);
  row.open = true; row.scrollIntoView({block:'center',behavior:'smooth'}); row.querySelector('[data-soreness]').focus();
 }));
 root.addEventListener('input', () => { refresh(); });
 root.querySelector('[data-body-clear]').addEventListener('click', () => {
  root.querySelectorAll('input[type="number"]').forEach(x=>x.value='0');
  root.querySelectorAll('input[type="checkbox"]').forEach(x=>x.checked=false);
  if (checked) checked.checked=true;
  const painFlag=document.querySelector('[name="PainFlag"][type="checkbox"]'); if(painFlag) painFlag.checked=false;
  const globalSoreness=document.querySelector('[name="SorenessScore"]'); if(globalSoreness) globalSoreness.value='';
  refresh();
 });
 refresh();
})();
