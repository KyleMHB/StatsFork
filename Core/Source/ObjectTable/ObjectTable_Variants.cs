using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Stats.ColumnWorkers;
using Verse;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private void ToggleVariants()
    {
        if (SupportsVariants == false)
        {
            return;
        }

        SetVariantsMode(_showVariants == false);
    }

    private void SetVariantsMode(bool showVariants)
    {
        if (SupportsVariants == false || _showVariants == showVariants)
        {
            return;
        }

        if (TryDeferWhileDrawing(() => SetVariantsMode(showVariants)))
        {
            return;
        }

        QueueCurrentConfiguration(TableIntent.SetShowVariants(showVariants));
    }

    private void SetQuality(QualityCategory quality)
    {
        if (SupportsQuality == false || _quality == quality)
        {
            return;
        }

        if (TryDeferWhileDrawing(() => SetQuality(quality)))
        {
            return;
        }

        QueueCurrentConfiguration(TableIntent.SetQuality((int)quality));
    }

    private void RebuildRowsAndColumns(List<TObject> objects, List<string> visibleColumnDefNames)
    {
        ResetRows(objects);
        ResetColumns(visibleColumnDefNames);
        SortRows();
        ApplyFilters();
    }

    private void ResetRows(List<TObject> objects)
    {
        _objects.Clear();
        _objects.AddRange(objects);

        _rowOrder.Clear();
        _rows.Clear();
        int objectsCount = _objects.Count;
        for (int i = 0; i < objectsCount; i++)
        {
            _rowOrder.Add(i);
            _rows.Add(i);
        }

        _topRowsCount = 0;
    }

    private void ResetColumns(List<string> visibleColumnDefNames, IReadOnlyList<string>? pinnedColumnDefNames = null, IReadOnlyList<float>? columnWidths = null)
    {
        foreach (Column column in _columns)
        {
            UnregisterColumnFilters(column);
        }

        foreach (Column column in _filterColumns.Values)
        {
            UnregisterColumnFilters(column);
        }
        _filterColumns.Clear();

        _columns.Clear();
        _leftColumnsCount = 0;
        _sortColumn = null;
        _reorderedColumn = null;
        _pressedColumn = null;

        if (_applyingConfiguration == false)
        {
            _tableSession.SetColumnWorkerRoles(visibleColumnDefNames, Array.Empty<string>());
        }

        IEnumerable<ColumnDef> columnDefs = visibleColumnDefNames
            .Select(ResolveVisibleColumnDef)
            .Where(column => column != null)!;

        foreach (ColumnDef columnDef in columnDefs)
        {
            TryAddColumn(columnDef, notifyToolbar: true, applyFilters: false);
        }

        IReadOnlyList<string> pinnedNames = pinnedColumnDefNames ??
            (visibleColumnDefNames.Count > 0 ? new[] { visibleColumnDefNames[0] } : Array.Empty<string>());
        _leftColumnsCount = _columns.Count(column => pinnedNames.Contains(column.Def.defName, StringComparer.Ordinal));
        if (_leftColumnsCount == 0 && _columns.Count > 0 && pinnedColumnDefNames == null)
        {
            _leftColumnsCount = 1;
        }

        ApplyColumnWidths(columnWidths);
        _sortColumn = _columns.Count > 0 ? _columns[0] : null;
    }

    private void ApplyColumnWidths(IReadOnlyList<float>? widths)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            float width = widths != null && i < widths.Count ? widths[i] : 0f;
            _columns[i].SetManualWidth(width);
        }
    }

    private ColumnDef? ResolveVisibleColumnDef(string defName)
    {
        ColumnDef? columnDef = _tableWorker.CompatibleColumns.FirstOrDefault(column => column.defName == defName);
        if (columnDef != null)
        {
            return columnDef;
        }

        string warningKey = $"{_tableWorker.Def.defName}:{defName}";
        if (_missingColumnWarnings.Add(warningKey))
        {
            Log.Warning($"Stats preset/table \"{_tableWorker.Def.defName}\" references missing or incompatible column \"{defName}\".");
        }

        return null;
    }
}
