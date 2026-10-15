# Coach Center foundation

Routes: `/coach` (Coach or Administrator), `/coach/clients/{clientId}` (active relationship required), `/my-coach` (client owner).

Coach profiles and coaching relationships are separate from client ownership and built-in learning coaches. Admin Users already provisions Coach roles. No role is granted by visiting the portal. All mutation routes require anti-forgery tokens. Client connections use 256-bit single-use codes, stored only as SHA-256 hashes, expiring after 14 days. Clients explicitly consent before any record is visible and may revoke access. Existing personal dashboards keep their owner context; no global client switching is introduced.

Migration 044 is additive and idempotent. An authorized database administrator must apply migration 043 (if absent) and migration 044 before activation. Deployment rechecks these idempotent scripts before replacing the application. The TEST runtime database identity has no CREATE TABLE permission (SQL 262); its permissions must remain unchanged. The TEST upload uses the existing Azure deployment identity via bearer authentication; migration stderr is retained in deployment diagnostics. No runtime DDL or new identity/permission grants are introduced.

Delivered: coach profile editing (name, bio, specialties), connection code creation/acceptance, client roster, latest measurement and recent completed workout overview, client/coach disconnection, portal navigation.

Next: trainer photo uploads; per-client prescription policies with macro and daily/weekly calorie limits, conflict preview and audit; reusable coach routines with sharing off by default; weekly scheduling tied to projected workload; private conversations; trend comparisons and attention queue. These are described as future work, not enabled controls. The engine and existing macro assignments are unchanged by this foundation.

## Activation handoff

1. On the TEST database, a database administrator runs `database/043_v1015c_learning_coach_foundation.sql`, followed by `database/044_coach_center_foundation.sql`. Both are additive and idempotent.
2. Re-run the failed TEST deployment job for the current test branch commit. Build and all 640 tests passed; replacement was blocked before deployment by SQL 262.
3. Verify Coach Center with a Coach or Administrator account and My Trainer with a client owner. Do not assign real clients for verification without their consent.
4. Apply the same schema prerequisites to LIVE using a database administrator, then promote the validated test commit. LIVE remains on its previous release until this prerequisite is complete.

## Connection-code update
New invitations use a four-letter word plus two digits (for example PINE42), expire after 24 hours, and remain hashed and single-use. Existing 64-character invitations remain valid until their original expiry. Invite generation and acceptance are rate-limited. Expired invitations are marked revoked while retaining history. SQL Server transactions run inside its execution strategy, with tracked state reset for retries. Prescription activation uses the same retry-safe pattern.
