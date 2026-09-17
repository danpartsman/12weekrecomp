# 12WR TEST v1008 - Freeze Record

Frozen: 2026-09-09 23:31:19 -07:00
Application: v1008
Engine: 3.0
Environment: TEST
Source root at freeze: C:\RecompAzure\web_recomp_v1008

## Freeze status

- Release build: PASS
- Current v1008 automated regression gate (excluding historical Engine21 tests): PASS
- Weekly Plan intentionally retains the existing broader planning-target calculation.
- Dashboard retains Engine 3.0 individualized/adaptive calculation.
- No LIVE deployment or LIVE database change is part of this freeze.

## Accepted / documented v1008 limitations

1. Engine 3.0 physiology/calibration coefficients are not considered fully tuned.
2. Glycogen, workout-energy, habitual-activity, muscle-region and recruitment assumptions require a structured calibration pass.
3. Weekly Plan and Dashboard may legitimately show different calorie/macro targets in v1008 because they are intentionally different calculation surfaces.
4. Do not unify Weekly Plan with Dashboard Engine 3.0 until calibration is completed and validated.
5. GitHub Actions / CI-CD is deferred and is not a v1008 freeze blocker.
6. Engine 3.0 calibration is the first planned technical focus for TEST v1009.

## v1009 starting objective

12WR TEST v1009 - Engine 3.0 Calibration

Initial validation scope:
- active Admin workout profiles;
- standardized one-person sweep across all workout types;
- workout energy;
- glycogen demand;
- muscle-region fraction;
- recruitment fraction;
- resistance hard-set workload;
- habitual activity;
- energy policy;
- macro allocation.
