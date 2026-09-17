# 12WR TEST v1011 - Nutrition Intelligence Phase 1C-A

This phase connects the standalone Nutrition Intelligence module to Web.

- Web references Nutrition Intelligence.
- USDA FoodData Central is registered behind IFoodCompositionProvider.
- The existing program-scoped NutritionFoodLogEnabled preference controls whether food search is shown.
- Existing manual daily totals remain the Engine 3.0 input.
- No app.NutritionDays handoff change is made.
- No Workout Intelligence or Engine 3.0 source is changed.
- FatSecret remains an independent future provider behind the same interface.
- No SQL migration is required in this subphase.
