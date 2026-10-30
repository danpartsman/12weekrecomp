(() => {
    const calculator=document.querySelector('.simple-fee-calculator');if(!calculator)return;
    const form=calculator.closest('form'),output=calculator.querySelector('[data-fee-calculation]');
    calculator.querySelector('[data-calculate-fee]').addEventListener('click',()=>{
        const quantity=Number(calculator.querySelector('[data-fee-quantity]').value),rate=Number(calculator.querySelector('[data-fee-rate]').value);
        const amount=Math.round((quantity*rate+Number.EPSILON)*100)/100;
        if(!Number.isFinite(amount)||quantity<=0||quantity>1000||rate<=0||rate>10000||amount<=0||amount>10000){output.textContent='Enter a positive quantity and rate, with a total up to $10,000.';return;}
        form.elements.Amount.value=amount.toFixed(2);form.elements.Kind.value='fee';
        if(!form.elements.Description.value.trim())form.elements.Description.value=`${quantity} × $${rate.toFixed(2)}`;
        output.textContent=`Calculated fee: $${amount.toFixed(2)}. Review the description, then record the entry.`;
    });
})();
