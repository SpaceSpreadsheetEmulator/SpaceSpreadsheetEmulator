# ADR 0007: Build-pinned Dogma definitions and preloaded runtime catalogs

Status: accepted

## Context

Types, Dogma attribute definitions, effect definitions, and per-type base values are
immutable for one promoted SDE build. They are not mutable item state and should not
be copied into PostgreSQL or fetched from SQLite in a simulation tick.

The SDE also does not guarantee that human-readable attribute or effect names are
globally unique. Runtime rules need stable identifiers, while original code should
still be able to resolve well-known semantic names without scattering numeric IDs.

## Decision

Static-data schema 5 imports normalized, read-only tables for:

- runtime-relevant type fields;
- Dogma attribute and effect definitions;
- per-type attribute values;
- per-type effect associations and their default flag.

`ITypeDefinitionQuery` and `IDogmaStaticDataQuery` are asynchronous artifact
boundaries. They are appropriate during startup, content validation, tooling, or
cache population. Worker registers these interfaces separately so consumers can
depend on the narrow query they require.

`DogmaDefinitionCatalog` preloads selected type profiles plus the attribute and
effect dictionaries into immutable in-memory indexes. `IDogmaDefinitionCatalog`
then provides synchronous runtime lookup by authoritative numeric ID, as well as by
name when that name is unique. Ambiguous names return no result and require an
explicit ID. Missing referenced definitions, duplicate IDs, and non-finite values
fail catalog loading.

The catalog contains static definitions and base values only. Durable item
overrides, computed values, active effects, sourced modifiers, dependency
invalidation, stacking, and provenance remain responsibilities of later Dogma
runtime slices.

## Consequences

- Simulation code can query a bounded immutable catalog without storage I/O.
- Static-data artifacts remain pinned by client build, protocol profile, SDE build,
  importer version, schema version, source hash, and artifact hash.
- Existing schema-4 artifacts can still serve their original generic queries, but
  the new typed query surface rejects them with an explicit schema error.
- Numeric IDs remain authoritative even when upstream semantic names collide.
- Adding a gameplay mechanic still requires an original, tested handler when the
  SDE describes data but not complete server behavior.
