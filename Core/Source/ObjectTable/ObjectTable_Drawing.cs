using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Stats.Utils;
using Stats.Utils.Extensions;
using Stats.Utils.GUIScopes;
using UnityEngine;
using Verse;
using static Stats.GUIStyles.Table;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    internal override void Draw(Rect rect)
    {
        using MultiValueDisplay.Scope displayScope = MultiValueDisplay.Enter(_expandMultiValueCells);
        if (Event.current.type == EventType.Layout && _beforeDraw != null)
        {
            _beforeDraw.Invoke();
            _beforeDraw = null;
        }

        _isDrawing = true;
        try
        {
            bool layoutPreparationSucceeded = Event.current.type != EventType.Layout;
            if (Event.current.type == EventType.Layout)
            {
                try
                {
                    _tableSession.StagePendingConfiguration(ApplyTableConfiguration);
                    RefreshLiveCells();
                    RecalcLayout();
                    layoutPreparationSucceeded = true;
                }
                catch (Exception exception)
                {
                    _tableSession.RejectStagedConfiguration(exception);
                    LogLayoutPreparationFailure(exception);
                }
            }

            TableFrame<TObject> frame = _tableSession.Frame;
            if (Event.current.type == EventType.Layout && layoutPreparationSucceeded)
            {
                frame = _tableSession.PrepareFrame(
                    new TableLayoutMetrics(
                        _rows,
                        _rowHeights,
                        _topRowsCount,
                        _topRowsHeight,
                        _bottomRowsHeight,
                        _contentSize.x,
                        _contentSize.y,
                        _leftColumnsWidth,
                        _columns.Select(column => column.Def.defName).ToList(),
                        _columns.Select(column => column.Width).ToList(),
                        _leftColumnsCount)).Frame;
            }

            // Layout
            rect
                .CutTop(out Rect toolbarRect, GUIStyles.TableToolbar.Height)
                .TakeRest(out Rect tableRect);

            // Toolbar
            _toolbar.Draw(toolbarRect);

            //if (showSettingsMenu)
            //{
            //    DrawColumnsTab(ref rect);
            //}

            Rect viewportRect = tableRect;
            Vector2 contentSize = new(frame.ContentWidth, frame.ContentHeight);
            Rect contentRect = new(Vector2.zero, contentSize);
            // Will scroll vertically
            if (frame.BottomRowsHeight > 0f)
            {
                viewportRect.width -= GenUI.ScrollBarWidth;
                // Add empty space for more convenient vertical scrolling.
                contentRect.height += viewportRect.height - HeadersRowHeight - frame.TopRowsHeight;
            }
            // Will scroll horizontally
            if (contentRect.width > viewportRect.width)
            {
                viewportRect.height -= GenUI.ScrollBarWidth;
                contentRect.height -= GenUI.ScrollBarWidth;
            }

            using (new GUIScrollScope(tableRect, ref _scrollPosition, contentRect)) { }

            DrawVisibleContent(viewportRect, frame);
        }
        finally
        {
            _isDrawing = false;
        }
    }

    private void LogLayoutPreparationFailure(Exception exception)
    {
        string key = $"{exception.GetType().FullName}:{exception.Message}";
        if (_layoutFailureWarnings.Add(key))
        {
            Log.Warning($"Stats deferred a table configuration after layout preparation failed: {exception.Message}");
        }
    }

    private void RefreshLiveCells()
    {
        bool anyColumnChanged = false;
        if (HasActiveLiveTableFilter() && InventoryStateTracker.RefreshIfNeeded())
        {
            anyColumnChanged = true;
        }

        int columnsCount = _columns.Count;
        for (int i = 0; i < columnsCount; i++)
        {
            Column column = _columns[i];
            if (column.IsRefreshable && column.RefreshCells())
            {
                anyColumnChanged = true;
            }
        }

        foreach (string columnDefName in _tableSession.FilterOnlyColumnDefNames)
        {
            if (_filterColumns.FirstOrDefault(pair =>
                    string.Equals(pair.Key.defName, columnDefName, StringComparison.Ordinal)).Value is not { } column)
            {
                continue;
            }

            if (column.IsRefreshable && column.RefreshCells())
            {
                anyColumnChanged = true;
            }
        }

        if (anyColumnChanged)
        {
            SortRows();
            ApplyFilters();
        }
    }

    private void DrawVisibleContent(Rect rect, TableFrame<TObject> frame)
    {
        Event @event = Event.current;
        Vector2 scrollPosition = _scrollPosition;
        float bottomRowsRectHeight = rect.height - HeadersRowHeight - frame.TopRowsHeight;
        GetVisibleBottomRows(
            frame,
            scrollPosition.y,
            bottomRowsRectHeight,
            out int visibleBottomRowsStart,
            out int visibleBottomRowsCount,
            out float firstVisibleBottomRowY);
        int topRowsCount = frame.PinnedRowCount;

        Span<int> visibleBottomRows = stackalloc int[visibleBottomRowsCount];
        for (int i = 0; i < visibleBottomRowsCount; i++)
        {
            visibleBottomRows[i] = frame.Rows[visibleBottomRowsStart + i];
        }

        Span<int> topRows = stackalloc int[topRowsCount];
        for (int i = 0; i < topRowsCount; i++)
        {
            topRows[i] = frame.Rows[i];
        }

        // Layout
        rect
            .CutLeft(out Rect leftColumnsRect, frame.LeftColumnsWidth)
            .TakeRest(out Rect rightColumnsRect)
            .CutTop(HeadersRowHeight)// Register mouse-drag only below headers to not interfere with them.
            .TakeRest(out Rect mouseDragScrollAreaRect);

        // Rows
        DrawRows(rect, frame, visibleBottomRowsStart, visibleBottomRowsCount, firstVisibleBottomRowY);

        // Pinned columns
        if (frame.PinnedColumnCount > 0)
        {
            DrawColumns(leftColumnsRect, frame, Vector2.zero, 0, frame.PinnedColumnCount, topRows, visibleBottomRows, visibleBottomRowsStart, firstVisibleBottomRowY);
            // Separator line
            if (@event.type == EventType.Repaint)
            {
                leftColumnsRect.DrawBorderRight(FixedPartSeparatorLineColor);
            }
        }

        // Unpinned columns
        if (frame.VisibleColumnDefNames.Count > frame.PinnedColumnCount)
        {
            using (new GUIClipScope(rightColumnsRect, new Vector2(-scrollPosition.x, 0f)))
            {
                DrawColumns(rightColumnsRect with { x = 0f, y = 0f }, frame, scrollPosition, frame.PinnedColumnCount, frame.VisibleColumnDefNames.Count, topRows, visibleBottomRows, visibleBottomRowsStart, firstVisibleBottomRowY);
            }
        }

        DoHorScrollControl(mouseDragScrollAreaRect);
    }

    private void GetVisibleBottomRows(
        TableFrame<TObject> frame,
        float scrollY,
        float viewportHeight,
        out int start,
        out int count,
        out float firstRowY)
    {
        VisibleRowRange range = frame.GetVisibleBottomRows(scrollY, viewportHeight);
        start = range.Start;
        count = range.Count;
        firstRowY = range.FirstRowY;
    }

    private void DrawColumns(Rect rect, TableFrame<TObject> frame, Vector2 scrollPosition, int columnStart, int columnEnd, Span<int> topRows, Span<int> bottomRows, int bottomRowsStart, float bottomRowsY)
    {
        Event @event = Event.current;
        float scrollX = scrollPosition.x;
        float xMin = rect.xMin + scrollX;
        float xMax = rect.xMax + scrollX;
        float mouseX = @event.mousePosition.x;
        bool mouseXIsInVisibleArea = xMin < mouseX && mouseX < xMax;
        ref Rect columnRect = ref rect;
        for (int frameColumnIndex = columnStart; frameColumnIndex < columnEnd; frameColumnIndex++)
        {
            string columnDefName = frame.VisibleColumnDefNames[frameColumnIndex];
            Column? column = _columns.FirstOrDefault(candidate =>
                string.Equals(candidate.Def.defName, columnDefName, StringComparison.Ordinal));
            if (column == null)
            {
                continue;
            }

            columnRect.width = frame.ColumnWidths[frameColumnIndex];
            float columnRectXmax = columnRect.xMax;

            if (xMin < columnRectXmax && columnRect.xMin < xMax)
            {
                column.Draw(columnRect, frame, topRows, bottomRows, bottomRowsStart, bottomRowsY, mouseXIsInVisibleArea);
            }
            else if (column.IsResized)
            {
                column.DoResize();
            }

            columnRect.x = columnRectXmax;
        }
    }

    private void DrawRows(Rect rect, TableFrame<TObject> frame, int bottomRowsStart, int bottomRowsCount, float bottomRowsY)
    {
        bool isRepaint = Event.current.type == EventType.Repaint;

        // Layout
        rect
            .CutTop(out Rect headersRowRect, HeadersRowHeight)
            .CutTop(out Rect topRowsRect, frame.TopRowsHeight)
            .TakeRest(out Rect bottomRowsRect);

        // Headers row
        if (isRepaint)
        {
            headersRowRect
                .Fill(HeadersRowBGColor)
                .DrawBorderBottom(ColumnSeparatorLineColor);
        }

        // Pinned rows
        int topRowsCount = frame.PinnedRowCount;
        if (topRowsCount > 0)
        {
            if (isRepaint)
            {
                topRowsRect.DrawBorderBottom(FixedPartSeparatorLineColor);
            }

            // Rows
            Rect rowRect = topRowsRect;
            for (int i = 0; i < topRowsCount; i++)
            {
                rowRect.height = frame.RowHeights[i];
                DrawRow(rowRect, i);
                rowRect.y = rowRect.yMax;
            }
        }

        // Unpinned rows
        // 
        // This part uses manual clipping.
        // - No need to use GUI.BeginClip/EndClip.
        // - Top row's "click" event will never collide with bottom row.
        if (bottomRowsCount > 0)
        {
            float rectYmax = rect.yMax;
            float firstRowHeight = frame.RowHeights[bottomRowsStart] + bottomRowsY;
            int bottomRowsEnd = bottomRowsCount + bottomRowsStart;// Exclusive
            Rect rowRect = bottomRowsRect with { height = firstRowHeight };
            for (int i = bottomRowsStart; i < bottomRowsEnd; i++)
            {
                DrawRow(rowRect, i);

                rowRect.y = rowRect.yMax;
                if (i + 1 < bottomRowsEnd)
                {
                    rowRect.height = frame.RowHeights[i + 1];
                }

                if (rowRect.yMax > rectYmax)
                {
                    rowRect.yMax = rectYmax;
                }
            }
        }
    }

    private void DrawRow(Rect rect, int index)
    {
        bool mouseIsOverRect = Mouse.IsOver(rect);
        Event @event = Event.current;

        if (@event.type == EventType.Repaint)
        {
            if (mouseIsOverRect)
            {
                rect.Highlight();
            }
            else if (index % 2 == 0)
            {
                rect.HighlightLight();
            }
        }

        if (mouseIsOverRect && @event is { type: EventType.MouseUp, button: 0, modifiers: EventModifiers.Control })
        {
            HandleRowPin(index);
            GUIUtils.ReleaseMouseControl();
            @event.Use();
        }
    }

    //private void DrawColumnsTab(ref Rect rect)
    //{
    //    var columnsTabWidgetSize = ColumnsTabWidget.GetSize(rect.size);
    //    var columnsTabRect = rect.CutByX(columnsTabWidgetSize.x + GenUI.ScrollBarWidth);
    //    var columnsTabRectMax = new Rect(Vector2.zero, columnsTabWidgetSize);
    //    // Adds empty space for more convenient vertical scrolling.
    //    columnsTabRectMax.height += columnsTabRect.height;

    //    Verse.Widgets.BeginScrollView(columnsTabRect, ref ColumnsTabScrollPosition, columnsTabRectMax, true);
    //    ColumnsTabWidget.DrawIn(columnsTabRectMax);
    //    Verse.Widgets.EndScrollView();
    //    Widgets.Draw.VerticalLine(
    //        columnsTabRect.xMax,
    //        rect.y,
    //        rect.height,
    //        MainTabWindowWidget.BorderLineColor
    //    );
    //    rect.xMin += 1f;
    //}

    private void DoHorScrollControl(Rect rect)
    {
        Event @event = Event.current;

        if (@event is { type: EventType.MouseDown, button: 0, modifiers: EventModifiers.None } && Mouse.IsOver(rect))
        {
            _rightPartIsPanned = true;
        }
        else if (_rightPartIsPanned)
        {
            if (OriginalEventUtility.EventType == EventType.MouseDrag)
            {
                _scrollPosition.x = Mathf.Max(_scrollPosition.x - @event.delta.x, 0f);
                @event.Use();
            }
            else if (@event.rawType == EventType.MouseUp)
            {
                _rightPartIsPanned = false;
                GUIUtils.ReleaseMouseControl();
                @event.Use();
            }
        }

        // This button is here to capture control from whatever
        // eats the events above horizontal scroll code.
        GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }
}
