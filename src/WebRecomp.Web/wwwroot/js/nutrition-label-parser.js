((root)=>{
    function parse(text){
        const normalized=String(text||'').replace(/\r/g,'').replace(/[–—]/g,'-');
        function number(pattern){const m=normalized.match(pattern);return m?Number(m[1]):null;}
        const serving=normalized.match(/serving\s*size\s*[:\-]?\s*([^\n]+)/i)?.[1]?.trim()||'';
        const size=serving.match(/(?:\(|\b)(\d+(?:\.\d+)?)\s*(g|ml)\b/i);
        return {energyKcal:number(/(?:^|\n)\s*calories\s*[:\-]?\s*(\d+(?:\.\d+)?)(?!\s*%)/i),
            proteinG:number(/(?:^|\n)\s*protein\s*[:\-]?\s*(\d+(?:\.\d+)?)\s*g\b/i),
            carbohydrateG:number(/(?:^|\n)\s*total\s+carbohydrates?\s*[:\-]?\s*(\d+(?:\.\d+)?)\s*g\b/i),
            fatG:number(/(?:^|\n)\s*total\s+fat\s*[:\-]?\s*(\d+(?:\.\d+)?)\s*g\b/i),
            fiberG:number(/(?:^|\n)\s*dietary\s+fib(?:er|re)\s*[:\-]?\s*(\d+(?:\.\d+)?)\s*g\b/i),
            servingSize:size?Number(size[1]):1,servingUnitCode:size?size[2].toLowerCase():'serving',servingText:serving};
    }
    root.NutritionLabelParser={parse};if(typeof module!=='undefined')module.exports={parse};
})(typeof window==='undefined'?globalThis:window);
