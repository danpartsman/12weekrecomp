# 12WR TEST v1011 - Nutrition Intelligence Phase 1C-B

Phase 1C-B converts the Phase 1C-A provider-neutral search foundation into a
working detailed food log.

When `NutritionFoodLogEnabled` is ON, the Nutrition page can:
- search enabled external providers;
- add USDA foods by gram weight;
- create first-party custom foods;
- reuse custom foods by servings;
- log breakfast, lunch, dinner, and snacks;
- remove logged items;
- calculate daily calories, macros, and fiber inside the `nutrition` schema.

The existing `app.NutritionDays` manual-total workflow remains the Engine 3.0
input. Nutrition Intelligence totals are deliberately not handed to Engine 3.0
in this phase.

USDA remains enabled with persistent cache policy.

FatSecret remains represented by the provider-neutral schema but is disabled
and reference-only until the adapter, credentials, API edition, and storage
terms are confirmed.

Recipes and Saved Meals already have Phase 1B tables and are the next UI slice.
