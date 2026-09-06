using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Stats.ColumnWorkers;

namespace Stats;

internal sealed class TableFilterState
{
    internal TableFilterState(string columnDefName, string filterId, string label, string state)
    {
        ColumnDefName = columnDefName ?? "";
        FilterId = filterId ?? "";
        Label = label ?? "";
        State = state ?? "";
    }

    internal string ColumnDefName { get; }
    internal string FilterId { get; }
    internal string Label { get; }
    internal string State { get; }
}

internal enum SortDirection
{
    Ascending = 1,
    Descending = -1,
}

internal sealed class TableConfiguration
{
    private readonly IReadOnlyList<string> _visibleColumnDefNames;
    private readonly IReadOnlyList<TableFilterState> _filterStates;
    private readonly IReadOnlyList<string> _pinnedColumnDefNames;
    private readonly IReadOnlyList<float> _columnWidths;

    internal TableConfiguration(
        IEnumerable<string>? visibleColumnDefNames = null,
        IEnumerable<TableFilterState>? filterStates = null,
        bool showVariants = false,
        bool expandMultiValueCells = false,
        int quality = 0,
        string? sortColumnDefName = null,
        SortDirection sortDirection = SortDirection.Ascending,
        IEnumerable<string>? pinnedColumnDefNames = null,
        IEnumerable<float>? columnWidths = null)
    {
        List<string> visibleNames = [];
        List<string> duplicateNames = [];
        foreach (string? name in visibleColumnDefNames ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (visibleNames.Contains(name, StringComparer.Ordinal))
            {
                duplicateNames.Add(name);
                continue;
            }

            visibleNames.Add(name);
        }

        List<string> pinnedNames = [];
        foreach (string? name in pinnedColumnDefNames ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || pinnedNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            pinnedNames.Add(name);
        }

        _visibleColumnDefNames = new ReadOnlyCollection<string>(visibleNames);
        _pinnedColumnDefNames = new ReadOnlyCollection<string>(pinnedNames);
        _columnWidths = new ReadOnlyCollection<float>(
            (columnWidths ?? Array.Empty<float>()).ToList());
        _filterStates = new ReadOnlyCollection<TableFilterState>(
            (filterStates ?? [])
                .Where(state => state != null)
                .Select(CloneFilterState)
                .ToList());
        DuplicateVisibleColumnDefNames = new ReadOnlyCollection<string>(duplicateNames);
        ShowVariants = showVariants;
        ExpandMultiValueCells = expandMultiValueCells;
        Quality = quality;
        SortColumnDefName = string.IsNullOrWhiteSpace(sortColumnDefName) ? null : sortColumnDefName;
        SortDirection = sortDirection;
    }

    internal IReadOnlyList<string> VisibleColumnDefNames => _visibleColumnDefNames;
    internal IReadOnlyList<TableFilterState> FilterStates => _filterStates;
    internal IReadOnlyList<string> PinnedColumnDefNames => _pinnedColumnDefNames;
    internal IReadOnlyList<float> ColumnWidths => _columnWidths;
    internal IReadOnlyList<string> DuplicateVisibleColumnDefNames { get; }
    internal bool ShowVariants { get; }
    internal bool ExpandMultiValueCells { get; }
    internal int Quality { get; }
    internal string? SortColumnDefName { get; }
    internal SortDirection SortDirection { get; }

    internal static TableConfiguration ForPreset(
        IEnumerable<string>? visibleColumnDefNames,
        IEnumerable<TableFilterState>? filterStates,
        bool showVariants,
        bool expandMultiValueCells,
        int quality,
        string? sortColumnDefName,
        SortDirection sortDirection)
    {
        List<string> visibleNames = (visibleColumnDefNames ?? Array.Empty<string>())
            .Where(name => string.IsNullOrWhiteSpace(name) == false)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TableConfiguration(
            visibleNames,
            filterStates,
            showVariants,
            expandMultiValueCells,
            quality,
            sortColumnDefName,
            sortDirection,
            pinnedColumnDefNames: visibleNames.Take(1),
            // Presets intentionally restore the column set, not transient
            // manual widths. The next layout recalculates them from cells.
            columnWidths: Array.Empty<float>());
    }

    internal TableConfiguration With(
        IEnumerable<string>? visibleColumnDefNames = null,
        IEnumerable<TableFilterState>? filterStates = null,
        bool? showVariants = null,
        bool? expandMultiValueCells = null,
        int? quality = null,
        string? sortColumnDefName = null,
        bool replaceSortColumn = false,
        SortDirection? sortDirection = null,
        IEnumerable<string>? pinnedColumnDefNames = null,
        IEnumerable<float>? columnWidths = null)
    {
        return new TableConfiguration(
            visibleColumnDefNames ?? VisibleColumnDefNames,
            filterStates ?? FilterStates,
            showVariants ?? ShowVariants,
            expandMultiValueCells ?? ExpandMultiValueCells,
            quality ?? Quality,
            replaceSortColumn ? sortColumnDefName : SortColumnDefName,
            sortDirection ?? SortDirection,
            pinnedColumnDefNames ?? PinnedColumnDefNames,
            columnWidths ?? ColumnWidths);
    }

