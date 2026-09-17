# 12WR TEST v1011 - Nutrition Intelligence Architecture

## Module boundary

Nutrition Intelligence is a separate module.

It must not reference:

- `WebRecomp.Web`
- Workout Intelligence
- Engine 3.0 implementation types

The composition root (`WebRecomp.Web`) may reference Nutrition Intelligence later, but Nutrition Intelligence does not reference the web application, Workout Intelligence, or Engine 3.0.

This keeps three independently replaceable components:

1. Workout Intelligence / Workout Builder
2. Engine 3.0 physiology
3. Nutrition Intelligence

## Canonical flow

External food database
→ provider adapter
→ Nutrition Intelligence canonical food/nutrient model
→ Nutrition Intelligence analysis
→ web composition/mapping layer
→ existing Engine 3.0 canonical nutrition inputs

Nutrition Intelligence never calls Engine 3.0 directly.
Engine 3.0 never calls a food database directly.
Workout Intelligence never calls Nutrition Intelligence directly.

## Phase plan

### Phase 1A - provider-neutral foundation

- Standalone `WebRecomp.NutritionIntelligence` project.
- Provider-neutral `IFoodCompositionProvider`.
- USDA FoodData Central adapter.
- Canonical nutrient codes.
- Parsing and mapping tests.
- No database migration.
- No web UI.
- No Engine changes.

### Phase 1B - module-owned persistence

Use a dedicated SQL schema: `nutrition`.

Planned module-owned data:

- food source cache
- user food favorites/recent foods
- custom foods
- recipes
- meal entries
- daily nutrition totals
- provenance / source version metadata

No Workout Intelligence tables are modified.
No Engine 3.0 profile/formula tables are modified.

### Phase 1C - food search and logging UI

- food search
- barcode/GTIN lookup
- serving quantity/unit
- recent foods
- favorites
- custom foods
- recipe builder
- meal/day logging

### Phase 1D - nutrition analysis

- calories and macros
- protein distribution
- fiber
- sodium
- saturated fat
- meal timing
- training-day fueling context
- confidence/provenance indicators

### Phase 1E - Engine handoff

The web composition layer translates finalized Nutrition Intelligence output into existing canonical Engine 3.0 nutrition inputs.

No duplicated physiology calculations are permitted.
