# Kinetic exercise catalog snapshot

This directory is populated by `scripts/Refresh-KineticCatalogSnapshot.ps1`.

12WR does **not** depend on the Kinetic hosted API at runtime. The open dataset is
copied into the versioned 12WR source tree so a frozen application version remains
reproducible even if an external service changes or disappears.

Source: https://github.com/kinetic-place/exercises-db
License: MIT

The refresh script stores:
- `exercises.json`
- `muscle_groups.json`
- `equipment.json`
- `LICENSE-KINETIC.txt`
- `snapshot-manifest.json` with SHA-256 hashes and counts

The Kinetic data is treated as catalog/source data only. 12WR-specific physiology,
regional muscle weighting, fatigue, glycogen and Engine 3.0 metadata remain owned by
12WR and are not inferred blindly from the external dataset.
