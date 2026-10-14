# Coach Center foundation

Routes: `/coach` (Coach or Administrator), `/coach/clients/{clientId}` (active relationship required), `/my-coach` (client owner).

Coach profiles and coaching relationships are separate from client ownership and built-in learning coaches. Admin Users already provisions Coach roles. No role is granted by visiting the portal. All mutation routes require anti-forgery tokens. Client connections use 256-bit single-use codes, stored only as SHA-256 hashes, expiring after 14 days. Clients explicitly consent before any record is visible and may revoke access. Existing personal dashboards keep their owner context; no global client switching is introduced.

Migration 044 is additive and idempotent. Deployment runs it with the existing App Service configured database identity before replacing the application. The TEST upload uses the existing Azure deployment identity via bearer authentication. No runtime DDL or new identity/permission grants are introduced.

Delivered: coach profile editing (name, bio, specialties), connection code creation/acceptance, client roster, latest measurement and recent completed workout overview, client/coach disconnection, portal navigation.

Next: trainer photo uploads; per-client prescription policies with macro and daily/weekly calorie limits, conflict preview and audit; reusable coach routines with sharing off by default; weekly scheduling tied to projected workload; private conversations; trend comparisons and attention queue. These are described as future work, not enabled controls. The engine and existing macro assignments are unchanged by this foundation.
