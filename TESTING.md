# Testing and acceptance

## Automated checks

Run the full test and build matrix from the repository root:

```powershell
dotnet test Stats.sln -c Debug -m:1 /p:UseSharedCompilation=false
dotnet build Stats.sln -c Debug -m:1 /p:UseSharedCompilation=false
dotnet build Stats.sln -c Release -m:1 /p:UseSharedCompilation=false
```

The `Stats.Tests` project targets `net48` and sets `EnableRuntimePackaging=false`. Its table tests use `TableSession<TObject>` and its Odyssey tests use `IOdysseyProjection`; private registry and drawing helpers are not tested directly. The XML worker-resolution test scans the mod XML under `Core`, `Biotech`, `Anomaly`, `CE`, and `Odyssey`, including patch XML, and resolves each non-comment `workerClass` element against the built project assemblies.

## Table-frame regression

The original failure was an `ArgumentOutOfRangeException` in `ObjectTable.GetRowHeight` while drawing visible rows. Unity's immediate-mode UI can run a Layout pass, apply a filter or preset, and then draw after the row list has changed. The positional `_rowHeights` array still described the previous row generation, so drawing paired a new row index with old geometry.

`TableSession<TObject>` queues GUI mutations and prepares one immutable `TableFrame<TObject>` for the next frame. The frame copies row order, row heights, visible columns, widths, pinned counts, aggregate geometry, and a monotonic revision together. Its required invariant is `Rows.Count == RowHeights.Count`; drawing consumes only that captured frame. A failed preparation keeps the previous frame, retains the queued intent for a compatible retry, and records a diagnostic.

Automated coverage includes row growth and shrinkage, variable expanded heights, pinned and empty tables, scrolling past the last row, deferred/coalesced actions, atomic configuration rollback, worker lifecycle disposal, and Odyssey reflection fallback/exception containment.

## RimWorld acceptance scenarios

With RimWorld 1.6 and the installed official DLC enabled, exercise the following in a representative save:

- Shrink and reset filters while the table is scrolled.
- Toggle expanded values, variants, and quality.
- Apply presets that add rows or hidden filters.
- Pin, reorder, and resize rows and columns.
- Remove and reopen tables.
- Complete research while a research-status filter is active.

When CE is installed locally, repeat the relevant weapon and apparel tables with CE enabled. After each scenario, inspect the newest usable RimWorld log. Acceptance requires no Stats exceptions, stale-frame diagnostics, disposed-worker callbacks, or secondary word-wrap warning. The RimWorld UI scenarios remain manual because the test project does not host Unity's immediate-mode GUI or RimWorld's live Def database.
