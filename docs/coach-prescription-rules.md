# Coach prescription rules

Connected coaches and administrators with an active client relationship can open `/coach/clients/{clientId}/prescriptions` from the client overview. No access follows from administrator status alone. All writes require anti-forgery tokens and a current relationship.

The rule builder sets optional protein/carbohydrate/fat min/max, daily calorie min/max, and Monday–Sunday weekly calorie min/max. Calories are always computed from final macros (4/4/9). Calorie limits only alter macros after explicit permission to adjust carbs, then fat, within fully specified bounds. Protein remains fixed after its own bounds are applied. Incompatible constraints block activation.

Preview shows the original shared engine projection against the adjusted seven days and week total. Save recomputes this preview, compares its fingerprint and the latest saved version, then appends an immutable audit version (coach, reason, rules, original/adjusted week). Rules begin on an upcoming Monday; no retroactive activation is permitted. Disabling adds an audited version restoring the engine from that week. Disconnecting the author removes their policy from active use. Past captured scores remain unchanged.

The overlay runs after energy carryover in Engine30DashboardProjectionService, the common source used by Command Center, Nutrition and Weekly Plan. Unexpected future conflicts preserve the engine targets and display a trainer-review notice on Command Center and Nutrition. The original physiological workload calculation is retained.

Apply additive, idempotent migration 045 with the database administrator on TEST and LIVE before deployment. Runtime database permissions are unchanged. No executable user-provided code is accepted; this is the configurable rule builder stage.
