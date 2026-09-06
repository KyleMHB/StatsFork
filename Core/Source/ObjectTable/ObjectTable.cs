using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Stats.ColumnWorkers;
using Stats.Filters;
using Stats.TableWorkers;
using Stats.Utils;
using UnityEngine;
using Verse;

namespace Stats;

internal abstract class ObjectTable
{
    internal abstract void Draw(Rect rect);

    internal abstract void ApplyDefaultPreset();

    internal abstract void NotifyParentWindowClosed();

    internal abstract void Dispose();
}

// Lack of abstraction/leaking abstractions is (almost) intentional here.
// Because abstractions are not free.
internal sealed partial class ObjectTable<TObject> : ObjectTable
{
    private static readonly TipSignal _manual =
        $"- {Localization.Get(Localization.ManualScroll)}\n" +
        $"- {Localization.Get(Localization.ManualPinColumn)}\n" +
        $"- {Localization.Get(Localization.ManualPinRow)}\n" +
        $"  - {Localization.Get(Localization.ManualPinMultipleRows)}\n" +
        $"  - {Localization.Get(Localization.ManualPinnedRowsUnaffected)}\n" +
        $"- {Localization.Get(Localization.ManualResize)}\n" +
        $"- {Localization.Get(Localization.ManualResetResize)}";
    // Filtering
    //public override TableFilterMode FilterMode
    //{
    //    get => field;
    //    set
    //    {
    //        if (value == field) return;

    //        field = value;
    //        MatchRowCells = value switch
    //        {
    //            TableFilterMode.AND => MatchRowCells_AND,
    //            TableFilterMode.OR => MatchRowCells_OR,
    //            _ => throw new NotSupportedException("Unsupported table filtering mode.")
    //        };

    //        OnFilterModeChange?.Invoke(value);
    //        DoFilter = true;
    //    }
    //} = TableFilterMode.AND;
    //public override event Action<TableFilterMode>? OnFilterModeChange;
    //private readonly List<Filter> Filters;
    //private readonly HashSet<Filter> ActiveFilters;
    //private RowCellsMatcher MatchRowCells = MatchRowCells_AND;
    //private static readonly RowCellsMatcher MatchRowCells_AND =
    //(cells, filters) =>
    //{
    //    return filters.All(filter => filter.Widget.Eval(cells[filter.Column]));
    //};
    //private static readonly RowCellsMatcher MatchRowCells_OR =
    //(cells, filters) =>
    //{
    //    return filters.Any(filter => filter.Widget.Eval(cells[filter.Column]));
    //};

    // Sorting
    private Column? _sortColumn;
    private int _sortDirection = SortDirectionAscending;
    private const int SortDirectionAscending = 1;
    private const int SortDirectionDescending = -1;

    // Filters tab

    // Rows
    private readonly List<TObject> _objects;
    private readonly List<int> _rowOrder;
    private readonly List<int> _rows;
    private int _topRowsCount;
    private int BottomRowsCount => _rows.Count - _topRowsCount;

    // Columns
    private readonly List<Column> _columns;
    private readonly Dictionary<ColumnDef, Column> _filterColumns;
    private int _leftColumnsCount;
    private int RightColumnsCount => _columns.Count - _leftColumnsCount;
    private ReadOnlyListSegment<Column> LeftColumns => new(_columns, 0, _leftColumnsCount);
    private ReadOnlyListSegment<Column> RightColumns => new(_columns, _leftColumnsCount, RightColumnsCount);
    private Column? _reorderedColumn;
    private Column? _pressedColumn;

    // Layout
    private float _topRowsHeight;
    private float _bottomRowsHeight;
    private readonly List<float> _rowHeights;
    private float _leftColumnsWidth;
    private Vector2 _contentSize;

    // Drawing
    private Vector2 _scrollPosition;
    private bool _isDrawing;
    // A way to defer any code that would otherwise modify
    // the collection that is currently being iterated over.
    // Primarily GUI event handlers.
    private Action? _beforeDraw;
    private bool _rightPartIsPanned;

    private bool TryDeferWhileDrawing(Action action)
    {
        if (_isDrawing == false)
        {
            return false;
        }

        if (_beforeDraw == null)
        {
            _beforeDraw = action;
        }
        else
        {
            Action previous = _beforeDraw;
            _beforeDraw = () =>
            {
                previous();
                action();
            };
        }

        return true;
    }

    // Toolbar
    private readonly Toolbar _toolbar;

