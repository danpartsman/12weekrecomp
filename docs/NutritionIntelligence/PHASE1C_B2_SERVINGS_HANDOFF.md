# 12WR TEST v1011 — Nutrition Intelligence Phase 1C-B2

## Purpose

Phase 1C-B2 corrects the first deployed Nutrition Intelligence food-log slice
before Recipe Builder work begins.

It addresses four connected behaviors:

1. Correct USDA detailed-food macro mapping.
2. Add serving / household measure selection.
3. Add deterministic search sort choices.
4. Establish Food Log -> Daily Entry -> Engine 3.0 handoff with Manual Override.

## USDA nutrient correction

The deployed Phase 1C-B parser correctly handled the abridged USDA search
nutrient shape and the full nested nutrient shape, but some Branded Food detail
responses contained `foodNutrients` rows with only a food-nutrient row ID and
an amount. Those rows did not contain the nutrient identity needed to map
energy/protein/carbohydrate/fat into 12WR canonical nutrient codes.

For Branded Food details, Phase 1C-B2 falls back to USDA `labelNutrients` when
the detailed `foodNutrients` collection does not contain the complete core
macro set. Label values are treated on the branded serving-size basis supplied
by USDA.

Generic Foundation/FNDDS/SR foods continue to use USDA food nutrients on the
100 g basis when their nutrient metadata are available.

## Serving and measure selection

Search remains lightweight.

A user first searches and then selects a food. Only the selected food is
hydrated through the USDA detail endpoint.

Where USDA provides food portions with `gramWeight`, 12WR offers those
household portions. Examples can include cup, tablespoon, slice, piece, or
other USDA-defined portions.

For gram-basis foods, grams and weight ounces remain available.

For milliliter-basis branded label foods, milliliters and fluid ounces are
available. Manufacturer serving size is shown when the serving unit matches
the nutrient basis.

No density conversion is invented. A volume measure is not silently converted
to grams unless USDA provides the food-specific gram weight.

## Search sorting

The provider-neutral UI supports:

- BEST_MATCH
- GENERIC_FIRST
- BRANDED_FIRST
- NAME_ASC
- NAME_DESC

BEST_MATCH preserves provider order.

GENERIC_FIRST ranks Foundation, FNDDS/Survey, SR Legacy, other, then Branded.

## Daily Entry handoff

`app.NutritionDays` remains the canonical Engine-facing nutrition input.

When Food Log has detailed totals:

- if no Daily Entry nutrition row exists, Food Log creates/synchronizes it;
- if the date is already Food Log sourced, Food Log updates it;
- if the date contains existing manual calories/macros, those values are
  preserved;
- if the date is in MANUAL_OVERRIDE, later Food Log changes do not overwrite it.

Daily Entry contains the source toggle.

### Food Log mode

Food Log totals populate:

- calories
- protein
- carbs
- fat
- fiber

The macro fields are read-only in the Daily Entry UI.

Water and notes remain Daily Entry fields.

### Manual Override mode

Manual Override makes the macro fields editable.

Manual values replace the calculated Food Log values in `app.NutritionDays`.

Food Log may continue calculating detailed totals independently, but it does
not overwrite the Engine-facing row while Manual Override is active.

Turning Manual Override off copies the current Food Log totals back into
`app.NutritionDays` and returns the source to `FOOD_LOG`.

### No Food Log used

The original manual Daily Entry workflow remains available. No detailed food
logging is required.

## Module boundaries

Nutrition Intelligence still does not call Engine 3.0.

The handoff is owned by the Web composition layer:

Nutrition Intelligence detailed log
-> nutrition.DailyTotals
-> Web handoff
-> app.NutritionDays
-> existing Engine 3.0 input path

Workout Intelligence is unchanged.

## Database

No schema migration is required for Phase 1C-B2.

Existing fields are sufficient:

- `entry_source_code`
- `source_record_id`
- calorie/macro/fiber columns

Source codes used by this workflow:

- `MANUAL`
- `FOOD_LOG`
- `MANUAL_OVERRIDE`

## Deferred

Recipe Builder and Saved Meals remain the next feature slice after this
corrective workflow is validated in TEST.
