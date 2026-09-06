using System.Collections.Generic;
using UnityEngine;
using static Stats.GUIStyles.Table;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private void RecalcLayout()
    {
        List<Column> columns = _columns;
        int columnsCount = _columns.Count;
        int leftColumnsCount = _leftColumnsCount;
        float leftColumnsWidth = 0f;
        float rightColumnsWidth = 0f;
        for (int i = 0; i < columnsCount; i++)
        {
            Column column = columns[i];
            if (column.IsManuallyResized == false)
            {
                column.RecalcWidth(_rows);
            }

            if (i < leftColumnsCount)
            {
                leftColumnsWidth += column.Width;
            }
            else
            {
                rightColumnsWidth += column.Width;
            }
        }

        float contentWidth = leftColumnsWidth + rightColumnsWidth;
        RecalcRowHeights();
        float topRowsHeight = 0f;
        for (int i = 0; i < _topRowsCount; i++)
        {
            topRowsHeight += _rowHeights[i];
        }
        float bottomRowsHeight = 0f;
        for (int i = _topRowsCount; i < _rowHeights.Count; i++)
        {
            bottomRowsHeight += _rowHeights[i];
        }
        float contentHeight = HeadersRowHeight + topRowsHeight + bottomRowsHeight;

        _topRowsHeight = topRowsHeight;
        _bottomRowsHeight = bottomRowsHeight;
        _leftColumnsWidth = leftColumnsWidth;
        _contentSize = new Vector2(contentWidth, contentHeight);
    }

    private void RecalcRowHeights()
    {
        _rowHeights.Clear();
        int rowsCount = _rows.Count;
        for (int rowPosition = 0; rowPosition < rowsCount; rowPosition++)
        {
            int lineCount = 1;
            if (_expandMultiValueCells)
            {
                int row = _rows[rowPosition];
                for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                {
                    lineCount = Mathf.Max(lineCount, _columns[columnIndex].GetExpandedLineCount(row));
                }
                lineCount = Mathf.Min(lineCount, MultiValueDisplay.MaxLines);
            }

            _rowHeights.Add(GUIStyles.Text.LineHeight * lineCount + GUIStyles.TableCell.PadVer * 2f);
        }
    }

}
