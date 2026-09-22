# Alien desert environment assets

Runtime artwork is the approved transparent PNG catalog under:

- `Mountains/` — three distant mountain/mesa silhouettes;
- `Cities/` — three distant city silhouettes;
- `Ruins/` — three ancient structure groups;
- `Rocks/` — five near-background formations;
- `Plants/` — two rare alien vegetation assets.

The game project packages only those five directories. Asset sheets, individually
generated source images, extraction tests, V1 backups, staging copies, and QC previews
are retained locally under ignored paths; they must never be loaded, copied as runtime
content, or committed with the final catalog.

`EnvironmentPresentationSettings` owns the sky palette, per-layer color treatment,
parallax factors, spacing, scale ranges, and overscan. `EnvironmentLayoutGenerator`
derives versioned named sub-seeds from each run seed so unrelated layers remain stable
when a layer is added or revised. All assets are visual-only and use linear filtering and
premultiplied alpha blending behind cave terrain and gameplay.
