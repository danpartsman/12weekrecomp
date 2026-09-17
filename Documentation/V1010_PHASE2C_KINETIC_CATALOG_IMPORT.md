# v1010 Phase 2C — Kinetic Catalog Import

The Workout Catalog Importer loads the local MIT-licensed Kinetic snapshot
through the Workout Intelligence internal persistence contract.

Authentication uses the cached Azure CLI Entra session to request a temporary
Azure SQL token. Passwords and access tokens are not persisted.

Import hardening:
- local snapshot hash/provenance verification
- duplicate exercise ID checks
- SQL column-length validation before database writes
- 300-second catalog-import command timeout
- controlled EF batch size
- full DbUpdateException / SqlException diagnostics
- import remains transaction-protected by SqlExerciseCatalogStore

Fixed TEST target:
- sql-recomp-test-dan.database.windows.net
- sqldb-recomp
- dlandon@lockedontech.com

Engine 3.0 remains unchanged.
