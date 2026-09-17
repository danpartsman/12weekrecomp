# 12WR TEST v1010 — Phase 2A Workout Intelligence Boundary

## Decision

Detailed workout processing is a separate logical module with a versioned internal handoff contract.

```text
Exercise source/catalog
        ↓
WebRecomp.WorkoutIntelligence
        ↓  workout-intelligence/v1
normalized WorkoutIntelligenceHandoff
        ↓
future Engine 3.0 adapter
```

## Boundaries

`WebRecomp.WorkoutIntelligence` owns:
- external exercise catalog adapters/import parsing;
- detailed set/exercise aggregation;
- volume, hard-set and density summaries;
- muscle-demand aggregation;
- future workout-specific physiology metadata.

It does **not** own:
- Engine 3.0 calorie/glycogen/macronutrient decisions;
- EF/database entities;
- MVC/HTTP models;
- user/program authorization;
- Kinetic network availability.

Engine 3.0 must consume a normalized handoff through a later adapter. It must not read Kinetic JSON or detailed workout tables directly.

## Kinetic source

Kinetic is imported as a local snapshot from its MIT-licensed open repository. 12WR does not require the hosted Kinetic API at runtime.

The snapshot includes SHA-256 provenance so each frozen 12WR version can reproduce exactly which catalog data it used.

## Phase 2A does not alter SQL

This phase creates the software boundary first. Database schema/import and detailed workout UI follow after the boundary is compiling and tested.
