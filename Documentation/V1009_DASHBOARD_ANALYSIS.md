# 12WR TEST v1009 — Dashboard Analysis

## Purpose

Replace the prior technical three-column Yesterday / Carryover / Today block
with two client-readable sections:

1. Yesterday
2. Trend

Engine 3.0 remains authoritative for physiology. The Dashboard Analysis
Service interprets structured Engine 3.0 outputs together with logged food,
workout, and next-morning scale evidence.

## Hard resistance / hard conditioning

Primary fueling success signal: glycogen restoration.

- Green: >= 80%
- Yellow: 60–79%
- Orange: 50–59%
- Red: < 50%

A modest calorie undershoot can remain favorable when glycogen restoration
and protein are protected.

## Moderate resistance / cardio

Carbohydrate remains useful, but exact hard-day restoration is less critical.

## Zone 2 / easy cardio / recovery

Energy control receives more weight. Lower carbohydrate is not automatically
penalized. Protein remains protected. Extreme under-eating is never green.

## Scale evidence

Yesterday's workout + food are compared with an exact next-morning
measurement.

If today's measurement is absent, the Dashboard asks for it but does not block.

One-day BIA body-fat and muscle changes are hydration-sensitive evidence,
not literal overnight tissue gain/loss.

## Tracking-confidence rule

One odd morning does not prove the nutrition log is wrong.

When logged calories and glycogen restoration are low but scale weight rises
materially, the Dashboard may flag a consistency mismatch while also naming
other plausible causes:

- hydration;
- sodium;
- hard-training inflammation;
- food/gut content;
- ordinary scale noise.

If the mismatch persists, review:

- portions;
- cooking oils;
- sauces and condiments;
- restaurant estimates;
- beverages;
- snacks;
- omitted entries.

Do not recalibrate the Engine from low-confidence logging evidence.

## Signal colors

- Green — good / functionally supported
- Yellow — acceptable but worth watching
- Orange — materially off / investigate
- Red — poor support / likely problem
- Gray — missing / insufficient evidence

## Architecture

Engine 3.0 calculations -> facts

Engine30DashboardAnalysisService -> interpretation

Dashboard -> presentation

The analysis service must not become a second hidden physiology engine.
## Glycogen-associated scale estimate

For explanatory text only, the Dashboard may estimate the scale mass associated
with modeled glycogen restoration using this practical rule of thumb:

- restored glycogen = modeled demand x restoration ratio
- associated water ≈ 3 g per 1 g glycogen
- combined glycogen + associated water = approximately 4 g scale mass per
  1 g restored glycogen

Example:

- 101 g demand
- 99% restoration
- approximately 100 g restored glycogen
- approximately 300 g associated water
- approximately 400 g combined glycogen + water
- approximately 0.9 lb combined scale mass

This is NOT a literal decomposition of the next morning's scale change.

Hard training can also change scale weight through inflammation, sodium,
hydration, food/gut content, and other daily fluid shifts.

A one-day increase is never labeled as equivalent fat or muscle gain.

## Missing current-morning scale

The Yesterday analysis remains visible, but it is explicitly preliminary until
the current morning's scale is entered.

- the missing-data dot remains gray
- the text asking for today's scale is red
- nutrition-log confidence cannot be High while today's morning scale is absent