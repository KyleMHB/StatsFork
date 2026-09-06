using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Stats.ColumnWorkers;
using Stats.TableWorkers;
using Stats.Utils;
using Stats.Utils.Extensions;
using Stats.Utils.GUIScopes;
using Stats.Utils.Widgets;
using UnityEngine;
using Verse;
using Verse.Sound;
using static Stats.GUIStyles.Table;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private void PinColumn(int index)
    {
        if (index < 0 || index >= _columns.Count)
        {
            return;
        }

        QueueColumnPinned(_columns[index].Def.defName, true);
    }

    private void UnpinColumn(int index)
    {
        if (index < 0 || index >= _columns.Count)
        {
            return;
        }

        QueueColumnPinned(_columns[index].Def.defName, false);
    }

    private void QueueColumnPinned(string columnDefName, bool pinned)
    {
        QueueCurrentConfiguration(TableIntent.SetColumnPinned(columnDefName, pinned));
    }

    private void QueueColumnWidth(string columnDefName, float width)
    {
        QueueCurrentConfiguration(TableIntent.ResizeColumn(columnDefName, width));
    }

    private void ResetColumnWidth(string columnDefName)
    {
        QueueCurrentConfiguration(TableIntent.ResetColumnWidth(columnDefName));
    }

    private void AddColumn(ColumnDef columnDef)
    {
        if (TryDeferWhileDrawing(() => AddColumn(columnDef)))
        {
            return;
        }

        if (_columns.Any(column => column.Def == columnDef))
        {
            return;
        }

        List<string> visibleColumnDefNames = CaptureVisibleColumnDefNames();
        visibleColumnDefNames.Add(columnDef.defName);
        QueueCurrentConfiguration(TableIntent.SetVisibleColumns(visibleColumnDefNames));
    }

    private bool TryAddColumn(ColumnDef columnDef, bool notifyToolbar, bool applyFilters)
    {
        if (_columns.Any(column => column.Def == columnDef))
        {
            return false;
        }

        if (_filterColumns.Remove(columnDef, out Column? hiddenColumn))
        {
            _columns.Add(hiddenColumn);
            if (_columns.Count == 1)
            {
                _leftColumnsCount = 1;
                _sortColumn = hiddenColumn;
            }
            if (applyFilters)
            {
                SortRows();
                ApplyFilters();
            }
            return true;
        }

        Type workerClass = columnDef.workerClass;
        if (typeof(ColumnWorker<TObject>).IsAssignableFrom(workerClass) == false)
        {
            WarnIncompatibleColumn(columnDef.defName, _tableWorker.Def.defName);
            return false;
        }

        if (_tableSession.TryGetColumnWorker(columnDef.defName, out ColumnWorker<TObject>? columnWorker) == false
            || columnWorker == null)
        {
            return false;
        }
        if (_tableSession.TryGetColumnFields(columnDef.defName, out ICollection<CellField>? cellFields) == false
            || cellFields == null)
        {
            return false;
        }
        Column column = new(columnWorker, _tableWorker, this, cellFields);
        _columns.Add(column);
        columnWorker.NotifyRowAdded(_objects);
        RegisterColumnFilters(column, cellFields);
        if (_sortColumn == null)
        {
            _sortColumn = column;
            SortRows();
        }
        if (applyFilters)
        {
            ApplyFilters();
        }
        return true;
    }

    private Column? EnsureFilterColumn(ColumnDef columnDef)
    {
        if (_columns.FirstOrDefault(column => column.Def == columnDef) is { } visibleColumn)
        {
            return visibleColumn;
        }

        if (_filterColumns.TryGetValue(columnDef, out Column? hiddenColumn))
        {
            return hiddenColumn;
        }

        Type workerClass = columnDef.workerClass;
        if (typeof(ColumnWorker<TObject>).IsAssignableFrom(workerClass) == false)
        {
            WarnIncompatibleColumn(columnDef.defName, _tableWorker.Def.defName);
            return null;
        }

        if (TryGetOrStageColumnWorker(columnDef) == false
            || _tableSession.TryGetColumnWorker(columnDef.defName, out ColumnWorker<TObject>? columnWorker) == false
            || columnWorker == null)
        {
            return null;
        }
        if (_tableSession.TryGetColumnFields(columnDef.defName, out ICollection<CellField>? cellFields) == false
            || cellFields == null)
        {
            return null;
        }
        Column column = new(columnWorker, _tableWorker, this, cellFields);
        columnWorker.NotifyRowAdded(_objects);
        _filterColumns.Add(columnDef, column);
        RegisterColumnFilters(column, cellFields);
        return column;
    }

    private bool TryGetOrStageColumnWorker(ColumnDef columnDef)
    {
        if (_tableSession.TryGetColumnWorker(columnDef.defName, out _))
        {
            return true;
        }

        List<string> filterColumnNames = _filterColumns.Keys
            .Select(def => def.defName)
            .Append(columnDef.defName)
            .ToList();
        _tableSession.SetColumnWorkerRoles(CaptureVisibleColumnDefNames(), filterColumnNames);
        return _tableSession.TryGetColumnWorker(columnDef.defName, out _);
    }

    private void AddColumnFilter(ColumnDef columnDef)
    {
        if (TryDeferWhileDrawing(() => AddColumnFilter(columnDef)))
        {
            return;
        }

        // Keep the newly created hidden column alive until the user can configure
        // its filter. Applying the current filters is unnecessary here because
        // the new filter is inactive, and a later filter reset may release it
        // before the Filters window has rendered it.
        EnsureFilterColumn(columnDef);
    }

    private void RemoveColumn(int index)
    {
        if (!_applyingConfiguration)
        {
            if (index < 0 || index >= _columns.Count)
            {
                return;
            }

            List<string> visibleColumnDefNames = CaptureVisibleColumnDefNames();
            visibleColumnDefNames.RemoveAt(index);
            QueueCurrentConfiguration(TableIntent.SetVisibleColumns(visibleColumnDefNames));
            return;
        }

        if (index < _leftColumnsCount)
        {
            _leftColumnsCount--;
        }

        Column column = _columns[index];
        _columns.RemoveAt(index);
        if (HasActiveFilter(column))
        {
            _filterColumns[column.Def] = column;
        }
        else
        {
            UnregisterColumnFilters(column);
        }
        if (_pressedColumn == column)
        {
            _pressedColumn = null;
        }
        if (_reorderedColumn == column)
        {
            _reorderedColumn = null;
        }
        if (_sortColumn == column)
        {
            _sortColumn = _columns.Count > 0 ? _columns[0] : null;
        }
        SortRows();
        ApplyFilters();
    }

    private void RemoveColumn(Column column)
    {
        if (TryDeferWhileDrawing(() => RemoveColumn(column)))
        {
            return;
        }

        int index = _columns.IndexOf(column);
        RemoveColumn(index);
    }

    private void RemoveColumn(ColumnDef columnDef)
    {
        if (TryDeferWhileDrawing(() => RemoveColumn(columnDef)))
        {
            return;
        }

        int index = _columns.FindIndex(column => column.Def == columnDef);
        RemoveColumn(index);
    }

    private sealed class Column
    {
        public float Width { get; private set; }
        public ColumnDef Def { get; }
        public bool IsRefreshable => _worker.IsRefreshable;
        public ColumnWorker<TObject> Worker => _worker;
        public bool IsManuallyResized { get; private set; }
        public bool IsResized { get; private set; }
        public Comparison<int>? SortComparison { get; }

        private readonly ColumnWorker<TObject> _worker;
        private readonly Widget _titleWidget;
        private readonly TipSignal _tooltip;
        private readonly ObjectTable<TObject> _parent;
        private readonly FloatMenu _menu;
        private readonly HashSet<string> _drawExceptionKeys = [];
        private float _resizeWidth;

        public Column(ColumnWorker<TObject> worker, TableWorker tableWorker, ObjectTable<TObject> parent, ICollection<CellField> cellFields)
        {
            ColumnDef def = worker.Def;
            Widget titleWidget = def.TitleWidget;

            _worker = worker;
            Def = worker.Def;
            _titleWidget = titleWidget;
            _tooltip = $"<i>{def.LabelCap}</i>\n\n{def.description}";
            _parent = parent;
            SortComparison = cellFields.FirstOrDefault().Compare;
            _menu = new FloatMenu([
                new FloatMenuOption(Localization.Get(Localization.SortAscending), () => {
                    parent.QueueCurrentConfiguration(TableIntent.SetSort(def.defName, SortDirection.Ascending));
                }, TexButton.ReorderUp, Color.white),
                new FloatMenuOption(Localization.Get(Localization.SortDescending), () => {
                    parent.QueueCurrentConfiguration(TableIntent.SetSort(def.defName, SortDirection.Descending));
                }, TexButton.ReorderDown, Color.white),
                new FloatMenuOption(Localization.Get(Localization.ResetWidth), () => parent.ResetColumnWidth(def.defName)),
                new FloatMenuOption(Localization.Get(Localization.Remove), () => parent.RemoveColumn(this), TexButton.Delete, Color.white)
            ]);
        }

        public void Draw(Rect rect, TableFrame<TObject> frame, Span<int> topRows, Span<int> bottomRows, int bottomRowsStart, float bottomRowsY, bool mouseXIsInVisibleArea)
        {
            bool shouldDrawCellsNow = _worker.ShouldDrawCellsNow;
            rect.CutTop(out Rect headerCellRect, HeadersRowHeight)
                .CutTop(out Rect topRowsRect, frame.TopRowsHeight)
                .TakeRest(out Rect bottomRowsRect);

            DrawHeaderCell(headerCellRect, mouseXIsInVisibleArea);

            if (shouldDrawCellsNow)
            {
                if (topRows.Length > 0)
                {
                    using (new GUIClipScope(topRowsRect))
                    {
                        DrawCells(topRowsRect with { x = 0f, y = 0f }, frame, topRows, 0);
                    }
                }

                if (bottomRows.Length > 0)
                {
                    using (new GUIClipScope(bottomRowsRect, new Vector2(0f, bottomRowsY)))
                    {
                        DrawCells(bottomRowsRect with { x = 0f, y = 0f }, frame, bottomRows, bottomRowsStart);
                    }
                }
            }
        }

        private void DrawHeaderCell(Rect rect, bool mouseXIsInVisibleArea)
        {
            Event @event = Event.current;
            ObjectTable<TObject> parent = _parent;
            ColumnType columnType = _worker.Type;
            const float SideControlMargin = 1f;
            rect.CutLeft(out Rect sortControlRect, GUIStyles.TableCell.PadHor - SideControlMargin)
                .CutRight(out Rect resizeControlRect, GUIStyles.TableCell.PadHor - SideControlMargin)
                .TakeRest(out Rect mainControlRect);

            if (@event.type == EventType.Repaint)
            {
                Rect titleClipRect = mainControlRect.ContractedBy(SideControlMargin, GUIStyles.TableCell.PadVer);
                GUI.BeginClip(titleClipRect);

                float titleWidgetWidth = _titleWidget.Size.x;
                Rect titleRect = titleClipRect with { x = 0f, y = 0f };
                if (columnType == ColumnType.Number)
                {
                    titleRect.CutRight(out titleRect, titleWidgetWidth);
                }
                else if (columnType == ColumnType.Boolean)
                {
                    titleRect.CutMidX(out titleRect, titleWidgetWidth);
                }
                else
                {
                    titleRect = titleRect with { width = titleWidgetWidth };
                }
                _titleWidget.Draw(titleRect);

                GUI.EndClip();

                if (parent._reorderedColumn == this)
                {
                    rect.HighlightSelected();
                }
                else if (Mouse.IsOver(rect))
                {
                    rect.HighlightLight();
                }

                rect.DrawBorderRight(ColumnSeparatorLineColor);
            }

            MouseoverSounds.DoRegion(rect);

            DoSortControl(sortControlRect);
            DoMainControl(mainControlRect, rect, mouseXIsInVisibleArea);
            DoResizeControl(resizeControlRect, mouseXIsInVisibleArea);

            mainControlRect.Tip(_tooltip);
        }

        private void DrawCells(Rect rect, TableFrame<TObject> frame, Span<int> rows, int displayedRowStart)
        {
            ColumnWorker<TObject> worker = _worker;
            ref Rect cellRect = ref rect;
            int rowsCount = rows.Length;
            for (int i = 0; i < rowsCount; i++)
            {
                cellRect.height = frame.RowHeights[displayedRowStart + i];
                try
                {
                    worker.DrawCell(cellRect, rows[i]);
                }
                catch (Exception exception)
                {
                    LogDrawException(rows[i], exception);
                    cellRect.Fill(Color.red);
                }
                cellRect.y = cellRect.yMax;
            }
        }

        private void LogDrawException(int row, Exception exception)
        {
            string key = $"{Def.defName}:{row}:{exception.GetType().FullName}:{exception.Message}";
            if (_drawExceptionKeys.Add(key) == false)
            {
                return;
            }

            Log.Error($"Stats failed to draw cell for column \"{Def.defName}\" at row {row}: {exception}");
        }

        private void DoMainControl(Rect rect, Rect cellRect, bool mouseXIsInVisibleArea)
        {
            Event @event = Event.current;
            ObjectTable<TObject> parent = _parent;
            bool mouseIsOverRect = Mouse.IsOver(rect);

            if (@event is { type: EventType.MouseDown, button: 0, modifiers: EventModifiers.None } && mouseIsOverRect)
            {
                parent._pressedColumn = this;
            }
            else if (parent._pressedColumn == this && OriginalEventUtility.EventType == EventType.MouseDrag)
            {
                parent._reorderedColumn = this;
            }

            if (parent._reorderedColumn != null)
            {
                DoReorder(cellRect, parent._reorderedColumn, mouseXIsInVisibleArea);
            }
            else if (@event.type == EventType.MouseUp && mouseIsOverRect)
            {
                if (@event is { button: 0, modifiers: EventModifiers.Control })
                {
                    HandlePin();
                    parent._pressedColumn = null;
                    GUIUtils.ReleaseMouseControl();
                    @event.Use();
                }
                else if (@event is { button: 0, modifiers: EventModifiers.None } && parent._pressedColumn == this)
                {
                    parent.HandleSortRequested(this);
                    parent._pressedColumn = null;
                    GUIUtils.ReleaseMouseControl();
                    @event.Use();
                }
                else if (@event is { button: 1, modifiers: EventModifiers.None })
                {
                    _menu.Open();
                    parent._pressedColumn = null;
                    GUIUtils.ReleaseMouseControl();
                    @event.Use();
                }
            }
            else if (@event.rawType == EventType.MouseUp && parent._pressedColumn == this)
            {
                parent._pressedColumn = null;
            }

            GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private void DoSortControl(Rect rect)
        {
            ObjectTable<TObject> parent = _parent;
            Event @event = Event.current;
            const float IconPadding = 3f;

            if (@event.type == EventType.Repaint)
            {
                if (parent._sortColumn == this)
                {
                    if (parent._sortDirection == SortDirectionAscending)
                    {
                        rect.TopHalf()
                            .ContractedBy(IconPadding)
                            .DrawTextureFitted(TexButton.ReorderUp);
                    }
                    else
                    {
                        rect.BottomHalf()
                            .ContractedBy(IconPadding)
                            .DrawTextureFitted(TexButton.ReorderDown);
                    }
                }

                if (Mouse.IsOver(rect))
                {
                    rect.Highlight();
                }
            }

            bool wasClicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (wasClicked && @event is { button: 0, modifiers: EventModifiers.None })
            {
                parent.HandleSortRequested(this);
                parent._pressedColumn = null;
                GUIUtils.ReleaseMouseControl();
                @event.Use();
            }
        }

        private void DoResizeControl(Rect rect, bool mouseXIsInVisibleArea)
        {
            Event @event = Event.current;
            bool mouseIsOverRect = Mouse.IsOver(rect);

            if (@event is { type: EventType.MouseDown, button: 0, modifiers: EventModifiers.None } && mouseIsOverRect)
            {
                IsResized = true;
                _resizeWidth = Width;
            }
            else if (IsResized)
            {
                DoResize();
                // TODO: This will work for both left and right.
                if (mouseXIsInVisibleArea == false)
                {
                    _parent._scrollPosition.x++;
                    _resizeWidth++;
                }
            }

            if (@event.type == EventType.Repaint && (mouseIsOverRect || IsResized))
            {
                rect.HighlightSelected();
            }

            GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        public void DoResize()
        {
            Event @event = Event.current;

            if (OriginalEventUtility.EventType == EventType.MouseDrag)
            {
                _resizeWidth = Mathf.Clamp(_resizeWidth + @event.delta.x, HeadersRowHeight, float.MaxValue);
                _parent.QueueColumnWidth(Def.defName, _resizeWidth);
                @event.Use();
            }
            else if (@event.rawType == EventType.MouseUp)
            {
                IsResized = false;
                IsManuallyResized = true;
                GUIUtils.ReleaseMouseControl();
                @event.Use();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoReorder(Rect rect, Column reorderedColumn, bool mouseXIsInVisibleArea)
        {
            Event @event = Event.current;
            ObjectTable<TObject> parent = _parent;

            if (OriginalEventUtility.EventType == EventType.MouseDrag
                && parent._reorderedColumn != this
                && mouseXIsInVisibleArea
                && rect.x < @event.mousePosition.x && @event.mousePosition.x < rect.xMax)
            {
                List<string> columnNames = parent._columns.Select(column => column.Def.defName).ToList();
                int reorderedColumnIndex = columnNames.IndexOf(reorderedColumn.Def.defName);
                int thisColumnIndex = columnNames.IndexOf(Def.defName);

                float xMiddle = rect.x + rect.width / 2f;
                float mouseX = @event.mousePosition.x;
                if (rect.x < mouseX && mouseX < xMiddle)// Left
                {
                    if (thisColumnIndex - 1 != reorderedColumnIndex)
                    {
                        MoveAndQueue(parent, columnNames, reorderedColumnIndex, thisColumnIndex);
                    }
                }
                // xMiddle < mouseX && mouseX < rect.xMax check is not necessary here
                // because we already checked if mouseX is between rect.x and rect.xMax.
                // So if mouseX is not on the left, it is guaranteed to be on the right.
                else// Right
                {
                    if (thisColumnIndex + 1 != reorderedColumnIndex)
                    {
                        MoveAndQueue(parent, columnNames, reorderedColumnIndex, thisColumnIndex + 1);
                    }
                }

                @event.Use();
            }
            else if (@event.rawType == EventType.MouseUp)
            {
                parent._reorderedColumn = null;
                parent._pressedColumn = null;
                GUIUtils.ReleaseMouseControl();
                @event.Use();
            }
        }

        private static void MoveAndQueue(
            ObjectTable<TObject> parent,
            List<string> names,
            int fromIndex,
            int insertionIndex)
        {
            string moved = names[fromIndex];
            names.RemoveAt(fromIndex);
            if (insertionIndex > fromIndex)
            {
                insertionIndex--;
            }

            names.Insert(Mathf.Clamp(insertionIndex, 0, names.Count), moved);
            parent.QueueCurrentConfiguration(TableIntent.ReorderColumns(names));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void HandlePin()
        {
            ObjectTable<TObject> parent = _parent;
            bool isPinned = parent._tableSession.Current.PinnedColumnDefNames.Contains(
                Def.defName,
                StringComparer.Ordinal);
            parent.QueueColumnPinned(Def.defName, !isPinned);
        }

        public void RecalcWidth(List<int> rows)
        {
            Width = Mathf.Max(_titleWidget.Size.x, _worker.GetWidth(rows)) + GUIStyles.TableCell.PadHor * 2f;
        }

        public void SetManualWidth(float width)
        {
            if (width > 0f)
            {
                Width = width;
                IsManuallyResized = true;
            }
            else
            {
                IsManuallyResized = false;
            }
        }

        public int CompareRows(int row1, int row2)
        {
            return SortComparison?.Invoke(row1, row2) ?? row1.CompareTo(row2);
        }

        public int GetExpandedLineCount(int row)
        {
            return _worker.GetExpandedLineCount(row);
        }

        public bool RefreshCells()
        {
            return _worker.RefreshCells();
        }

        public void NotifyParentWindowClosed()
        {
            if (IsResized)
            {
                IsResized = false;
            }
        }
    }
}