    internal bool EquivalentTo(TableConfiguration other)
    {
        return ShowVariants == other.ShowVariants
            && ExpandMultiValueCells == other.ExpandMultiValueCells
            && Quality == other.Quality
            && SortDirection == other.SortDirection
            && string.Equals(SortColumnDefName, other.SortColumnDefName, StringComparison.Ordinal)
            && VisibleColumnDefNames.SequenceEqual(other.VisibleColumnDefNames, StringComparer.Ordinal)
            && PinnedColumnDefNames.SequenceEqual(other.PinnedColumnDefNames, StringComparer.Ordinal)
            && ColumnWidths.SequenceEqual(other.ColumnWidths)
            && FilterStatesEqual(FilterStates, other.FilterStates);
    }

    internal static TableFilterState CloneFilterState(TableFilterState source)
    {
        return new TableFilterState(source.ColumnDefName, source.FilterId, source.Label, source.State);
    }

    private static bool FilterStatesEqual(IReadOnlyList<TableFilterState> left, IReadOnlyList<TableFilterState> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            TableFilterState a = left[i];
            TableFilterState b = right[i];
            if (!string.Equals(a.ColumnDefName, b.ColumnDefName, StringComparison.Ordinal)
                || !string.Equals(a.FilterId, b.FilterId, StringComparison.Ordinal)
                || !string.Equals(a.Label, b.Label, StringComparison.Ordinal)
                || !string.Equals(a.State, b.State, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class TableIntent
{
    private enum IntentKind
    {
        MarkRowsDirty,
        ApplyConfiguration,
        SetVisibleColumns,
        SetShowVariants,
        SetExpandedMultiValueCells,
        SetQuality,
        SetSort,
        SetFilters,
        SetPinnedColumns,
        ReorderColumns,
        SetColumnPinned,
        ResizeColumn,
        ResetColumnWidth,
    }

    private readonly IntentKind _kind;
    private readonly TableConfiguration? _configuration;
    private readonly IReadOnlyList<string>? _names;
    private readonly IReadOnlyList<TableFilterState>? _filters;
    private readonly bool _boolValue;
    private readonly int _quality;
    private readonly string? _sortColumn;
    private readonly SortDirection _sortDirection;
    private readonly string? _columnName;
    private readonly float _columnWidth;
    private readonly bool _pinColumn;

    private TableIntent(IntentKind kind, TableConfiguration? configuration = null)
    {
        _kind = kind;
        _configuration = configuration;
    }

    private TableIntent(IEnumerable<string> names, IntentKind kind)
    {
        _kind = kind;
        _names = new ReadOnlyCollection<string>(names.Where(name => name != null).ToList());
    }

    private TableIntent(bool value, IntentKind kind)
    {
        _kind = kind;
        _boolValue = value;
    }

    private TableIntent(int quality)
    {
        _kind = IntentKind.SetQuality;
        _quality = quality;
    }

    private TableIntent(string? sortColumn, SortDirection sortDirection)
    {
        _kind = IntentKind.SetSort;
        _sortColumn = sortColumn;
        _sortDirection = sortDirection;
    }

    private TableIntent(string columnName, float columnWidth, IntentKind kind)
    {
        _kind = kind;
        _columnName = columnName ?? "";
        _columnWidth = columnWidth;
    }

    private TableIntent(string columnName, bool pinColumn)
    {
        _kind = IntentKind.SetColumnPinned;
        _columnName = columnName ?? "";
        _pinColumn = pinColumn;
    }

    private TableIntent(IEnumerable<TableFilterState> filters)
    {
        _kind = IntentKind.SetFilters;
        _filters = new ReadOnlyCollection<TableFilterState>(
            filters.Where(filter => filter != null).Select(TableConfiguration.CloneFilterState).ToList());
    }

    internal static TableIntent MarkRowsDirty() => new(IntentKind.MarkRowsDirty);

    internal static TableIntent ApplyConfiguration(TableConfiguration configuration)
    {
        return new TableIntent(IntentKind.ApplyConfiguration, configuration: configuration ?? throw new ArgumentNullException(nameof(configuration)));
    }

    internal static TableIntent SetVisibleColumns(IEnumerable<string> names) => new(names ?? [], IntentKind.SetVisibleColumns);
    internal static TableIntent SetShowVariants(bool value) => new(value, IntentKind.SetShowVariants);
    internal static TableIntent SetExpandedMultiValueCells(bool value) => new(value, IntentKind.SetExpandedMultiValueCells);
    internal static TableIntent SetQuality(int quality) => new(quality);
    internal static TableIntent SetSort(string? columnDefName, SortDirection direction) => new(columnDefName, direction);
    internal static TableIntent SetFilters(IEnumerable<TableFilterState> filters) => new(filters ?? []);
    internal static TableIntent SetPinnedColumns(IEnumerable<string> names) => new(names ?? [], IntentKind.SetPinnedColumns);
    internal static TableIntent ReorderColumns(IEnumerable<string> names) => new(names ?? [], IntentKind.ReorderColumns);
    internal static TableIntent SetColumnPinned(string name, bool pinned) => new(name, pinned);
    internal static TableIntent ResizeColumn(string name, float width) => new(name, width, IntentKind.ResizeColumn);
    internal static TableIntent ResetColumnWidth(string name) => new(name, 0f, IntentKind.ResetColumnWidth);

    internal bool IsConfigurationIntent => _kind != IntentKind.MarkRowsDirty;
    internal bool IsRowsDirtyIntent => _kind == IntentKind.MarkRowsDirty;

    internal TableConfiguration ApplyConfigurationTo(TableConfiguration current)
    {
        return _kind switch
        {
            IntentKind.ApplyConfiguration => _configuration!,
            IntentKind.SetVisibleColumns => ReorderColumns(current, _names!),
            IntentKind.SetShowVariants => current.With(showVariants: _boolValue),
            IntentKind.SetExpandedMultiValueCells => current.With(expandMultiValueCells: _boolValue),
            IntentKind.SetQuality => current.With(quality: _quality),
            IntentKind.SetSort => current.With(sortColumnDefName: _sortColumn, replaceSortColumn: true, sortDirection: _sortDirection),
            IntentKind.SetFilters => current.With(filterStates: _filters),
            IntentKind.SetPinnedColumns => current.With(pinnedColumnDefNames: _names),
            IntentKind.ReorderColumns => ReorderColumns(current, _names!),
            IntentKind.SetColumnPinned => ApplyPinnedColumn(current, _columnName!, _pinColumn),
            IntentKind.ResizeColumn => current.With(
                columnWidths: SetColumnWidth(current, _columnName!, _columnWidth)),
            IntentKind.ResetColumnWidth => current.With(
                columnWidths: SetColumnWidth(current, _columnName!, 0f)),
            _ => current,
        };
    }

    private static IReadOnlyList<string> SetPinnedColumn(
        IReadOnlyList<string> current,
        string columnName,
        bool pinned)
    {
        List<string> result = current.ToList();
        if (pinned)
        {
            if (result.Contains(columnName, StringComparer.Ordinal) == false)
            {
                result.Add(columnName);
            }
        }
        else
        {
            result.RemoveAll(name => string.Equals(name, columnName, StringComparison.Ordinal));
        }

        return result;
    }

    private static IReadOnlyList<float> SetColumnWidth(
        TableConfiguration current,
        string columnName,
        float width)
    {
        List<float> result = current.ColumnWidths.ToList();
        while (result.Count < current.VisibleColumnDefNames.Count)
        {
            result.Add(0f);
        }

        int index = -1;
        for (int i = 0; i < current.VisibleColumnDefNames.Count; i++)
        {
            if (string.Equals(current.VisibleColumnDefNames[i], columnName, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            result[index] = Math.Max(0f, width);
        }

        return result;
    }

    private static TableConfiguration ApplyPinnedColumn(
        TableConfiguration current,
        string columnName,
        bool pinned)
    {
        List<string> pinnedNames = SetPinnedColumn(current.PinnedColumnDefNames, columnName, pinned).ToList();
        List<string> visibleNames = current.VisibleColumnDefNames.ToList();
        int currentIndex = visibleNames.FindIndex(name =>
            string.Equals(name, columnName, StringComparison.Ordinal));
        if (currentIndex >= 0)
        {
            visibleNames.RemoveAt(currentIndex);
            int pinnedCount = visibleNames.Count(name => pinnedNames.Contains(name, StringComparer.Ordinal));
            int insertionIndex = pinned ? pinnedCount : pinnedCount;
            visibleNames.Insert(Math.Min(insertionIndex, visibleNames.Count), columnName);
        }

        return ReorderColumns(current.With(pinnedColumnDefNames: pinnedNames), visibleNames);
    }

    private static TableConfiguration ReorderColumns(
        TableConfiguration current,
        IReadOnlyList<string> names)
    {
        List<string> requestedNames = names
            .Where(name => string.IsNullOrWhiteSpace(name) == false)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        HashSet<string> pinnedNames = new(
            current.PinnedColumnDefNames.Where(requestedNames.Contains),
            StringComparer.Ordinal);
        List<string> orderedNames = requestedNames
            .Where(pinnedNames.Contains)
            .Concat(requestedNames.Where(name => pinnedNames.Contains(name) == false))
            .ToList();
        List<float> widths = [];
        for (int i = 0; i < orderedNames.Count; i++)
        {
            int oldIndex = -1;
            for (int j = 0; j < current.VisibleColumnDefNames.Count; j++)
            {
                if (string.Equals(current.VisibleColumnDefNames[j], orderedNames[i], StringComparison.Ordinal))
                {
                    oldIndex = j;
                    break;
                }
            }

            widths.Add(oldIndex >= 0 && oldIndex < current.ColumnWidths.Count
                ? current.ColumnWidths[oldIndex]
                : 0f);
        }

        return current.With(
            visibleColumnDefNames: orderedNames,
            pinnedColumnDefNames: orderedNames.Where(pinnedNames.Contains),
            columnWidths: widths);
    }
}

internal sealed class TableLayoutMetrics
{
    internal TableLayoutMetrics(
        IReadOnlyList<int> rows,
        IReadOnlyList<float> rowHeights,
        int pinnedRowCount = 0,
        float topRowsHeight = 0f,
        float bottomRowsHeight = 0f,
        float contentWidth = 0f,
        float contentHeight = 0f,
        float leftColumnsWidth = 0f,
        IReadOnlyList<string>? visibleColumnDefNames = null,
        IReadOnlyList<float>? columnWidths = null,
        int pinnedColumnCount = 0)
    {
        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (rowHeights == null)
        {
            throw new ArgumentNullException(nameof(rowHeights));
        }

        Rows = new ReadOnlyCollection<int>(new List<int>(rows));
        RowHeights = new ReadOnlyCollection<float>(new List<float>(rowHeights));
        VisibleColumnDefNames = new ReadOnlyCollection<string>(
            new List<string>(visibleColumnDefNames ?? Array.Empty<string>()));
        ColumnWidths = new ReadOnlyCollection<float>(
            new List<float>(columnWidths ?? Array.Empty<float>()));
        PinnedRowCount = pinnedRowCount;
        PinnedColumnCount = pinnedColumnCount;
        TopRowsHeight = topRowsHeight;
        BottomRowsHeight = bottomRowsHeight;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        LeftColumnsWidth = leftColumnsWidth;
    }

    internal IReadOnlyList<int> Rows { get; }
    internal IReadOnlyList<float> RowHeights { get; }
    internal IReadOnlyList<string> VisibleColumnDefNames { get; }
    internal IReadOnlyList<float> ColumnWidths { get; }
    internal int PinnedRowCount { get; }
    internal int PinnedColumnCount { get; }
    internal float TopRowsHeight { get; }
    internal float BottomRowsHeight { get; }
    internal float ContentWidth { get; }
    internal float ContentHeight { get; }
    internal float LeftColumnsWidth { get; }
}

internal sealed class TableFrame<TObject>
{
    private TableFrame(
        IReadOnlyList<int> rows,
        IReadOnlyList<float> rowHeights,
        IReadOnlyList<string> visibleColumnDefNames,
        IReadOnlyList<float> columnWidths,
        int pinnedRowCount,
        int pinnedColumnCount,
        float topRowsHeight,
        float bottomRowsHeight,
        float contentWidth,
        float contentHeight,
        float leftColumnsWidth,
        long revision)
    {
        Rows = new ReadOnlyCollection<int>(new List<int>(rows));
        RowHeights = new ReadOnlyCollection<float>(new List<float>(rowHeights));
        VisibleColumnDefNames = new ReadOnlyCollection<string>(new List<string>(visibleColumnDefNames));
        ColumnWidths = new ReadOnlyCollection<float>(new List<float>(columnWidths));
        PinnedRowCount = pinnedRowCount;
        PinnedColumnCount = pinnedColumnCount;
        TopRowsHeight = topRowsHeight;
        BottomRowsHeight = bottomRowsHeight;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        LeftColumnsWidth = leftColumnsWidth;
        Revision = revision;
    }

    internal static TableFrame<TObject> Empty { get; } = new(
        [], [], [], [], 0, 0, 0f, 0f, 0f, 0f, 0f, 0L);

    internal IReadOnlyList<int> Rows { get; }
    internal IReadOnlyList<float> RowHeights { get; }
    internal IReadOnlyList<string> VisibleColumnDefNames { get; }
    internal IReadOnlyList<float> ColumnWidths { get; }
    internal int PinnedRowCount { get; }
    internal int PinnedColumnCount { get; }
    internal float TopRowsHeight { get; }
    internal float BottomRowsHeight { get; }
    internal float ContentWidth { get; }
    internal float ContentHeight { get; }
    internal float LeftColumnsWidth { get; }
    internal long Revision { get; }

    internal VisibleRowRange GetVisibleBottomRows(float scrollY, float viewportHeight)
    {
        int start = PinnedRowCount;
        float heightBeforeStart = 0f;
        while (start < Rows.Count)
        {
            float rowHeight = RowHeights[start];
            if (heightBeforeStart + rowHeight > scrollY)
            {
                break;
            }

            heightBeforeStart += rowHeight;
            start++;
        }

        float firstRowY = heightBeforeStart - scrollY;
        int count = 0;
        float visibleHeight = firstRowY;
        while (start + count < Rows.Count && visibleHeight < viewportHeight)
        {
            visibleHeight += RowHeights[start + count];
            count++;
        }

        return new VisibleRowRange(start, count, firstRowY);
    }

    internal static TableFrame<TObject> Create(TableLayoutMetrics metrics, long revision)
    {
        if (metrics.Rows.Count != metrics.RowHeights.Count)
        {
            throw new ArgumentException("Rows and row heights must be from the same layout generation.", nameof(metrics));
        }

        if (metrics.VisibleColumnDefNames.Count != metrics.ColumnWidths.Count)
        {
            throw new ArgumentException("Visible columns and column widths must be from the same layout generation.", nameof(metrics));
        }

        if (metrics.PinnedColumnCount < 0 || metrics.PinnedColumnCount > metrics.VisibleColumnDefNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Pinned columns must be within the visible column range.");
        }

        if (metrics.PinnedRowCount < 0 || metrics.PinnedRowCount > metrics.Rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Pinned rows must be within the row range.");
        }

        return new TableFrame<TObject>(
            metrics.Rows,
            metrics.RowHeights,
            metrics.VisibleColumnDefNames,
            metrics.ColumnWidths,
            metrics.PinnedRowCount,
            metrics.PinnedColumnCount,
            metrics.TopRowsHeight,
            metrics.BottomRowsHeight,
            metrics.ContentWidth,
            metrics.ContentHeight,
            metrics.LeftColumnsWidth,
            revision);
    }
}

internal readonly struct VisibleRowRange
{
    internal VisibleRowRange(int start, int count, float firstRowY)
    {
        Start = start;
        Count = count;
        FirstRowY = firstRowY;
    }

    internal int Start { get; }
    internal int Count { get; }
    internal float FirstRowY { get; }
}

internal sealed class TableTransitionResult<TObject>
{
    internal TableTransitionResult(TableFrame<TObject> frame, bool applied, IReadOnlyList<string> diagnostics, TableConfiguration configuration)
    {
        Frame = frame;
        Applied = applied;
        Diagnostics = diagnostics;
        Configuration = configuration;
    }

    internal TableFrame<TObject> Frame { get; }
    internal bool Applied { get; }
    internal IReadOnlyList<string> Diagnostics { get; }
    internal TableConfiguration Configuration { get; }
}

internal sealed class TableConfigurationTransitionResult
{
    internal TableConfigurationTransitionResult(TableConfiguration configuration, bool applied, IReadOnlyList<string> diagnostics)
    {
        Configuration = configuration;
        Applied = applied;
        Diagnostics = diagnostics;
    }

    internal TableConfiguration Configuration { get; }
    internal bool Applied { get; }
    internal IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// A configuration mutation that has been staged against the live table but is
/// not visible through <see cref="TableSession{TObject}.Current"/> until the
/// next frame is published.
/// </summary>
internal sealed class TableConfigurationTransaction : IDisposable
{
    private readonly Action? _commit;
    private readonly Action? _rollback;
    private bool _completed;

    internal TableConfigurationTransaction(
        TableConfiguration configuration,
        Action? commit = null,
        Action? rollback = null,
        IEnumerable<string>? diagnostics = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _commit = commit;
        _rollback = rollback;
        Diagnostics = new ReadOnlyCollection<string>((diagnostics ?? []).ToList());
    }

    internal TableConfiguration Configuration { get; }
    internal IReadOnlyList<string> Diagnostics { get; }

    internal void Commit()
    {
        if (_completed)
        {
            return;
        }

        _commit?.Invoke();
        _completed = true;
    }

    internal void Rollback()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _rollback?.Invoke();
    }

    public void Dispose() => Rollback();
}

internal sealed class TableSession<TObject> : IDisposable
{
    private readonly List<TableIntent> _pendingIntents = new();
    private TableFrame<TObject> _frame = TableFrame<TObject>.Empty;
    private TableConfiguration _current;
    private readonly ColumnRegistry<TObject>? _columnRegistry;
    private ColumnRegistry<TObject>.Transition? _stagedColumnTransition;
    private TableConfiguration? _stagedConfiguration;
    private TableConfigurationTransaction? _stagedTransaction;
    private bool _disposed;

    internal TableSession()
        : this(new TableConfiguration())
    {
    }

    internal TableSession(TableConfiguration initialConfiguration)
    {
        _current = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
    }

    internal TableSession(
        TableConfiguration initialConfiguration,
        IEnumerable<TableColumnDefinition<TObject>> columnDefinitions,
        Action? onFilterChanged = null)
        : this(initialConfiguration)
    {
        _columnRegistry = new ColumnRegistry<TObject>(
            columnDefinitions ?? throw new ArgumentNullException(nameof(columnDefinitions)),
            () =>
            {
                if (_disposed == false)
                {
                    _pendingIntents.Add(TableIntent.MarkRowsDirty());
                    onFilterChanged?.Invoke();
                }
            });
    }

    internal TableFrame<TObject> Frame => _frame;
    internal TableConfiguration Current => _current;
    internal IReadOnlyList<string> VisibleColumnDefNames =>
        _stagedColumnTransition?.VisibleNames
        ?? _columnRegistry?.VisibleNames
        ?? _current.VisibleColumnDefNames;
    internal IReadOnlyList<string> FilterOnlyColumnDefNames =>
        _stagedColumnTransition?.FilterOnlyNames
        ?? _columnRegistry?.FilterOnlyNames
        ?? Array.Empty<string>();
    internal IReadOnlyCollection<string> KnownColumnDefNames =>
        _columnRegistry?.KnownNames
        ?? Array.Empty<string>();

    internal bool TryGetColumnWorker(string defName, out ColumnWorker<TObject>? worker)
    {
        if (_columnRegistry == null)
        {
            worker = null;
            return false;
        }

        if (_stagedColumnTransition?.TryGetWorker(defName, out worker) == true)
        {
            return true;
        }

        return _columnRegistry.TryGetWorker(defName, out worker);
    }

    internal bool TryGetColumnFields(string defName, out ICollection<CellField>? fields)
    {
        if (_columnRegistry == null)
        {
            fields = null;
            return false;
        }

        if (_stagedColumnTransition?.TryGetFields(defName, out fields) == true)
        {
            return true;
        }

        return _columnRegistry.TryGetFields(defName, out fields);
    }

    internal ColumnWorker<TObject> AcquireColumnWorker(string defName)
    {
        if (_columnRegistry == null)
        {
            throw new InvalidOperationException("This table session has no column registry.");
        }

        return _columnRegistry.Acquire(defName);
    }

    internal void SetVisibleColumnWorkers(IEnumerable<string> names)
    {
        if (_columnRegistry == null)
        {
            return;
        }

        _columnRegistry.SetVisibleNames(names);
    }

    internal void SetColumnWorkerRoles(IEnumerable<string> visibleNames, IEnumerable<string> filterColumnNames)
    {
        if (_columnRegistry == null)
        {
            return;
        }

        using ColumnRegistry<TObject>.Transition transition = _columnRegistry.Stage(visibleNames, filterColumnNames);
        transition.Commit();
        transition.FinalizeCommit();
    }

    internal void SetFilterOnlyColumnWorkers(IEnumerable<string> filterColumnNames)
    {
        if (_columnRegistry == null)
        {
            return;
        }

        // Filter-window cleanup changes only the hidden role. The registry's
        // committed visible names remain authoritative for this out-of-band
        // cleanup, so visible workers and their subscriptions are untouched.
        SetColumnWorkerRoles(_columnRegistry.VisibleNames, filterColumnNames);
    }

    internal void ResetColumnWorkers()
    {
        _columnRegistry?.ResetWorkers();
    }

    internal void Queue(TableIntent intent)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_disposed)
        {
            return;
        }

        // A new configuration supersedes a live staged transaction. Roll it
        // back before accepting the new intent so the next stage starts from
        // the last published configuration and live table state.
        if (intent.IsConfigurationIntent && _stagedTransaction != null)
        {
            RollbackStagedConfiguration();
        }

        _pendingIntents.Add(intent);
    }

    internal TableConfigurationTransitionResult StagePendingConfiguration(
        Func<TableConfiguration, TableConfigurationTransaction?> stage)
    {
        if (stage == null)
        {
            throw new ArgumentNullException(nameof(stage));
        }

        if (_disposed)
        {
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(new[] { "Table session has been disposed." }));
        }

        if (_pendingIntents.Any(intent => intent.IsConfigurationIntent) == false)
        {
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(Array.Empty<string>()));
        }

        if (_stagedTransaction != null)
        {
            return new TableConfigurationTransitionResult(
                _stagedConfiguration!,
                true,
                _stagedTransaction.Diagnostics);
        }

        TableConfiguration candidate = BuildCandidateConfiguration(out List<string> diagnostics);
        if (_current.EquivalentTo(candidate))
        {
            RemoveConfigurationIntents();
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(diagnostics));
        }

        ColumnRegistry<TObject>.Transition? columnTransition = null;
        try
        {
            columnTransition = StageColumnTransition(candidate);
            _stagedColumnTransition = columnTransition;
        }
        catch (Exception exception)
        {
            diagnostics.Add($"The staged column registry was rejected: {exception.Message}");
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(diagnostics));
        }

        TableConfigurationTransaction? tableTransaction;
        try
        {
            tableTransaction = stage(candidate);
        }
        catch (Exception exception)
        {
            columnTransition?.Rollback();
            _stagedColumnTransition = null;
            diagnostics.Add($"The staged table configuration was rejected: {exception.Message}");
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(diagnostics));
        }

