const {test}=require('node:test'),assert=require('node:assert/strict');
const {parse}=require('../src/WebRecomp.Web/wwwroot/js/nutrition-label-parser.js');
test('US label reads totals, serving mass and decimal values, not saturated fat or daily percentages',()=>{
 const result=parse('Nutrition Facts\nServing size 1 scoop (33 g)\nCalories 130\nTotal Fat 2.5g 3%\nSaturated Fat 1.5g 8%\nTotal Carbohydrate 3g 1%\nDietary Fiber 0g 0%\nProtein 25g');
 assert.deepEqual(result,{energyKcal:130,fatG:2.5,carbohydrateG:3,proteinG:25,fiberG:0,servingSize:33,servingUnitCode:'g',servingText:'1 scoop (33 g)'});
});
test('missing or less-than amounts stay blank rather than becoming invented zeros',()=>{
 const result=parse('Calories 100\nProtein <1g\nSaturated Fat 2g\nTotal Carbohydrate 20g');
 assert.equal(result.proteinG,null);assert.equal(result.fatG,null);assert.equal(result.energyKcal,100);
});
test('liquid serving basis stays in ml',()=>{const result=parse('Serving size 1 cup (240 ml)\nCalories 90\nTotal Fat 0g\nTotal Carbohydrate 12g\nProtein 8g');assert.equal(result.servingSize,240);assert.equal(result.servingUnitCode,'ml');});