    // Misc
    private readonly TableWorker<TObject> _tableWorker;
    private readonly IVariantTableWorker<TObject>? _variantTableWorker;
    private readonly TableSession<TObject> _tableSession;
    private readonly HashSet<string> _missingColumnWarnings = [];
    private readonly HashSet<string> _layoutFailureWarnings = [];
    private bool _applyingConfiguration;
    private bool _disposed;
    private bool _showVariants;
    private bool _expandMultiValueCells;
    internal bool SupportsVariants => _variantTableWorker?.SupportsVariants == true;
    internal bool ShowVariants => _showVariants;
    internal bool ExpandMultiValueCells => _expandMultiValueCells;
    private QualityCategory _quality = QualityCategory.Normal;
    internal bool SupportsQuality => typeof(TObject) == typeof(DefBasedObject)
        && _tableWorker.CompatibleColumns.Any(column =>
            column is StatColumnDef
            || typeof(IQualityAwareColumnWorker).IsAssignableFrom(column.workerClass));
    internal QualityCategory Quality => _quality;

    private sealed class TableStateSnapshot
    {
        private readonly List<TObject> _objects;
        private readonly List<int> _rowOrder;
        private readonly List<int> _rows;
        private readonly List<float> _rowHeights;
        private readonly List<Column> _columns;
        private readonly Dictionary<ColumnDef, Column> _filterColumns;
        private readonly List<FilterEntry> _filters;
        private readonly Dictionary<string, string> _filterStates;
        private readonly HashSet<string> _missingColumnWarnings;
        private readonly Column? _sortColumn;
        private readonly int _sortDirection;
        private readonly int _topRowsCount;
        private readonly int _leftColumnsCount;
        private readonly float _topRowsHeight;
        private readonly float _bottomRowsHeight;
        private readonly float _leftColumnsWidth;
        private readonly Vector2 _contentSize;
        private readonly Vector2 _scrollPosition;
        private readonly Column? _reorderedColumn;
        private readonly Column? _pressedColumn;
        private readonly bool _rightPartIsPanned;
        private readonly bool _showVariants;
        private readonly bool _expandMultiValueCells;
        private readonly QualityCategory _quality;
        private readonly bool _expandedMultiValueCellsSetting;

        private TableStateSnapshot(ObjectTable<TObject> table)
        {
            _objects = new List<TObject>(table._objects);
            _rowOrder = new List<int>(table._rowOrder);
            _rows = new List<int>(table._rows);
            _rowHeights = new List<float>(table._rowHeights);
            _columns = new List<Column>(table._columns);
            _filterColumns = new Dictionary<ColumnDef, Column>(table._filterColumns);
            _filters = new List<FilterEntry>(table._filters);
            _filterStates = table._filters
                .Where(filter => filter.Widget is IPresettableFilter)
                .ToDictionary(
                    filter => filter.FilterId,
                    filter => ((IPresettableFilter)filter.Widget).SerializeState(),
                    StringComparer.Ordinal);
            _missingColumnWarnings = new HashSet<string>(table._missingColumnWarnings, StringComparer.Ordinal);
            _sortColumn = table._sortColumn;
            _sortDirection = table._sortDirection;
            _topRowsCount = table._topRowsCount;
            _leftColumnsCount = table._leftColumnsCount;
            _topRowsHeight = table._topRowsHeight;
            _bottomRowsHeight = table._bottomRowsHeight;
            _leftColumnsWidth = table._leftColumnsWidth;
            _contentSize = table._contentSize;
            _scrollPosition = table._scrollPosition;
            _reorderedColumn = table._reorderedColumn;
            _pressedColumn = table._pressedColumn;
            _rightPartIsPanned = table._rightPartIsPanned;
            _showVariants = table._showVariants;
            _expandMultiValueCells = table._expandMultiValueCells;
            _quality = table._quality;
            _expandedMultiValueCellsSetting = StatsMod.Instance.Settings.expandedMultiValueCells;
        }

        internal static TableStateSnapshot Capture(ObjectTable<TObject> table) => new(table);

