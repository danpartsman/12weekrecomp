# Smart Intelligence v1

Route: `/SmartIntelligence`, linked under Command Center. This is an authenticated, client-scoped read-only module. It does not run the prescription engine or persist health-record changes. No database migration is required. Existing TEST-only changes must not be promoted to LIVE as part of this module.

## Model

Four separate two-state local-linear filters: level and daily slope for fat, consistent muscle metric, weight and estimated water. These are not four independent measurements or a uniquely identified tissue/fluid decomposition. Water must never be subtracted from muscle. Observations are the latest nondeleted saved reading per local date. Skeletal and generic muscle values are never combined.

F=[1,1;0,1], H=[1,0]. Base process variances: level 0.0009 lb²/day, slope 0.000004. Sensitivity multipliers: steady .3, balanced 1, responsive 3. Observation SD uses only earlier adjacent-day differences, robust MAD / sqrt(2), with floors fat .8 lb, muscle .5 lb, weight .8 lb, water .6 lb. Innovation >2.5 standardized SD inflates observation variance (Huber weighting). Missing days predict with widening variance; no zero-filling. Twenty-eight earlier days initialize the visible window where available. Bands are 1.96 posterior model SD, conditional on assumptions; they omit unknown scale bias and are not validated clinical confidence intervals. Adaptive noise and all earlier estimates are causal. Do not force monotonic improvement.

The default graphs show actual levels, model estimates and original readings. Three-/seven-day averages and their seven-day differences are available in the export. Existing dashboard charts and prescriptions remain unchanged. Correlations use explicit calendar lags, pairwise available values, daily differences where relevant, and at least eight paired days. They are exploratory, not causal or adjusted for multiple comparisons/autocorrelation.

## Evidence

Completed sessions only. Missing sessions stay unknown; repeated name/type/duration/RPE records are flagged, not silently removed. Template-estimated sets and missing exercise loads remain visible. Past nutrition records are authoritative, including manual overrides. Totals between 0 and 1,000 kcal are marked possibly partial and excluded from the optional weekly/correlation summaries, not deleted or assumed false. Current-day nutrition is unfinished. Historical target source: first captured same-day target if available; otherwise latest stored Recommendation, explicitly labeled. Missing targets are not reconstructed. Intake minus prescription is not energy balance versus maintenance.

## OpenAI and future providers

`ISmartAnalysisProvider` accepts a client-scoped, versioned evidence report and returns structured dated findings. OpenAI Responses API is the initial implementation, fixed HTTPS endpoint, redirects disabled, store=false. Optional notes and user context are treated as untrusted data. No names, account IDs or emails are included in the assembled request; user-entered notes may contain personal content, so the form discloses transmission. No requests run just by opening the page. The explicit Analyze button authorizes the request. AI is advisory, cannot rewrite records or prescribe automatically.

Server configuration: `SmartIntelligence__OpenAI__ApiKey`, `SmartIntelligence__OpenAI__Model` (default `gpt-5-mini`); existing `OpenAI__ApiKey` or `OPENAI_API_KEY` also accepted. If no existing key is configured, an Administrator can enter one on the page. It is encrypted with ASP.NET Data Protection in the private App Service HOME/data/smart-intelligence directory. Protect/persist the host's Data Protection keys. Key is never returned or prefilled. Environment configuration takes precedence. No secret is committed. Four analysis/setup submissions per user per five minutes; identical client/evidence/model/context requests reuse a 30-minute memory cache. Logs never contain request payloads, notes, keys, or upstream response bodies.

A missing key does not disable statistical analysis. The page clearly reports that AI needs connection; it must not display local heuristic text as a successful OpenAI result. Additional providers can be registered without changing the evidence model. JSON export supports comparison and offline model evaluation.

## Validation

Automated tests cover isolated outliers, missing weeks, uncertainty expansion, sustained reversal, causality, date-aligned correlation pairs, latest daily reading, tenant isolation, completed workouts, unknown targets and no database writes. Deployment uses each environment's existing branch and CI; do not reset TEST to MAIN or include unrelated TEST work in LIVE.

LIVE release verification decodes byte-array manifest responses as UTF-8 before parsing JSON. PowerShell may return `application/manifest+json` as bytes; piping those bytes directly into `ConvertFrom-Json` does not validate the manifest object.
