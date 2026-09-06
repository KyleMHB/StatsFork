using System.Collections.Generic;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private void SortRows()
    {
        if (_sortColumn?.SortComparison == null || _rowOrder.Count < 2)
        {
            return;
        }

        SortRows(_rowOrder, 0, _topRowsCount);
        SortRows(_rowOrder, _topRowsCount, _rowOrder.Count - _topRowsCount);
    }

    private void SortRows(List<int> rows, int index, int count)
    {
        if (count < 2)
        {
            return;
        }

        rows.Sort(index, count, Comparer<int>.Create(CompareRows));
    }

    private int CompareRows(int row1, int row2)
    {
        Column? sortColumn = _sortColumn;
        if (sortColumn?.SortComparison == null)
        {
            return row1.CompareTo(row2);
        }

        int result = sortColumn.CompareRows(row1, row2) * _sortDirection;
        if (result != 0)
        {
            return result;
        }

        return row1.CompareTo(row2);
    }

    private void HandleSortRequested(Column column)
    {
        string? sortColumnDefName = column.Def.defName;
        int sortDirection;
        if (_sortColumn != column)
        {
            sortDirection = SortDirectionDescending;
        }
        else
        {
            sortDirection = _sortDirection == SortDirectionDescending
                ? SortDirectionAscending
                : SortDirectionDescending;
        }

        QueueCurrentConfiguration(TableIntent.SetSort(
            sortColumnDefName,
            sortDirection == SortDirectionAscending ? SortDirection.Ascending : SortDirection.Descending));
    }
}
