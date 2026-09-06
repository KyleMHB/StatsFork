# Changelog

## 2026-09-05

### Added

- Added a .NET Framework 4.8 NUnit test project with table-session, column-lifecycle, Odyssey projection, and XML worker-resolution coverage.
- Added an immutable rendered-table frame seam that keeps rows, heights, columns, and geometry from mixed Layout generations.
- Added durable testing and table-frame architecture notes, including the RimWorld log acceptance scenarios.

### Changed

- Table actions now queue for the next frame preparation and configuration changes publish as one atomic transition.
- Column workers now have idempotent disposal and Odyssey workers consume typed projections through a reflection adapter.
- Test builds disable runtime packaging and keep generated test output outside the mod deployment folders.

### Fixed

- Fixed stale row-height state causing visible-row drawing to index geometry from a previous row generation after filters or presets changed the table.
- Fixed worker subscriptions and staged workers surviving hidden-column, remove/re-add, or failed-transition paths.

## 2026-08-28

### Added

- Added a default preset action for each table.
- Added restoration of open tables and the active table between sessions.
- Added filters that remain active when their columns are hidden.
- Added translation keys for the table UI and filter labels.
- Added localized filter operator descriptions, option controls, and table interaction help.
- Added an optional fixed-height expanded display for multi-value cells.

### Changed

- Compatibility assemblies now use globally unique names: `Stats.Compat.Odyssey.dll`, `Stats.Compat.CE.dll`, `Stats.Compat.Biotech.dll`, and `Stats.Compat.Anomaly.dll`.
- Presets now persist the expanded multi-value display mode and stable filter identifiers.
- The Filters window now shows active filters and the filter being configured, with a searchable picker grouped into table, visible-column, and hidden-column filters.
- Renamed the Values toolbar option to Expanded View.
- Expanded multi-value rows now grow to the content they display, up to ten lines, with the full value list retained in tooltips.
- Expanded rows now vertically center text and icons.
- Runtime-only deployment now packages the English keyed language files beneath the always-loaded Core folder.

### Fixed

- Added missing Combat Extended column tags to remove configuration errors and restore compatible columns to the column menu.
- Fixed table-window resizing so drag calculations use a stable screen-space anchor and cannot produce invalid geometry.
- Fixed the Add column filter action so a new hidden filter remains available while it is being configured.
- Fixed table toolbar and filter controls displaying raw translation keys instead of English labels.
- Fixed inactive hidden filter drafts remaining registered after the Filters window closes.
- Fixed compatibility assembly collisions with other mods that use generic assembly names such as `Odyssey.dll`.

## 2026-05-11

### Added

- Added apparel and weapon filters for recipe presence, recipe ingredients, recipe benches, and material/stuff.
- Clarified that Stats Forked does not require the original Stats mod.

### Fixed

- Fixed Odyssey table dropdown icons that were showing the missing-texture placeholder.
- Fixed the main melee DPS column so it responds to selected quality.
- Fixed melee DPS quality scaling when RimWorld hides the melee damage multiplier stat.
- Fixed melee weapon damage and DPS cells blanking for non-normal quality selections.
- Fixed quality selection consistency for quality-aware custom weapon columns across eligible tables.
- Fixed a Searchable Menus compatibility crash when table column menu entries were updated.

### Changed

- Removed the Odyssey fishing outcomes table.
- Removed the default description column from Odyssey tables.

## 2026-05-05

### Added

- Added melee weapon columns for armor penetration-adjusted DPS, blunt DPS, and sharp DPS.

## 2026-05-03

### Fixed

- Fixed missing table icon paths logging RimWorld load errors by falling back to the default bad texture.

### Added

- Added completed Odyssey gameplay table support for books, fish, gravship systems, orbital infrastructure, unique weapons, and fishing outcomes.

### Changed

- Updated README and Steam Workshop description support text to state that Stats Forked supports RimWorld 1.6 only.
- Clarified the project history now that RimWorld 1.5 support is no longer maintained.

## 2026-05-02

### Added

- Added updated README and Steam Workshop description files for the documentation refresh.
- Added a template-structured README covering installation, usage, configuration, source builds, validation, links, license, and credits.
- Added a Steam Workshop description document for publishing-facing mod copy.
- Added a dated changelog structure based on the GlobalTemplates changelog format.

### Changed

- Changed the changelog from a running summary into a dated project history with categorized entries.
- Updated documentation to use the current package metadata, Steam Workshop link, supported RimWorld versions, compatibility modules, and source repository links.

## 2026-05-01

### Added

- Added inventory-aware table support for colony-owned and map-visible item filters.
- Added saved table presets for restoring filter state, visible columns, and variant display mode.
- Added stuffable item variant support with a table toolbar toggle.
- Added a bionics comparison table with body part, capacity, efficiency, effect, special effect, and content source columns.
- Added distance-based ranged DPS columns for close, short, medium, and long range comparisons.
- Added general stats table definitions and expanded table definitions for Core, Biotech, Anomaly, CE, and Odyssey content.
- Added Biotech gene, mechanoid, and apparel table support.
- Added Combat Extended columns for caliber, one-handed weapons, magazine capacity, reload time, and related weapon stats.
- Added an Odyssey compatibility module scaffold and included it in the solution and load folder configuration.

### Changed

- Changed the project branding and metadata to Stats Forked.
- Updated the table UI for column management, filtering, sorting, row handling, toolbar actions, variant expansion, and preset persistence.
- Updated stat request handling to use default stuff for apparel where needed.
- Updated equipped stat offset handling and quality-aware stat table behavior.
- Split Biotech compatibility out of Core into a dedicated module.
- Updated runtime packaging so builds produce `Runtime Only/Stats Forked`.
- Updated repository ignore rules for generated assemblies, object folders, build outputs, local caches, debug symbols, Visual Studio files, and temporary files.

### Fixed

- Fixed compatibility table structure for newer RimWorld content and active module loading.
- Fixed generated build and deploy output being kept in the working tree.

### Internal

- Added shared MSBuild configuration in `Directory.Build.props`.
- Added Odyssey, CE, Biotech, Anomaly, and Core projects to the solution/build layout.
- Preserved the tracked Combat Extended reference DLL needed by the project.
