using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Stats.ColumnWorkers;
using Stats.TableWorkers;

namespace Stats.Tests;

[TestFixture]
public sealed class TableSessionTests
{
    [Test]
    public void Column_workers_transition_from_visible_to_filter_only_to_released()
    {
        var worker = new RecordingColumnWorker("value");
        var source = new TableColumnDefinition<int>(
            "value",
            () => worker,
            subscribe: (_, callback) =>
            {
                worker.FilterChanged += callback;
                worker.FilterSubscriptionCount++;
            },
            unsubscribe: (_, callback) =>
            {
                worker.FilterChanged -= callback;
                worker.FilterSubscriptionCount--;
            });
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "value" }),
            new[] { source });

        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Queue(TableIntent.SetVisibleColumns(Array.Empty<string>()));
        session.Queue(TableIntent.SetFilters(new[]
        {
            new TableFilterState("value", "value:0", "Value", "active")
        }));

        TableTransitionResult<int> hidden = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(hidden.Applied, Is.True);
        Assert.That(worker.DisposeCount, Is.Zero);
        Assert.That(worker.FilterSubscriptionCount, Is.EqualTo(1));
        Assert.That(session.FilterOnlyColumnDefNames, Is.EqualTo(new[] { "value" }));

        session.Queue(TableIntent.SetFilters(Array.Empty<TableFilterState>()));
        TableTransitionResult<int> released = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(released.Applied, Is.True);
        Assert.That(worker.DisposeCount, Is.EqualTo(1));
        Assert.That(worker.FilterSubscriptionCount, Is.Zero);
        Assert.That(session.FilterOnlyColumnDefNames, Is.Empty);
    }

    [Test]
    public void Releasing_unused_filter_roles_disposes_hidden_workers_without_touching_visible_workers()
    {
        var visibleWorker = new RecordingColumnWorker("visible");
        var hiddenWorker = new RecordingColumnWorker("hidden");
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "visible" }),
            new[]
            {
                new TableColumnDefinition<int>("visible", () => visibleWorker),
                new TableColumnDefinition<int>("hidden", () => hiddenWorker),
            });

        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.SetColumnWorkerRoles(new[] { "visible" }, new[] { "hidden" });

        Assert.That(session.FilterOnlyColumnDefNames, Is.EqualTo(new[] { "hidden" }));
        session.SetFilterOnlyColumnWorkers(Array.Empty<string>());

        Assert.That(session.FilterOnlyColumnDefNames, Is.Empty);
        Assert.That(hiddenWorker.DisposeCount, Is.EqualTo(1));
        Assert.That(visibleWorker.DisposeCount, Is.Zero);
        Assert.That(session.VisibleColumnDefNames, Is.EqualTo(new[] { "visible" }));
    }

    [Test]
    public void Removing_and_readding_a_column_reuses_the_live_worker()
    {
        int constructions = 0;
        var workers = new List<RecordingColumnWorker>();
        var source = new TableColumnDefinition<int>("value", () =>
        {
            constructions++;
            var worker = new RecordingColumnWorker("value");
            workers.Add(worker);
            return worker;
        });
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "value" }),
            new[] { source });

        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Queue(TableIntent.SetVisibleColumns(Array.Empty<string>()));
        session.Queue(TableIntent.SetFilters(new[]
        {
            new TableFilterState("value", "value:0", "Value", "active")
        }));
        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        TableTransitionResult<int> readded = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(readded.Applied, Is.True);
        Assert.That(constructions, Is.EqualTo(1));
        Assert.That(workers[0].DisposeCount, Is.Zero);
    }

    [Test]
    public void Failed_column_construction_rolls_back_the_previous_frame_and_disposes_staged_workers()
    {
        var stable = new RecordingColumnWorker("stable");
        var staged = new RecordingColumnWorker("staged");
        var source = new TableColumnDefinition<int>("stable", () => stable);
        var failingSource = new TableColumnDefinition<int>(
            "staged",
            () => staged,
            subscribe: (_, _) => throw new InvalidOperationException("cannot subscribe staged worker"));
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "stable" }),
            new[] { source, failingSource });

        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "staged" }));

        TableTransitionResult<int> rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(rejected.Frame, Is.SameAs(initial.Frame));
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "stable" }));
        Assert.That(stable.DisposeCount, Is.Zero);
        Assert.That(staged.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Disposed_column_workers_no_longer_receive_filter_callbacks()
    {
        var worker = new RecordingColumnWorker("value");
        var source = new TableColumnDefinition<int>(
            "value",
            () => worker,
            subscribe: (_, callback) => worker.FilterChanged += callback,
            unsubscribe: (_, callback) => worker.FilterChanged -= callback);
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "value" }),
            new[] { source });

        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Dispose();
        worker.RaiseFilterChanged();

        Assert.That(worker.FilterCallbackCount, Is.Zero);
        Assert.That(worker.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void A_complete_configuration_is_queued_and_applied_as_one_transition()
    {
        var initial = new TableConfiguration(
            new[] { "name", "value" },
            new TableFilterState[0],
            showVariants: false,
            expandMultiValueCells: false,
            quality: 0,
            sortColumnDefName: "name",
            sortDirection: SortDirection.Ascending);
        var session = new TableSession<int>(initial);

        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        session.Queue(TableIntent.SetShowVariants(true));
        session.Queue(TableIntent.SetExpandedMultiValueCells(true));
        session.Queue(TableIntent.SetQuality(2));
        session.Queue(TableIntent.SetSort("value", SortDirection.Descending));

        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name", "value" }));
        Assert.That(session.Current.ShowVariants, Is.False);
        Assert.That(session.Current.ExpandMultiValueCells, Is.False);

        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Frame.Revision, Is.EqualTo(1L));
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value" }));
        Assert.That(session.Current.ShowVariants, Is.True);
        Assert.That(session.Current.ExpandMultiValueCells, Is.True);
        Assert.That(session.Current.Quality, Is.EqualTo(2));
        Assert.That(session.Current.SortColumnDefName, Is.EqualTo("value"));
        Assert.That(session.Current.SortDirection, Is.EqualTo(SortDirection.Descending));
    }

    [Test]
    public void Removing_a_column_keeps_remaining_widths_by_column_identifier()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "first", "second", "third" },
            columnWidths: new[] { 10f, 20f, 30f }));

        session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "first", "second", "third" },
            columnWidths: new[] { 10f, 20f, 30f }));

        session.Queue(TableIntent.SetVisibleColumns(new[] { "second", "third" }));
        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "second", "third" },
            columnWidths: new[] { 20f, 30f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.ColumnWidths, Is.EqualTo(new[] { 20f, 30f }));
    }

    [Test]
    public void Reordering_columns_keeps_pinned_columns_in_the_leading_block()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "pinned-a", "pinned-b", "right" },
            pinnedColumnDefNames: new[] { "pinned-a", "pinned-b" },
            columnWidths: new[] { 10f, 20f, 30f }));

        session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "pinned-a", "pinned-b", "right" },
            columnWidths: new[] { 10f, 20f, 30f },
            pinnedColumnCount: 2));

        session.Queue(TableIntent.ReorderColumns(new[] { "right", "pinned-b", "pinned-a" }));
        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "pinned-b", "pinned-a", "right" },
            columnWidths: new[] { 20f, 10f, 30f },
            pinnedColumnCount: 2));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames,
            Is.EqualTo(new[] { "pinned-b", "pinned-a", "right" }));
        Assert.That(session.Current.PinnedColumnDefNames,
            Is.EqualTo(new[] { "pinned-b", "pinned-a" }));
        Assert.That(session.Current.ColumnWidths, Is.EqualTo(new[] { 20f, 10f, 30f }));
    }

    [Test]
    public void A_preset_configuration_resets_pin_and_width_state_but_preserves_sort()
    {
        TableConfiguration preset = TableConfiguration.ForPreset(
            visibleColumnDefNames: new[] { "new-first", "new-second" },
            filterStates: Array.Empty<TableFilterState>(),
            showVariants: true,
            expandMultiValueCells: true,
            quality: 2,
            sortColumnDefName: "new-second",
            sortDirection: SortDirection.Descending);

        Assert.That(preset.PinnedColumnDefNames, Is.EqualTo(new[] { "new-first" }));
        Assert.That(preset.ColumnWidths, Is.Empty);
        Assert.That(preset.SortColumnDefName, Is.EqualTo("new-second"));
        Assert.That(preset.SortDirection, Is.EqualTo(SortDirection.Descending));
    }

    private sealed class RecordingColumnWorker : ColumnWorker<int>
    {
        private readonly ColumnDef _def;

        internal RecordingColumnWorker(string name)
        {
            _def = new ColumnDef { defName = name, workerClass = GetType() };
        }

        internal event Action? FilterChanged;
        internal int DisposeCount { get; private set; }
        internal int FilterSubscriptionCount { get; set; }
        internal int FilterCallbackCount { get; private set; }

        public override ColumnDef Def => _def;
        public override ColumnType Type => ColumnType.String;
        public override bool IsRefreshable => false;
        public override void DrawCell(UnityEngine.Rect rect, int row) { }
        public override float GetWidth(List<int> rows) => 10f;
        public override void NotifyRowAdded(List<int> rows) { }
        public override void NotifyRowAdded(int row) { }
        public override void NotifyRowRemoved(int row) { }
        public override bool RefreshCells() => false;
        public override ICollection<CellField> GetCellFields(TableWorker tableWorker) => Array.Empty<CellField>();

        internal void RaiseFilterChanged()
        {
            if (FilterChanged != null)
            {
                FilterCallbackCount++;
            }

            FilterChanged?.Invoke();
        }

        public override void Dispose()
        {
            DisposeCount++;
            FilterChanged = null;
        }
    }

    [Test]
    public void Duplicate_columns_and_malformed_filters_are_normalized_with_diagnostics()
    {
        var session = new TableSession<int>();
        session.Queue(TableIntent.ApplyConfiguration(new TableConfiguration(
            new[] { "value", "value", "name" },
            new[]
            {
                new TableFilterState("", "", "", ""),
                new TableFilterState("value", "value:0", "Value", "x"),
            },
            showVariants: false,
            expandMultiValueCells: false,
            quality: 0,
            sortColumnDefName: "name",
            sortDirection: SortDirection.Ascending)));

        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value", "name" }));
        Assert.That(session.Current.FilterStates, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics, Has.Some.Contains("duplicate"));
        Assert.That(result.Diagnostics, Has.Some.Contains("malformed"));
    }

    [Test]
    public void A_sort_column_removed_from_visible_columns_falls_back_to_the_first_live_column()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "name", "value" },
            sortColumnDefName: "missing"));

        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "name", "value" },
            columnWidths: new[] { 40f, 50f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.SortColumnDefName, Is.EqualTo("name"));
        Assert.That(result.Diagnostics, Has.Some.Contains("sort column"));
    }

    [Test]
    public void Unknown_columns_and_filter_columns_are_removed_once_while_table_filters_are_preserved()
    {
        var worker = new RecordingColumnWorker("known");
        var session = new TableSession<int>(
            new TableConfiguration(new[] { "known" }),
            new[] { new TableColumnDefinition<int>("known", () => worker) });

        session.Queue(TableIntent.ApplyConfiguration(new TableConfiguration(
            new[] { "known", "missing", "missing" },
            new[]
            {
                new TableFilterState("missing", "missing:0", "Missing", "active"),
                new TableFilterState("__table_filter_available", "available", "Available", "active"),
            },
            sortColumnDefName: "missing")));

        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "known" },
            columnWidths: new[] { 40f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "known" }));
        Assert.That(session.Current.FilterStates.Select(state => state.ColumnDefName),
            Is.EqualTo(new[] { "__table_filter_available" }));
        Assert.That(session.Current.SortColumnDefName, Is.EqualTo("known"));
        Assert.That(result.Diagnostics.Count(diagnostic => diagnostic.Contains("missing")), Is.EqualTo(1));
    }

    [Test]
    public void A_rejected_layout_preserves_the_previous_configuration_and_queued_transition()
    {
        var initial = new TableConfiguration(
            new[] { "name" },
            new TableFilterState[0],
            showVariants: false,
            expandMultiValueCells: false,
            quality: 0,
            sortColumnDefName: "name",
            sortDirection: SortDirection.Ascending);
        var session = new TableSession<int>(initial);
        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Queue(TableIntent.SetShowVariants(true));

        TableTransitionResult<int> rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new float[0]));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(rejected.Frame.Revision, Is.EqualTo(1L));
        Assert.That(session.Current.ShowVariants, Is.False);

        TableTransitionResult<int> next = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(next.Applied, Is.True);
        Assert.That(session.Current.ShowVariants, Is.True);
        Assert.That(next.Frame.Revision, Is.EqualTo(2L));
    }

    [Test]
    public void A_failed_configuration_application_is_atomic_and_retries_on_the_next_layout()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "name" },
            new TableFilterState[0],
            sortColumnDefName: "name"));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        int applyAttempts = 0;

        TableConfigurationTransitionResult rejected = session.ApplyPendingConfiguration(_ =>
        {
            applyAttempts++;
            return false;
        });

        Assert.That(rejected.Applied, Is.False);
        Assert.That(applyAttempts, Is.EqualTo(1));
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));

        TableConfigurationTransitionResult applied = session.ApplyPendingConfiguration(configuration =>
        {
            applyAttempts++;
            return configuration.VisibleColumnDefNames.SequenceEqual(new[] { "value" });
        });

        Assert.That(applied.Applied, Is.True);
        Assert.That(applyAttempts, Is.EqualTo(2));
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value" }));
    }

    [Test]
    public void A_staged_configuration_is_not_published_until_its_frame_is_valid()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "name" },
            sortColumnDefName: "name"));
        session.PrepareFrame(new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        bool rolledBack = false;
        bool committed = false;

        TableConfigurationTransitionResult staged = session.StagePendingConfiguration(configuration =>
            new TableConfigurationTransaction(
                configuration,
                commit: () => committed = true,
                rollback: () => rolledBack = true));

        Assert.That(staged.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));
        Assert.That(committed, Is.False);

        TableTransitionResult<int> rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, Array.Empty<float>()));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(rolledBack, Is.True);
        Assert.That(committed, Is.False);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));

        TableConfigurationTransitionResult restaged = session.StagePendingConfiguration(configuration =>
            new TableConfigurationTransaction(
                configuration,
                commit: () => committed = true));
        Assert.That(restaged.Applied, Is.True);

        TableTransitionResult<int> applied = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(applied.Applied, Is.True);
        Assert.That(committed, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value" }));
    }

    [Test]
    public void The_effective_configuration_from_commit_is_published_not_the_requested_candidate()
    {
        var session = new TableSession<int>(new TableConfiguration(new[] { "name" }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "requested" }));

        TableConfiguration effective = new(new[] { "effective" });
        Assert.That(session.StagePendingConfiguration(_ =>
            new TableConfigurationTransaction(effective)).Applied, Is.True);

        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Configuration.VisibleColumnDefNames, Is.EqualTo(new[] { "effective" }));
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "effective" }));
    }

    [Test]
    public void A_layout_preparation_failure_rolls_back_staged_state_and_retains_the_intent()
    {
        var session = new TableSession<int>(new TableConfiguration(new[] { "name" }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        bool rolledBack = false;
        Assert.That(session.StagePendingConfiguration(configuration =>
            new TableConfigurationTransaction(configuration, rollback: () => rolledBack = true)).Applied, Is.True);

        TableTransitionResult<int> rejected = session.RejectStagedConfiguration(
            new InvalidOperationException("row refresh failed"));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(rolledBack, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));

        TableTransitionResult<int> retry = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(retry.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value" }));
    }

    [Test]
    public void Queuing_a_new_configuration_rolls_back_the_previous_staged_transition()
    {
        var session = new TableSession<int>(new TableConfiguration(new[] { "name" }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        bool rolledBack = false;

        Assert.That(session.StagePendingConfiguration(configuration =>
            new TableConfigurationTransaction(configuration, rollback: () => rolledBack = true)).Applied, Is.True);

        session.Queue(TableIntent.SetQuality(2));

        Assert.That(rolledBack, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));
        TableTransitionResult<int> applied = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(applied.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value" }));
        Assert.That(session.Current.Quality, Is.EqualTo(2));
    }

    [Test]
    public void A_commit_failure_rolls_back_the_staged_state_and_keeps_the_intent()
    {
        var session = new TableSession<int>(new TableConfiguration(new[] { "name" }));
        session.Queue(TableIntent.SetVisibleColumns(new[] { "value" }));
        bool rolledBack = false;
        int commitAttempts = 0;

        Assert.That(session.StagePendingConfiguration(configuration =>
            new TableConfigurationTransaction(
                configuration,
                commit: () =>
                {
                    commitAttempts++;
                    throw new InvalidOperationException("commit failed");
                },
                rollback: () => rolledBack = true)).Applied, Is.True);

        TableTransitionResult<int> rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(commitAttempts, Is.EqualTo(1));
        Assert.That(rolledBack, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name" }));

        session.Queue(TableIntent.SetVisibleColumns(new[] { "other" }));
        TableTransitionResult<int> next = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        Assert.That(next.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "other" }));
    }

    [Test]
    public void Row_added_after_layout_does_not_pair_with_stale_height()
    {
        var session = new TableSession<int>();

        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 10, 20 }, new[] { 20f, 20f }));

        Assert.That(initial.Applied, Is.True);
        Assert.That(initial.Frame.Rows, Has.Count.EqualTo(2));
        Assert.That(initial.Frame.RowHeights, Has.Count.EqualTo(2));

        session.Queue(TableIntent.MarkRowsDirty());

        TableTransitionResult<int> staleLayout = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 10, 20 }, new[] { 20f, 20f }));

        Assert.That(staleLayout.Applied, Is.True);
        Assert.That(staleLayout.Frame.Revision, Is.EqualTo(initial.Frame.Revision + 1L));
        Assert.That(staleLayout.Frame.Rows, Is.EqualTo(new[] { 10, 20 }));
        Assert.That(staleLayout.Frame.RowHeights, Is.EqualTo(new[] { 20f, 20f }));

        TableTransitionResult<int> nextLayout = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 10, 20, 30 }, new[] { 20f, 40f, 20f }));

        Assert.That(nextLayout.Applied, Is.True);
        Assert.That(nextLayout.Frame.Rows, Is.EqualTo(new[] { 10, 20, 30 }));
        Assert.That(nextLayout.Frame.RowHeights, Is.EqualTo(new[] { 20f, 40f, 20f }));
        Assert.That(nextLayout.Frame.Rows, Has.Count.EqualTo(nextLayout.Frame.RowHeights.Count));
    }

    [Test]
    public void A_frame_snapshots_visible_column_order_widths_and_pinned_count()
    {
        var session = new TableSession<int>(new TableConfiguration(new[] { "name", "value" }));

        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 },
            new[] { 20f },
            visibleColumnDefNames: new[] { "value", "name" },
            columnWidths: new[] { 80f, 120f },
            pinnedColumnCount: 1));

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Frame.VisibleColumnDefNames, Is.EqualTo(new[] { "value", "name" }));
        Assert.That(result.Frame.ColumnWidths, Is.EqualTo(new[] { 80f, 120f }));
        Assert.That(result.Frame.PinnedColumnCount, Is.EqualTo(1));
    }

    [Test]
    public void Column_layout_intents_are_deferred_and_coalesced_into_current()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "name", "value" },
            pinnedColumnDefNames: Array.Empty<string>(),
            columnWidths: new[] { 100f, 50f }));

        session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 },
            new[] { 20f },
            visibleColumnDefNames: new[] { "name", "value" },
            columnWidths: new[] { 100f, 50f },
            pinnedColumnCount: 0));

        session.Queue(TableIntent.ReorderColumns(new[] { "value", "name" }));
        session.Queue(TableIntent.SetColumnPinned("value", true));
        session.Queue(TableIntent.ResizeColumn("value", 80f));

        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "name", "value" }));

        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 },
            new[] { 20f },
            visibleColumnDefNames: new[] { "value", "name" },
            columnWidths: new[] { 80f, 100f },
            pinnedColumnCount: 1));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "value", "name" }));
        Assert.That(session.Current.PinnedColumnDefNames, Is.EqualTo(new[] { "value" }));
        Assert.That(session.Current.ColumnWidths, Is.EqualTo(new[] { 80f, 100f }));
    }

    [Test]
    public void Unpinning_the_first_column_then_pinning_a_later_column_preserves_pinned_order()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "first", "second", "later" },
            pinnedColumnDefNames: new[] { "first", "second" },
            columnWidths: new[] { 10f, 20f, 30f }));

        session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "first", "second", "later" },
            columnWidths: new[] { 10f, 20f, 30f }, pinnedColumnCount: 2));
        session.Queue(TableIntent.SetColumnPinned("first", false));
        session.Queue(TableIntent.SetColumnPinned("later", true));

        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "second", "later", "first" },
            columnWidths: new[] { 20f, 30f, 10f }, pinnedColumnCount: 2));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "second", "later", "first" }));
        Assert.That(session.Current.PinnedColumnDefNames, Is.EqualTo(new[] { "second", "later" }));
    }

    [Test]
    public void Pinning_a_later_column_when_none_are_pinned_moves_it_to_the_pinned_block()
    {
        var session = new TableSession<int>(new TableConfiguration(
            new[] { "first", "later" },
            columnWidths: new[] { 10f, 30f }));
        session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "first", "later" },
            columnWidths: new[] { 10f, 30f }));

        session.Queue(TableIntent.SetColumnPinned("later", true));
        TableTransitionResult<int> result = session.PrepareFrame(new TableLayoutMetrics(
            new[] { 0 }, new[] { 20f },
            visibleColumnDefNames: new[] { "later", "first" },
            columnWidths: new[] { 30f, 10f }, pinnedColumnCount: 1));

        Assert.That(result.Applied, Is.True);
        Assert.That(session.Current.VisibleColumnDefNames, Is.EqualTo(new[] { "later", "first" }));
        Assert.That(session.Current.PinnedColumnDefNames, Is.EqualTo(new[] { "later" }));
    }

    [Test]
    public void Rows_removed_after_layout_keep_the_published_frame_coherent()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1, 2, 3 }, new[] { 20f, 30f, 40f }));

        session.Queue(TableIntent.MarkRowsDirty());
        TableTransitionResult<int> staleLayout = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1, 2, 3 }, new[] { 20f, 30f, 40f }));

        Assert.That(staleLayout.Applied, Is.True);
        Assert.That(staleLayout.Frame.Revision, Is.EqualTo(initial.Frame.Revision + 1L));
        Assert.That(staleLayout.Frame.Rows, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(staleLayout.Frame.RowHeights, Is.EqualTo(new[] { 20f, 30f, 40f }));

        TableTransitionResult<int> nextLayout = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1 }, new[] { 20f }));

        Assert.That(nextLayout.Applied, Is.True);
        Assert.That(nextLayout.Frame.Rows, Is.EqualTo(new[] { 1 }));
        Assert.That(nextLayout.Frame.RowHeights, Is.EqualTo(new[] { 20f }));
        Assert.That(nextLayout.Frame.Rows, Has.Count.EqualTo(nextLayout.Frame.RowHeights.Count));
    }

    [Test]
    public void Variable_row_heights_are_copied_as_one_layout_generation()
    {
        var session = new TableSession<int>();
        var rows = new List<int> { 0, 1, 2 };
        var heights = new List<float> { 20f, 40f, 60f };

        TableTransitionResult<int> initial = session.PrepareFrame(new TableLayoutMetrics(rows, heights));

        rows[1] = 99;
        heights[1] = 120f;

        Assert.That(initial.Frame.Rows, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(initial.Frame.RowHeights, Is.EqualTo(new[] { 20f, 40f, 60f }));

        TableTransitionResult<int> nextLayout = session.PrepareFrame(new TableLayoutMetrics(rows, heights));

        Assert.That(nextLayout.Applied, Is.True);
        Assert.That(nextLayout.Frame.Rows, Is.EqualTo(new[] { 0, 99, 2 }));
        Assert.That(nextLayout.Frame.RowHeights, Is.EqualTo(new[] { 20f, 120f, 60f }));
        Assert.That(nextLayout.Frame.Rows, Has.Count.EqualTo(nextLayout.Frame.RowHeights.Count));
    }

    [Test]
    public void Pinned_rows_and_aggregate_heights_are_published_with_the_same_frame()
    {
        var session = new TableSession<int>();

        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(
                new[] { 0, 1, 2 },
                new[] { 30f, 40f, 50f },
                pinnedRowCount: 1,
                topRowsHeight: 30f,
                bottomRowsHeight: 90f,
                contentWidth: 640f,
                contentHeight: 120f,
                leftColumnsWidth: 140f));

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Frame.PinnedRowCount, Is.EqualTo(1));
        Assert.That(result.Frame.TopRowsHeight, Is.EqualTo(30f));
        Assert.That(result.Frame.BottomRowsHeight, Is.EqualTo(90f));
        Assert.That(result.Frame.ContentWidth, Is.EqualTo(640f));
        Assert.That(result.Frame.ContentHeight, Is.EqualTo(120f));
        Assert.That(result.Frame.LeftColumnsWidth, Is.EqualTo(140f));
        Assert.That(result.Frame.Rows, Has.Count.EqualTo(result.Frame.RowHeights.Count));
    }

    [Test]
    public void Empty_table_publishes_an_empty_coherent_frame()
    {
        var session = new TableSession<int>();

        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(
                new int[0],
                new float[0],
                pinnedRowCount: 0,
                topRowsHeight: 0f,
                bottomRowsHeight: 0f,
                contentWidth: 300f,
                contentHeight: 24f));

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Frame.Rows, Is.Empty);
        Assert.That(result.Frame.RowHeights, Is.Empty);
        Assert.That(result.Frame.PinnedRowCount, Is.Zero);
        Assert.That(result.Frame.ContentWidth, Is.EqualTo(300f));
        Assert.That(result.Frame.ContentHeight, Is.EqualTo(24f));
    }

    [Test]
    public void Queued_row_mutations_wait_for_the_next_frame_and_coalesce()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1, 2 }, new[] { 20f, 20f }));

        session.Queue(TableIntent.MarkRowsDirty());
        session.Queue(TableIntent.MarkRowsDirty());

        Assert.That(session.Frame.Revision, Is.EqualTo(initial.Frame.Revision));
        Assert.That(session.Frame.Rows, Is.EqualTo(new[] { 1, 2 }));

        TableTransitionResult<int> next = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 3, 4 }, new[] { 40f, 60f }));

        Assert.That(next.Applied, Is.True);
        Assert.That(next.Frame.Revision, Is.EqualTo(initial.Frame.Revision + 1L));
        Assert.That(next.Frame.Rows, Is.EqualTo(new[] { 3, 4 }));
        Assert.That(next.Frame.RowHeights, Is.EqualTo(new[] { 40f, 60f }));
    }

    [Test]
    public void Scrolling_past_the_last_row_returns_no_bottom_rows_without_touching_the_frame()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> result = session.PrepareFrame(
            new TableLayoutMetrics(
                new[] { 0, 1, 2, 3 },
                new[] { 30f, 40f, 50f, 60f },
                pinnedRowCount: 1));

        VisibleRowRange visible = result.Frame.GetVisibleBottomRows(scrollY: 1000f, viewportHeight: 200f);

        Assert.That(visible.Start, Is.EqualTo(4));
        Assert.That(visible.Count, Is.Zero);
        Assert.That(result.Frame.Rows, Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(result.Frame.RowHeights, Is.EqualTo(new[] { 30f, 40f, 50f, 60f }));
        Assert.That(result.Frame.Rows, Has.Count.EqualTo(result.Frame.RowHeights.Count));
    }

    [Test]
    public void Invalid_pinned_row_count_preserves_the_last_good_frame()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0, 1 }, new[] { 20f, 20f }));

        TableTransitionResult<int> rejected = null!;
        Assert.DoesNotThrow(() => rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0, 1 }, new[] { 20f, 20f }, pinnedRowCount: 3)));

        Assert.That(rejected.Applied, Is.False);
        Assert.That(rejected.Frame.Revision, Is.EqualTo(initial.Frame.Revision));
        Assert.That(rejected.Frame.Rows, Is.EqualTo(new[] { 0, 1 }));
        Assert.That(rejected.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Disposing_a_session_is_idempotent_and_does_not_publish_more_frames()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> initial = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0 }, new[] { 20f }));

        session.Dispose();
        Assert.DoesNotThrow(session.Dispose);
        session.Queue(TableIntent.MarkRowsDirty());

        TableTransitionResult<int> afterDispose = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1, 2 }, new[] { 20f, 20f }));

        Assert.That(afterDispose.Applied, Is.False);
        Assert.That(afterDispose.Frame.Revision, Is.EqualTo(initial.Frame.Revision));
        Assert.That(afterDispose.Frame.Rows, Is.EqualTo(new[] { 0 }));
        Assert.That(afterDispose.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Incompatible_layout_keeps_the_queued_intent_for_the_next_compatible_generation()
    {
        var session = new TableSession<int>();
        session.PrepareFrame(new TableLayoutMetrics(new[] { 0, 1 }, new[] { 20f, 20f }));
        session.Queue(TableIntent.MarkRowsDirty());

        TableTransitionResult<int> rejected = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0, 1 }, new[] { 20f, 20f }));

        Assert.That(rejected.Applied, Is.True);
        Assert.That(rejected.Frame.Rows, Is.EqualTo(new[] { 0, 1 }));

        TableTransitionResult<int> next = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 99, 98, 97 }, new[] { 20f, 40f, 20f }));

        Assert.That(next.Applied, Is.True);
        Assert.That(next.Frame.Rows, Is.EqualTo(new[] { 99, 98, 97 }));
        Assert.That(next.Frame.RowHeights, Is.EqualTo(new[] { 20f, 40f, 20f }));
    }

    [Test]
    public void A_mutation_requested_after_frame_capture_cannot_change_the_frame_consumed_by_a_draw()
    {
        var session = new TableSession<int>();
        TableTransitionResult<int> captured = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 0, 1 }, new[] { 20f, 40f }));

        session.Queue(TableIntent.MarkRowsDirty());
        IReadOnlyList<int> rowsConsumedByDraw = captured.Frame.Rows;

        Assert.That(session.Frame, Is.SameAs(captured.Frame));
        Assert.That(rowsConsumedByDraw, Is.EqualTo(new[] { 0, 1 }));
        Assert.That(captured.Frame.RowHeights, Is.EqualTo(new[] { 20f, 40f }));

        TableTransitionResult<int> next = session.PrepareFrame(
            new TableLayoutMetrics(new[] { 1, 0 }, new[] { 40f, 20f }));

        Assert.That(next.Applied, Is.True);
        Assert.That(next.Frame.Rows, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(next.Frame.RowHeights, Is.EqualTo(new[] { 40f, 20f }));
        Assert.That(rowsConsumedByDraw, Is.EqualTo(new[] { 0, 1 }));
    }
}
