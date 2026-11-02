# Orangetheory dated workout lookup

Selecting the generic Orangetheory 2G or Strength 50 system workout resolves the selected class date automatically. The OTF preview includes a date/class selector and an automatic-lookup switch, remembered in this browser for the signed-in client. Existing weekly plans retain their generic planning codes. The canonical Engine 3.0 type remains ORANGETHEORY for 2G and RESISTANCE_CIRCUIT for Strength 50.

Resolution order: matching built-in dated recipe, validated persistent public recipe cache, OpenAI Responses web search, generic class profile. Only the public date and class type are sent. Source URLs must come from the web search response and point to Reddit or Orangetheory. The model must return the matching date and type; 3G, Tread 50 and presumed fortnightly repeats are excluded. Public community workouts can differ from a studio's actual class, so the user reviews the imported session before completion.

Imported movements preserve order, names, published workload descriptions, estimated rounds and source notes. Catalog matches retain catalog muscle roles. Other movements receive per-client exercise identities with estimated primary/secondary muscle rows. Unknown older movements remain unmapped when their names do not support a conservative mapping. Completed sessions are not rewritten. Generic fallback keeps the existing class physiology, with no invented exercise rows, and is labeled as an estimate.

Configuration:

- Reuses Smart Intelligence's server-side OpenAI connection and configured model. Configure that connection in Admin → Manage Connections if absent.
- `OtfWorkouts:Model`: optional model override supporting Responses web search and structured output.
- `OtfWorkouts:Enabled`: defaults true; false disables external lookup.
- `OtfWorkouts:DataPath`: optional persistent public-recipe directory. Default is the App Service HOME/data/otf-workouts directory.
- Lookups time out after 70 seconds; missing/failed lookups use generic profiles and are cached for 15 minutes. Successful results are cached in memory for 12 hours and on persistent disk. The first selection can take about a minute.

No database migration is needed. Tests cover exact-date selection, generic fallback, switch behavior, cache reuse, source/date/type/muscle validation and persistent muscle-role records without duplicates. An actual paid OpenAI lookup and signed-in browser flow require a configured deployment; automated tests do not call the paid API.
