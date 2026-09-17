# 12WR TEST v1011 - Nutrition Intelligence Phase 1B

## Status

Phase 1B establishes the module-owned persistence boundary.

The Nutrition Intelligence module remains separate from:

- Workout Intelligence / Workout Builder
- Engine 3.0
- Web UI implementation

The only cross-schema ownership relationship is `client_id -> app.Clients(client_id)`.
There are no Nutrition Intelligence foreign keys to Workout Intelligence or Engine tables.

## SQL ownership

Dedicated schema:

`nutrition`

Tables:

1. `Providers`
2. `ExternalFoods`
3. `ExternalFoodNutrients`
4. `CustomFoods`
5. `CustomFoodNutrients`
6. `Recipes`
7. `RecipeItems`
8. `SavedMeals`
9. `SavedMealItems`
10. `FoodPreferences`
11. `DailyLogs`
12. `DailyLogItems`
13. `DailyTotals`

All primary identifiers use `uniqueidentifier`, matching the existing application convention.
Client ownership uses `app.Clients.client_id`.
Module-owned mutable aggregate roots use `rowversion` where appropriate.
UTC audit timestamps use `datetime2(3)`.

## Provider policy

### USDA FoodData Central

Provider code:

`USDA_FDC`

Initial policy:

- cache policy: `PERSISTENT`
- enabled: yes
- license code: `CC0`

The provider adapter may persist normalized food records, nutrient records, and source payload/provenance.

### FatSecret

Provider code:

`FATSECRET`

Initial conservative policy:

- cache policy: `REFERENCE_ONLY`
- enabled: no
- terms review required: yes

This is deliberate. FatSecret is represented in the schema from day one, but the module will not persist provider payloads or nutrient cache rows until the selected API edition and its storage/caching terms have been reviewed and configured.

The FatSecret adapter can be added later without changing the schema.

## First-party 12WR data

The following are owned by 12WR and are not dependent on any external food provider:

- custom foods
- custom food nutrients
- recipes
- saved meals
- favorites
- recent-food usage metadata
- daily logs
- daily totals

A recipe can contain external foods and custom foods.

A saved meal can contain:

- an external food
- a custom food
- a recipe

Daily log items can contain:

- an external food
- a custom food
- a recipe

Saved meals are expanded into log items, while `source_saved_meal_id` preserves provenance.

## Durable history

Daily log items contain description and nutrition snapshot fields.

Snapshot persistence is policy-aware:

- `PERSISTED`: provider terms permit a durable nutritional snapshot.
- `REFERENCE_ONLY`: provider reference is durable but restricted source nutrition is not persisted.
- `USER_ENTERED`: first-party custom food/recipe data.

This preserves the module boundary without silently violating provider storage policy.

## Engine 3.0 handoff

Phase 1B does not alter `app.NutritionDays` and does not call Engine 3.0.

A later composition-layer adapter will translate finalized `nutrition.DailyTotals` into the existing canonical daily nutrition input expected by Engine 3.0.

This prevents Nutrition Intelligence from becoming coupled to physiology implementation.
