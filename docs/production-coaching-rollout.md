# Coaching production rollout

Promote the validated TEST source, preserving LIVE users, programs and logged entries. Never copy TEST accounts or sample data into LIVE.

An authorized SQL administrator applies the additive, idempotent scripts 043, 044, 045, 046 and 047 to `sqldb-recomp-live` on `sql-recomp-test-dan.database.windows.net`. Script 047 requires `@ExpectedDatabase` to match the selected catalog. Its insert/read/rollback smoke runs only in TEST; LIVE receives schema changes and narrowly scoped application permissions for the new tables, without sample agreements or approvals.

The migration tool requires an exact site/server/catalog match: `recomp-test` / `sqldb-recomp`, or `recomp-live` / `sqldb-recomp-live`. Set `RECOMP_EXPECTED_DATABASE` explicitly for LIVE. Sample-data insertion remains restricted to TEST.

Both deployment workflows verify the three accountability tables through the existing runtime identity before replacing the application. No runtime CREATE TABLE permission or SQL login is added. Accountability routes and views are enabled only on the explicitly named TEST and LIVE App Services; normal role, owner, active-coach relationship, anti-forgery and approval checks remain in effect.

Agreements, results, dated excuses, immutable approvals and change history persist in each environment's own database. Payment totals are calculated estimates; this release does not collect payments automatically.