        internal void Restore(ObjectTable<TObject> table)
        {
            bool previousApplyingConfiguration = table._applyingConfiguration;
            table._applyingConfiguration = true;
            try
            {
                foreach (FilterEntry filter in table._filters)
                {
                    if (filter.Column != null)
                    {
                        filter.Widget.OnChange -= table.ApplyFilters;
                    }
                }

                foreach (Column column in table._columns)
                {
                    table.UnregisterColumnFilters(column);
                }

                table._objects.Clear();
                table._objects.AddRange(_objects);
                table._rowOrder.Clear();
                table._rowOrder.AddRange(_rowOrder);
                table._rows.Clear();
                table._rows.AddRange(_rows);
                table._rowHeights.Clear();
                table._rowHeights.AddRange(_rowHeights);
                table._columns.Clear();
                table._columns.AddRange(_columns);
                table._filterColumns.Clear();
                foreach (KeyValuePair<ColumnDef, Column> pair in _filterColumns)
                {
                    table._filterColumns.Add(pair.Key, pair.Value);
                }

                foreach (ColumnWorker<TObject> worker in table._columns
                    .Concat(table._filterColumns.Values)
                    .Select(column => column.Worker)
                    .Distinct())
                {
                    worker.ResetRows();
                    worker.NotifyRowAdded(table._objects);
                }

                table._filters.Clear();
                table._filters.AddRange(_filters);
                foreach (FilterEntry filter in table._filters)
                {
                    if (filter.Column != null)
                    {
                        filter.Widget.OnChange += table.ApplyFilters;
                    }

                    if (filter.Widget is IPresettableFilter presettableFilter)
                    {
                        filter.Widget.Reset();
                        if (_filterStates.TryGetValue(filter.FilterId, out string? state))
                        {
                            presettableFilter.DeserializeState(state);
                        }
                    }
                }

                table._sortColumn = _sortColumn;
                table._sortDirection = _sortDirection;
                table._topRowsCount = _topRowsCount;
                table._leftColumnsCount = _leftColumnsCount;
                table._topRowsHeight = _topRowsHeight;
                table._bottomRowsHeight = _bottomRowsHeight;
                table._leftColumnsWidth = _leftColumnsWidth;
                table._contentSize = _contentSize;
                table._scrollPosition = _scrollPosition;
                table._reorderedColumn = _reorderedColumn;
                table._pressedColumn = _pressedColumn;
                table._rightPartIsPanned = _rightPartIsPanned;
                table._showVariants = _showVariants;
                table._expandMultiValueCells = _expandMultiValueCells;
                table._quality = _quality;
                table._missingColumnWarnings.Clear();
                table._missingColumnWarnings.UnionWith(_missingColumnWarnings);
                StatsMod.Instance.Settings.expandedMultiValueCells = _expandedMultiValueCellsSetting;

            }
            finally
            {
                table._applyingConfiguration = previousApplyingConfiguration;
            }
        }
    }

    public ObjectTable(TableWorker<TObject> tableWorker)
    {
        //tableWorker.OnObjectAdded += AddObject;
        //tableWorker.OnObjectRemoved += RemoveObject;

        // Finalize
        _tableWorker = tableWorker;
        _variantTableWorker = tableWorker as IVariantTableWorker<TObject>;
        _showVariants = _variantTableWorker?.ShowVariantsByDefault == true;
        _expandMultiValueCells = StatsMod.Instance.Settings.expandedMultiValueCells;
        _tableSession = new TableSession<TObject>(new TableConfiguration(
            tableWorker.Def.columns.Select(column => column.defName),
            showVariants: _showVariants,
            expandMultiValueCells: _expandMultiValueCells,
            quality: (int)_quality,
            sortColumnDefName: tableWorker.Def.columns.FirstOrDefault()?.defName,
            sortDirection: SortDirection.Ascending,
            pinnedColumnDefNames: tableWorker.Def.columns.FirstOrDefault() is { } firstColumn
                ? new[] { firstColumn.defName }
                : Array.Empty<string>()),
            tableWorker.CompatibleColumns.Select(column => new TableColumnDefinition<TObject>(
                column.defName,
                () => (ColumnWorker<TObject>)Activator.CreateInstance(column.workerClass, column)!,
                subscribe: (fields, callback) =>
                {
                    foreach (CellField field in fields)
                    {
                        field.FilterWidget.OnChange += callback;
                    }
                },
                unsubscribe: (fields, callback) =>
                {
                    foreach (CellField field in fields)
                    {
                        field.FilterWidget.OnChange -= callback;
                    }
                },
                getCellFields: worker => worker.GetCellFields(tableWorker))),
            ApplyFilters);
        _objects = [];
        _rowOrder = [];
        _rows = [];
        _rowHeights = [];
        _columns = [];
        _filterColumns = [];
        _toolbar = new Toolbar(this);
        RegisterTableFilters();
        RebuildRowsAndColumns(GetCurrentObjects(), tableWorker.Def.columns.Select(column => column.defName).ToList());
    }

