using System.Collections.Generic;
using Stats.ColumnWorkers.Cells;
using System;
using Stats.TableWorkers;
using Stats.Utils.Extensions;
using UnityEngine;
using Verse;

namespace Stats.ColumnWorkers;

public abstract class ColumnWorker<TObject>
{
    public abstract ColumnDef Def { get; }
    public abstract ColumnType Type { get; }
    public abstract bool IsRefreshable { get; }
    public virtual bool ShouldDrawCellsNow => Event.current.type == EventType.Repaint;

    public abstract void DrawCell(Rect rect, int row);

    public abstract float GetWidth(List<int> rows);

    public virtual int GetExpandedLineCount(int row) => 1;

    public abstract void NotifyRowAdded(List<TObject> rows);

    public abstract void NotifyRowAdded(TObject row);

    public abstract void NotifyRowRemoved(int row);

    /// <summary>
    /// Clears the row cache before a table rebuild. Optional for external
    /// workers so existing subclasses remain source-compatible.
    /// </summary>
    public virtual void ResetRows()
    {
    }

    public abstract bool RefreshCells();

    public abstract ICollection<CellField> GetCellFields(TableWorker tableWorker);

    /// <summary>
    /// Releases subscriptions and other resources owned by this worker.
    /// Existing external workers remain source-compatible because the hook is
    /// optional and idempotent by contract.
    /// </summary>
    public virtual void Dispose()
    {
    }
}

public interface IQualityAwareColumnWorker;

public abstract class ColumnWorker<TObject, TCell> : ColumnWorker<TObject> where TCell : struct, ICell
{
    public override bool IsRefreshable => _refreshableCellsCount > 0;

    private readonly List<TCell> _cells = new(250);
    private readonly HashSet<string> _makeCellExceptionKeys = [];
    private int _refreshableCellsCount;

    protected TCell this[int index] => _cells[index];

    protected abstract TCell MakeCell(TObject @object);

    public override void DrawCell(Rect rect, int row)
    {
        _cells[row].Draw(rect);
    }

    public override float GetWidth(List<int> rows)
    {
        float width = 0f;
        int rowsCount = rows.Count;
        for (int i = 0; i < rowsCount; i++)
        {
            int rowIndex = rows[i];
            float cellWidth = _cells[rowIndex].Width;
            if (width < cellWidth)
            {
                width = cellWidth;
            }
        }

        return width;
    }

    public override int GetExpandedLineCount(int row)
    {
        return _cells[row] is IExpandableCell expandableCell
            ? expandableCell.ExpandedLineCount
            : 1;
    }

    public override void NotifyRowAdded(List<TObject> rows)
    {
        int rowsCount = rows.Count;
        for (int i = 0; i < rowsCount; i++)
        {
            NotifyRowAdded(rows[i]);
        }
    }

    public override void ResetRows()
    {
        _cells.Clear();
        _refreshableCellsCount = 0;
    }

    public override void NotifyRowAdded(TObject row)
    {
        TCell cell;
        try
        {
            cell = MakeCell(row);
        }
        catch (Exception exception)
        {
            LogMakeCellException(row, exception);
            cell = default;
        }

        _cells.Add(cell);
        if (cell.IsRefreshable)
        {
            _refreshableCellsCount++;
        }
    }

    public override void NotifyRowRemoved(int row)
    {
        if (_cells[row].IsRefreshable)
        {
            _refreshableCellsCount--;
        }
        _cells.ReplaceWithLast(row);
    }

    protected virtual TCell RefreshCell(TCell cell, out bool wasStale)
    {
        wasStale = false;
        return cell;
    }

    public override bool RefreshCells()
    {
        if (_refreshableCellsCount == 0)
        {
            return false;
        }

        bool anyCellChanged = false;
        List<TCell> cells = _cells;
        int cellsCount = cells.Count;
        for (int i = 0; i < cellsCount; i++)
        {
            TCell originalCell = cells[i];
            if (originalCell.IsRefreshable)
            {
                TCell possiblyNewCell = RefreshCell(originalCell, out bool wasStale);
                if (wasStale)
                {
                    cells[i] = possiblyNewCell;
                    anyCellChanged = true;
                }
            }
        }

        return anyCellChanged;
    }

    private void LogMakeCellException(TObject row, Exception exception)
    {
        string key = $"{GetType().FullName}:{exception.GetType().FullName}:{exception.Message}";
        if (_makeCellExceptionKeys.Add(key) == false)
        {
            return;
        }

        string rowText;
        try
        {
            rowText = row?.ToString() ?? "<null>";
        }
        catch
        {
            rowText = "<unprintable>";
        }

        Log.Error($"Stats failed to create cell for worker {GetType().FullName}, column \"{Def.defName}\", row {rowText}: {exception}");
    }
}