        if (tableTransaction == null)
        {
            columnTransition?.Rollback();
            _stagedColumnTransition = null;
            diagnostics.Add("The table rejected the staged configuration.");
            return new TableConfigurationTransitionResult(
                _current,
                false,
                new ReadOnlyCollection<string>(diagnostics));
        }

        diagnostics.AddRange(tableTransaction.Diagnostics);
        _stagedConfiguration = tableTransaction.Configuration;
        _stagedTransaction = new TableConfigurationTransaction(
            tableTransaction.Configuration,
            commit: () =>
            {
                // Swap the registry first, but retain retired workers until
                // the table transaction has also committed.
                columnTransition?.Commit();
                tableTransaction.Commit();
                columnTransition?.FinalizeCommit();
            },
            rollback: () =>
            {
                columnTransition?.Rollback();
                tableTransaction.Rollback();
            },
            diagnostics: tableTransaction.Diagnostics);
        return new TableConfigurationTransitionResult(
            _stagedConfiguration,
            true,
            new ReadOnlyCollection<string>(diagnostics));
    }

    // Kept for callers that need to apply a configuration outside a frame
    // (notably compatibility code). Production drawing uses the staged method
    // followed by PrepareFrame so configuration and frame publication remain a
    // single transaction.
    internal TableConfigurationTransitionResult ApplyPendingConfiguration(Func<TableConfiguration, bool> apply)
    {
        if (apply == null)
        {
            throw new ArgumentNullException(nameof(apply));
        }

        TableConfigurationTransitionResult result = StagePendingConfiguration(configuration =>
            apply(configuration)
                ? new TableConfigurationTransaction(configuration)
                : null);
        if (result.Applied)
        {
            if (TryCommitStagedConfiguration(out string? failure))
            {
                return new TableConfigurationTransitionResult(
                    _current,
                    true,
                    result.Diagnostics);
            }

            return new TableConfigurationTransitionResult(
                _current,
                false,
                result.Diagnostics.Concat(new[] { failure! }).ToArray());
        }

        return result;
    }

    internal TableTransitionResult<TObject> PrepareFrame(TableLayoutMetrics metrics)
    {
        if (metrics == null)
        {
            throw new ArgumentNullException(nameof(metrics));
        }

        if (_disposed)
        {
            return Result(false, new[] { "Table session has been disposed." });
        }

        if (metrics.Rows.Count != metrics.RowHeights.Count
            || metrics.PinnedRowCount < 0
            || metrics.PinnedRowCount > metrics.Rows.Count)
        {
            return RejectFrame(new[]
            {
                "Rejected layout generation because rows, row heights, and pinned rows were not coherent."
            });
        }

        List<string> diagnostics;
        TableConfiguration candidateConfiguration;
        if (_stagedConfiguration != null)
        {
            candidateConfiguration = _stagedConfiguration;
            diagnostics = _stagedTransaction?.Diagnostics.ToList() ?? [];
        }
        else
        {
            candidateConfiguration = BuildCandidateConfiguration(out diagnostics);
        }

        if (candidateConfiguration.VisibleColumnDefNames.SequenceEqual(
                metrics.VisibleColumnDefNames,
                StringComparer.Ordinal)
            && candidateConfiguration.ColumnWidths.Count != metrics.ColumnWidths.Count)
        {
            candidateConfiguration = candidateConfiguration.With(columnWidths: metrics.ColumnWidths);
        }
        // Layout metrics are the only authoritative row/height generation.
        // Queued row intents mark the table dirty, but their copied row list
        // must never be paired with heights from a later or earlier layout.
        TableLayoutMetrics candidateMetrics = metrics;
        bool hasQueuedRowsDirtyIntent = _pendingIntents.Any(intent => intent.IsRowsDirtyIntent);

        bool configurationChanged = _current.EquivalentTo(candidateConfiguration) == false;
        if (!configurationChanged && IsSameFrame(candidateMetrics)
            && _stagedTransaction == null
            && hasQueuedRowsDirtyIntent == false)
        {
            _pendingIntents.Clear();
            return Result(false, diagnostics);
        }

        TableFrame<TObject> nextFrame;
        try
        {
            nextFrame = TableFrame<TObject>.Create(candidateMetrics, _frame.Revision + 1L);
        }
        catch (ArgumentException exception)
        {
            return RejectFrame(diagnostics.Concat(new[] { exception.Message }));
        }

        bool hadStagedConfiguration = _stagedTransaction != null;
        if (_stagedColumnTransition == null)
        {
            try
            {
                _stagedColumnTransition = StageColumnTransition(candidateConfiguration);
            }
            catch (Exception exception)
            {
                return RejectFrame(diagnostics.Concat(new[]
                {
                    $"Rejected staged column workers: {exception.Message}"
                }));
            }
        }

        if (TryCommitStagedConfiguration(out string? commitFailure) == false)
        {
            return Result(false, diagnostics.Concat(new[] { commitFailure! }));
        }

        if (hadStagedConfiguration == false && _stagedColumnTransition != null)
        {
            try
            {
                _stagedColumnTransition.Commit();
                _stagedColumnTransition.FinalizeCommit();
                _stagedColumnTransition = null;
            }
            catch (Exception exception)
            {
                RollbackStagedConfiguration();
                return Result(false, diagnostics.Concat(new[]
                {
                    $"Rejected staged column workers during commit: {exception.Message}"
                }));
            }
        }

        _current = candidateConfiguration;
        _frame = nextFrame;
        _pendingIntents.Clear();
        return new TableTransitionResult<TObject>(
            nextFrame,
            true,
            new ReadOnlyCollection<string>(diagnostics),
            _current);
    }

    private TableTransitionResult<TObject> RejectFrame(IEnumerable<string> diagnostics)
    {
        RollbackStagedConfiguration();
        return Result(false, diagnostics);
    }

    internal TableTransitionResult<TObject> RejectStagedConfiguration(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return RejectFrame(new[]
        {
            $"Rejected staged table configuration after layout preparation failed: {exception.Message}"
        });
    }

    private bool TryCommitStagedConfiguration(out string? failure)
    {
        failure = null;
        if (_stagedTransaction == null)
        {
            return true;
        }

        TableConfigurationTransaction transaction = _stagedTransaction;
        TableConfiguration committedConfiguration = _stagedConfiguration!;
        try
        {
            transaction.Commit();
        }
        catch (Exception exception)
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // The original published state remains authoritative even if
                // an adapter cannot complete its rollback hook.
            }

            _stagedTransaction = null;
            _stagedConfiguration = null;
            _stagedColumnTransition = null;
            failure = $"The staged table configuration could not be committed: {exception.Message}";
            return false;
        }

        _stagedTransaction = null;
        _stagedConfiguration = null;
        _stagedColumnTransition = null;
        _current = committedConfiguration;
        RemoveConfigurationIntents();
        return true;
    }

    private void RollbackStagedConfiguration()
    {
        if (_stagedTransaction == null)
        {
            _stagedColumnTransition?.Rollback();
            _stagedColumnTransition = null;
            _stagedConfiguration = null;
            return;
        }

        TableConfigurationTransaction transaction = _stagedTransaction;
        _stagedTransaction = null;
        _stagedConfiguration = null;
        try
        {
            transaction.Rollback();
        }
        catch
        {
            // Rollback is best effort. The original frame/configuration remain
            // published even when an adapter's cleanup hook fails.
        }
    }

    private TableTransitionResult<TObject> Result(bool applied, IEnumerable<string> diagnostics)
    {
        return new TableTransitionResult<TObject>(
            _frame,
            applied,
            new ReadOnlyCollection<string>(diagnostics.ToList()),
            _current);
    }

    private TableConfiguration BuildCandidateConfiguration(out List<string> diagnostics)
    {
        diagnostics = [];
        TableConfiguration candidate = _current;
        foreach (TableIntent intent in _pendingIntents.Where(intent => intent.IsConfigurationIntent))
        {
            candidate = intent.ApplyConfigurationTo(candidate);
        }

        if (_columnRegistry != null)
        {
            candidate = NormalizeKnownColumnIdentifiers(candidate, diagnostics);
        }
        else
        {
            diagnostics.AddRange(candidate.DuplicateVisibleColumnDefNames.Select(name => $"Removed duplicate column identifier \"{name}\"."));
        }

        List<TableFilterState> validFilters = [];
        HashSet<string> filterIds = new(StringComparer.Ordinal);
        foreach (TableFilterState state in candidate.FilterStates)
        {
            if (string.IsNullOrWhiteSpace(state.ColumnDefName)
                && string.IsNullOrWhiteSpace(state.FilterId)
                && string.IsNullOrWhiteSpace(state.Label))
            {
                diagnostics.Add("Skipped malformed filter state.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(state.FilterId) && filterIds.Add(state.FilterId) == false)
            {
                diagnostics.Add($"Skipped duplicate filter identifier \"{state.FilterId}\".");
                continue;
            }

            validFilters.Add(TableConfiguration.CloneFilterState(state));
        }

        if (validFilters.Count != candidate.FilterStates.Count)
        {
            candidate = new TableConfiguration(
                candidate.VisibleColumnDefNames,
                validFilters,
                candidate.ShowVariants,
                candidate.ExpandMultiValueCells,
                candidate.Quality,
                 candidate.SortColumnDefName,
                 candidate.SortDirection,
                 candidate.PinnedColumnDefNames,
                 candidate.ColumnWidths);
        }

        string? fallbackSortColumn = candidate.VisibleColumnDefNames.Count == 0
            ? null
            : candidate.VisibleColumnDefNames.Contains(candidate.SortColumnDefName ?? "", StringComparer.Ordinal)
                ? candidate.SortColumnDefName
                : candidate.VisibleColumnDefNames[0];
        if (string.Equals(candidate.SortColumnDefName, fallbackSortColumn, StringComparison.Ordinal) == false)
        {
            if (candidate.SortColumnDefName != null
                && (_columnRegistry == null || KnownColumnDefNames.Contains(candidate.SortColumnDefName)))
            {
                diagnostics.Add($"Replaced missing sort column \"{candidate.SortColumnDefName}\" with \"{fallbackSortColumn}\".");
            }

            candidate = candidate.With(
                sortColumnDefName: fallbackSortColumn,
                replaceSortColumn: true);
        }

        return candidate;
    }

    private TableConfiguration NormalizeKnownColumnIdentifiers(
        TableConfiguration candidate,
        List<string> diagnostics)
    {
        HashSet<string> knownNames = new(KnownColumnDefNames, StringComparer.Ordinal);
        HashSet<string> missingNames = new(StringComparer.Ordinal);
        foreach (string duplicateName in candidate.DuplicateVisibleColumnDefNames)
        {
            if (knownNames.Contains(duplicateName))
            {
                diagnostics.Add($"Removed duplicate column identifier \"{duplicateName}\".");
            }
        }
        List<string> visibleNames = [];
        List<float> visibleWidths = [];
        bool hasColumnWidths = candidate.ColumnWidths.Count > 0;
        for (int i = 0; i < candidate.VisibleColumnDefNames.Count; i++)
        {
            string name = candidate.VisibleColumnDefNames[i];
            if (knownNames.Contains(name) == false)
            {
                AddMissingColumnDiagnostic(name, missingNames, diagnostics);
                continue;
            }

            visibleNames.Add(name);
            if (hasColumnWidths)
            {
                visibleWidths.Add(i < candidate.ColumnWidths.Count ? candidate.ColumnWidths[i] : 0f);
            }
        }

        List<string> pinnedNames = [];
        foreach (string name in candidate.PinnedColumnDefNames)
        {
            if (knownNames.Contains(name) && visibleNames.Contains(name, StringComparer.Ordinal))
            {
                pinnedNames.Add(name);
            }
            else if (knownNames.Contains(name) == false)
            {
                AddMissingColumnDiagnostic(name, missingNames, diagnostics);
            }
        }

        List<TableFilterState> filterStates = [];
        foreach (TableFilterState state in candidate.FilterStates)
        {
            bool isTableFilter = state.ColumnDefName.StartsWith("__", StringComparison.Ordinal);
            if (isTableFilter || knownNames.Contains(state.ColumnDefName))
            {
                filterStates.Add(TableConfiguration.CloneFilterState(state));
            }
            else
            {
                AddMissingColumnDiagnostic(state.ColumnDefName, missingNames, diagnostics);
            }
        }

        return new TableConfiguration(
            visibleNames,
            filterStates,
            candidate.ShowVariants,
            candidate.ExpandMultiValueCells,
            candidate.Quality,
            candidate.SortColumnDefName,
            candidate.SortDirection,
            pinnedNames,
            visibleWidths);
    }

    private static void AddMissingColumnDiagnostic(
        string identifier,
        HashSet<string> missingNames,
        List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(identifier) || missingNames.Add(identifier) == false)
        {
            return;
        }

        diagnostics.Add($"Removed missing column identifier \"{identifier}\".");
    }

    private void RemoveConfigurationIntents()
    {
        _pendingIntents.RemoveAll(intent => intent.IsConfigurationIntent);
    }

    private ColumnRegistry<TObject>.Transition? StageColumnTransition(TableConfiguration configuration)
    {
        if (_columnRegistry == null)
        {
            return null;
        }

        return _columnRegistry.Stage(
            configuration.VisibleColumnDefNames,
            configuration.FilterStates.Select(state => state.ColumnDefName));
    }

    private bool IsSameFrame(TableLayoutMetrics metrics)
    {
        if (_frame.Rows.Count != metrics.Rows.Count
            || _frame.RowHeights.Count != metrics.RowHeights.Count
            || _frame.PinnedRowCount != metrics.PinnedRowCount
            || _frame.TopRowsHeight != metrics.TopRowsHeight
            || _frame.BottomRowsHeight != metrics.BottomRowsHeight
            || _frame.ContentWidth != metrics.ContentWidth
            || _frame.ContentHeight != metrics.ContentHeight
            || _frame.LeftColumnsWidth != metrics.LeftColumnsWidth)
        {
            return false;
        }

        for (int i = 0; i < metrics.Rows.Count; i++)
        {
            if (_frame.Rows[i] != metrics.Rows[i] || _frame.RowHeights[i] != metrics.RowHeights[i])
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RollbackStagedConfiguration();
        _pendingIntents.Clear();
        _columnRegistry?.Dispose();
    }
}
