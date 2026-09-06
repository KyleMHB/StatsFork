# Table frame architecture

## Failure mode

An ObjectTable spans Unity's Layout and draw events. Before the frame seam, a filter, preset, or other GUI action could change `_rows` between those events while `_rowHeights` and the visible-row calculation still belonged to the previous layout. A draw then indexed geometry with the new row generation and could throw `ArgumentOutOfRangeException` from `ObjectTable.GetRowHeight`.

## Invariant and flow

`TableSession<TObject>` is the internal seam between mutable table state and the RimWorld/Unity adapter:

1. GUI actions queue `TableIntent` values. Queueing does not mutate the published frame.
2. Layout calls `PrepareFrame` with live row and column metrics.
3. Preparation drains and coalesces intents, refreshes live cells, and stages a complete transition.
4. A successful transition publishes one immutable frame with a new revision.
5. Drawing receives that frame explicitly and applies any new GUI actions on the next preparation.

The frame owns copied rows, row heights, visible columns and widths, pinned counts, and aggregate geometry. `Rows.Count == RowHeights.Count` is checked before publication. On invalid metrics, worker construction failure, or frame calculation failure, the previous configuration and frame remain published and the transition exposes a diagnostic for the log/tests.

This keeps the adapter responsible for RimWorld and Unity integration while keeping mutable table behavior behind a small, testable seam. `ColumnRegistry<TObject>` owns worker construction, filter-only lifetimes, subscriptions, and release. Odyssey workers consume typed projections; reflection and compatibility fallbacks stay in the adapter.

## Verification

`Stats.Tests` exercises only the agreed `TableSession<TObject>` and `IOdysseyProjection` seams. Tests assert behavior such as coherent row/height generations, deferred actions, atomic rollback, idempotent worker disposal, and reflection exception containment. The XML worker-resolution test also prevents a renamed or removed worker type from reaching RimWorld's Def loader unnoticed.

See [TESTING.md](../TESTING.md) for commands, log acceptance, and the manual RimWorld scenarios.
