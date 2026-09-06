# Stats Forked

Stats Forked is a maintained fork of [Stats](https://github.com/AzzkiyOne/Stats) for RimWorld 1.6. It keeps the original sortable stats-table framework while adding modern compatibility modules, inventory-aware filtering, saved presets, bionics comparison, distance-based ranged DPS, and better handling for stuffable item variants.

The fork exists to keep the Stats table framework usable with current RimWorld content and larger modlists while preserving the original mod's data-driven approach.

## Features

- **Sortable stats tables** for weapons, apparel, animals, plants, beds, buildings, turrets, mechs, bionics, and general stats.
- **Inventory-aware filters** for items owned by the colony and items visible on the current player map.
- **Equipment recipe filters** for apparel and weapons, including recipe presence, ingredients, benches, and material/stuff.
- **Saved table presets** for restoring filter states, visible columns, and variant display mode.
- **Stuffable item variants** so material-specific versions can be compared when a table supports variants.
- **Bionics comparison table** covering affected body parts, capacities, efficiency, special effects, and capacity changes.
- **Distance-based ranged DPS** columns for close, short, medium, and long range weapon comparison.
- **Quality-aware stat handling** and default-stuff stat resolution for more useful apparel and equipment data.
- **Compatibility modules** for Biotech, Anomaly, Odyssey, and Combat Extended where those mods or expansions are active, including Odyssey gameplay tables for books, fish, gravship systems, orbital infrastructure, unique weapons, and fishing outcomes.
- **Runtime packaging on build** into `Runtime Only/Stats Forked`.

## Installation

### Steam Workshop

Subscribe on Steam:

- [Stats Forked Steam Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3479566439)

Enable **Harmony** and **Stats Forked** in RimWorld's mod list. Harmony is required. The original Stats mod is not required.

### Manual Installation

1. Download or clone this repository.
2. Place the `Stats Forked` mod folder in your RimWorld `Mods` directory.
3. Enable **Harmony** and **Stats Forked** in the RimWorld mod list.
4. Do not enable the original Stats mod alongside Stats Forked unless you are deliberately testing compatibility.

## Usage

Open the Stats table from the in-game main tab. Choose a table, sort by the columns you care about, and use filters to narrow the displayed defs.

Useful workflows include:

- Compare ranged weapons by accuracy, damage, warmup, cooldown, and DPS at specific distance bands.
- Filter thing-based tables to colony-owned or currently visible items.
- Filter apparel and weapons by whether they have recipes, which ingredients they use, which benches make them, and which material variants apply.
- Save table presets for repeated comparisons.
- Toggle variants for stuffable items when you need material-specific stat values.
- Compare bionics by affected body part, capacity impact, efficiency, and special effects.
- Review Odyssey books, fish, gravship systems, orbital infrastructure, unique weapons, and fishing outcomes when Odyssey is active.

## Configuration

Stats Forked stores table presets in the mod settings data managed by RimWorld. Presets can include:

- active filters
- visible columns
- variant display mode

Supported RimWorld versions:

- RimWorld 1.6

Optional compatibility content loads when these expansions or mods are active:

- Biotech
- Anomaly
- Odyssey, including books, fish, gravship systems, orbital infrastructure, unique weapons, and fishing outcomes
- Combat Extended

## Building from Source

Prerequisites:

- .NET SDK compatible with the project build
- RimWorld reference packages resolved through the project package references

Build the full solution:

```powershell
dotnet build Stats.sln -c Debug -m:1 /p:UseSharedCompilation=false
```

The shared build configuration is in `Directory.Build.props`. Build output and runtime packaging are generated under ignored build folders, including `Runtime Only/Stats Forked`.

## Testing and Validation

Run the test suite against the full solution:

```powershell
dotnet test Stats.sln -c Debug -m:1 /p:UseSharedCompilation=false
```

Run both production build configurations:

```powershell
dotnet build Stats.sln -c Debug -m:1 /p:UseSharedCompilation=false
dotnet build Stats.sln -c Release -m:1 /p:UseSharedCompilation=false
```

The tests target .NET Framework 4.8, keep runtime packaging disabled, and include XML checks that resolve every table and column worker class in the built Core and compatibility assemblies. The shared build configuration in `Directory.Build.props` keeps test output separate from runtime-only packaging.

See [TESTING.md](TESTING.md) for the table-frame regression, log checks, and manual RimWorld scenarios.

## Contributing

> Contributions, issues, and feature requests are welcome.

## Links


Support me on Ko-fi. This does not imply endorsement by the original authors.

[![Support me on Ko-fi](https://img.shields.io/badge/Support_me_on_Ko--fi-72a4f2?style=for-the-badge&logo=kofi&logoColor=white)](https://ko-fi.com/I7L525WMJ6)
[![GitHub Repository](https://img.shields.io/badge/GitHub-Repository-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/KyleMHB/StatsForked)
- **Steam Workshop:** [Stats Forked](https://steamcommunity.com/sharedfiles/filedetails/?id=3479566439)
- **Source Repository:** [KyleMHB/StatsForked](https://github.com/KyleMHB/StatsForked)
- **Issue Tracker:** [GitHub Issues](https://github.com/KyleMHB/StatsForked/issues)
- **Original Project:** [AzzkiyOne/Stats](https://github.com/AzzkiyOne/Stats)
- **Harmony:** [Harmony for RimWorld](https://github.com/pardeike/HarmonyRimWorld/releases/latest)

## License

> This project is a fork of **Stats** and inherits the original project's license. See the original project and this repository's license files for license terms.

This repository includes GNU LGPL license files:

- [COPYING](COPYING)
- [COPYING.LESSER](COPYING.LESSER)

## Credits

- Original Stats project by [AzzkiyOne](https://github.com/AzzkiyOne).
- Stats Forked maintained by [kylohb](https://github.com/KyleMHB).
- Harmony by the Harmony/RimWorld modding community.
- Compatibility support references RimWorld expansion and mod content where available.