    private TableConfigurationTransaction? ApplyTableConfiguration(TableConfiguration configuration)
    {
        TableStateSnapshot snapshot = TableStateSnapshot.Capture(this);
        bool supportsVariants = SupportsVariants;
        bool supportsQuality = SupportsQuality;
        bool requestedQualityIsSupported = Enum.IsDefined(typeof(QualityCategory), configuration.Quality);
        QualityCategory effectiveQuality = supportsQuality && requestedQualityIsSupported
            ? (QualityCategory)configuration.Quality
            : _quality;
        bool effectiveShowVariants = supportsVariants ? configuration.ShowVariants : _showVariants;
        TableConfiguration effectiveConfiguration = configuration.With(
            showVariants: effectiveShowVariants,
            quality: (int)effectiveQuality);
        bool variantsChanged = supportsVariants && _showVariants != effectiveConfiguration.ShowVariants;
        bool qualityChanged = supportsQuality && (int)_quality != effectiveConfiguration.Quality;
        bool expandedChanged = _expandMultiValueCells != effectiveConfiguration.ExpandMultiValueCells;
        List<string> currentColumns = CaptureVisibleColumnDefNames();
        bool columnsChanged = !currentColumns.SequenceEqual(effectiveConfiguration.VisibleColumnDefNames, StringComparer.Ordinal);
        bool pinnedColumnsChanged = !_tableSession.Current.PinnedColumnDefNames.SequenceEqual(
            effectiveConfiguration.PinnedColumnDefNames,
            StringComparer.Ordinal)
            || _leftColumnsCount != effectiveConfiguration.PinnedColumnDefNames.Count;
        bool columnWidthsChanged = false;
        if (effectiveConfiguration.ColumnWidths.Count == _columns.Count)
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                float width = effectiveConfiguration.ColumnWidths[i];
                if (width > 0f
                    ? Math.Abs(_columns[i].Width - width) > 0.01f
                    : _columns[i].IsManuallyResized)
                {
                    columnWidthsChanged = true;
                    break;
                }
            }
        }
        string? previousSortColumn = _sortColumn?.Def.defName;

        try
        {
            _applyingConfiguration = true;
            _showVariants = effectiveConfiguration.ShowVariants;
            if (supportsQuality)
            {
                _quality = effectiveQuality;
            }
            _expandMultiValueCells = effectiveConfiguration.ExpandMultiValueCells;

            if (variantsChanged || qualityChanged)
            {
                ResetRows(GetCurrentObjects());
            }

            if (columnsChanged || pinnedColumnsChanged || variantsChanged || qualityChanged)
            {
                ResetColumns(
                    effectiveConfiguration.VisibleColumnDefNames.ToList(),
                    effectiveConfiguration.PinnedColumnDefNames,
                    effectiveConfiguration.ColumnWidths);
            }
            else if (columnWidthsChanged)
            {
                ApplyColumnWidths(effectiveConfiguration.ColumnWidths);
            }

            RestoreSort(effectiveConfiguration.SortColumnDefName ?? previousSortColumn, (int)effectiveConfiguration.SortDirection);
            SortRows();
            ApplyFilterPresetStates(ConvertFilterStates(effectiveConfiguration.FilterStates));
        }
        catch
        {
            snapshot.Restore(this);
            throw;
        }
        finally
        {
            _applyingConfiguration = false;
        }

        return new TableConfigurationTransaction(
            effectiveConfiguration,
            commit: expandedChanged
                ? () =>
                {
                    StatsMod.Instance.Settings.expandedMultiValueCells = _expandMultiValueCells;
                    StatsMod.Instance.WriteSettings();
                }
                : null,
            rollback: () => snapshot.Restore(this));
    }

    private static List<FilterPresetState> ConvertFilterStates(IReadOnlyList<TableFilterState> states)
    {
        return states.Select(state => new FilterPresetState
        {
            columnDefName = state.ColumnDefName,
            filterId = state.FilterId,
            label = state.Label,
            state = state.State,
        }).ToList();
    }

    private void QueueCurrentConfiguration(TableIntent intent)
    {
        if (_applyingConfiguration)
        {
            return;
        }

        _tableSession.Queue(intent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WarnIncompatibleColumn(string columnName, string tableName)
    {
        Log.Warning($"Column \"${columnName}\" is not compatible with table \"${tableName}\", because it does not implement \"${typeof(ColumnWorker<TObject>).Name}\".");
    }

    internal override void NotifyParentWindowClosed()
    {
        _rightPartIsPanned = false;
        _reorderedColumn = null;
        _pressedColumn = null;
        _filtersWindow?.Close(false);
        for (int i = 0; i < _columns.Count; i++)
        {
            Column column = _columns[i];
            column.NotifyParentWindowClosed();
        }
    }

    internal override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FiltersWindow? filtersWindow = _filtersWindow;
        _filtersWindow = null;
        try
        {
            // FiltersWindow.PostClose releases filter-only columns. It must
            // run while the session and its workers are still alive.
            filtersWindow?.Close(false);
        }
        finally
        {
            _tableSession.Dispose();
        }
    }

    private List<TObject> GetCurrentObjects()
    {
        List<TObject> objects = _variantTableWorker?.GetObjects(_showVariants) ?? _tableWorker.InitialObjects;
        return ApplyQuality(objects);
    }

    private List<TObject> ApplyQuality(List<TObject> objects)
    {
        if (SupportsQuality == false)
        {
            return objects;
        }

        return objects
            .Cast<DefBasedObject>()
            .Select(@object => @object.WithQuality(_quality))
            .Cast<TObject>()
            .ToList();
    }
}
