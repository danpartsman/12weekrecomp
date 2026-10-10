# Dashboard2 TEST — October 9, 2026

Separate /Dashboard2 route and navigation link. Original /Dashboard remains available.
Order: daily nutrients, daily training, tomorrow nutrients/training, daily and four-day analysis, coaches corner (daily advice and recomp lessons together).

## Data and limits
- Reuses DashboardService and its setup checks; no schema migration or nutrition-target writeback.
- Complete Food Log totals drive meal progress when authoritative. Manual overrides/imported totals remain authoritative and are never added to food logs.
- Missing or incomplete intake pauses allocation. Macro/energy discrepancies above max(100 kcal, 10%) pause suggestions for review.
- Meal slots have no consumption timestamps; the chart groups slots and does not pretend to show actual eating times. Logged meals need not be finished.
- Remaining eating occasions can be changed from 1 to 4. Defaults use client-local time and observed meal slots.
- Allocation aims at engine central targets: protein first, lower planned fat allocation reserved, carbs, then remaining fat. It stays within remaining energy, surfaces unresolved deficits, and never recommends chasing every upper bound.
- TEST display bands are design tolerances: protein target through +10%; carbs/fat ±10%. Not experimentally established physiological thresholds. No fixed grams are prescribed to all users.
- Approximate 3h spacing is optional scheduling guidance, not an absorption rule. No meal timestamp is invented.
- Existing daily/four-day engine analysis remains visible as model output with uncertainty. No assertion of measured glycogen or short-term fat gain.

## Evidence standard
Original empirical studies, scrutinized for methods, population, duration, confounding, outcomes, uncertainty, conflicts and replication. Institutional guidance and repetition do not establish truth. Distinguish findings, extrapolations, design choices and hypotheses. Study publication is not sufficient proof. Some source access was limited to original abstracts; no complete funding audit claimed.

## Lesson experiments
- Mettler 2010: https://pubmed.ncbi.nlm.nih.gov/19927027/
- Longland 2016: https://pubmed.ncbi.nlm.nih.gov/26817506/
- Trommelen 2023: https://pubmed.ncbi.nlm.nih.gov/38118410/
- Areta 2013: https://pubmed.ncbi.nlm.nih.gov/23459753/
- Burke 1996 feeding frequency: https://pubmed.ncbi.nlm.nih.gov/8669406/
- Bray 2012: https://pubmed.ncbi.nlm.nih.gov/22215165/

Tests cover meal recognition, remaining protein split, missing/incomplete records, manual overrides, energy overages, combined bands, inconsistencies, input clamping, no extra-food prompting at target, and deleted foods.
