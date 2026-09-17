# Nutrition Intelligence Data Sources

## Primary provider: USDA FoodData Central

Provider code: `USDA_FDC`

12WR uses USDA FoodData Central as the first external food composition provider.

Reasons:

- U.S. government food composition source.
- CC0/public-domain data.
- Foundation Foods for basic/minimally processed foods.
- FNDDS for dietary-study foods.
- Branded Foods for packaged products.
- GTIN/UPC is available for branded products.
- Branded data are updated monthly through the API.
- API supports search and details by FDC ID.
- Downloadable datasets make future local indexing possible.

## API key handling

The USDA data.gov API key is a secret and must not be committed to source control.

Phase 1A reads it from:

`USDA_FDC_API_KEY`

The application will later bind the key from TEST/production configuration or secret storage.

## Provider abstraction

USDA is not hard-wired into Nutrition Intelligence business logic.

All external food sources implement:

`IFoodCompositionProvider`

This permits a future provider to be added or replaced without rebuilding Nutrition Intelligence's domain model.

## Open Food Facts

Open Food Facts is not included in the canonical v1011 baseline.

Its database uses ODbL/share-alike terms. If it is added later, it should be implemented as an isolated provider/cache with explicit provenance and a licensing review before combining or redistributing database-derived records.

## Source provenance

Every imported food retains:

- provider code
- provider food ID
- provider data type
- brand owner when available
- GTIN/UPC when available
- source nutrient IDs/names
- canonical nutrient mapping

Nutrition Intelligence must never silently replace source values with inferred values.
