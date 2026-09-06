using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Stats.ColumnWorkers;
using Stats.Filters;
using Stats.Utils;
using Stats.Utils.Extensions;
using Stats.Utils.GUIScopes;
using Stats.Utils.Widgets;
using UnityEngine;
using Verse;
using FilterLabelWidget = Stats.Utils.Widgets.Widget;
using LegacyThingDefIcon = Stats.Widgets_Legacy.ThingDefIcon;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private const string AvailableFilterKey = "__table_filter_available";
    private const string HasRecipeFilterKey = "__table_filter_has_recipe";
    private const string MaterialFilterKey = "__table_filter_material";
    private const string RecipeIngredientsFilterKey = "__table_filter_recipe_ingredients";
    private const string RecipeBenchFilterKey = "__table_filter_recipe_bench";
    private readonly List<FilterEntry> _filters = [];
    private FiltersWindow? _filtersWindow;
    private Filter? _availableFilter;
    private static readonly HashSet<ThingDef?> _emptyThingDefSet = [];

    private void ToggleFiltersTab()
    {
        if (_filtersWindow != null)
        {
            _filtersWindow.Close();
            return;
        }

        _filtersWindow = new FiltersWindow(this);
        _filtersWindow.Open();
    }

    private void RegisterColumnFilters(Column column, ICollection<CellField> fields)
    {
        int fieldIndex = 0;
        foreach (CellField field in fields)
        {
            string labelText = field.Label is Label label ? label.Text : field.Label.GetType().Name;
            _filters.Add(new FilterEntry(column.Def.defName, column, field.Label, labelText, field.FilterWidget, $"{column.Def.defName}:{fieldIndex}"));
            fieldIndex++;
        }
    }

    private void RegisterTableFilters()
    {
        if (typeof(TObject) != typeof(DefBasedObject))
        {
            return;
        }

        BooleanFilter availableFilter = new(IsAvailable);
        availableFilter.OnChange += ApplyFilters;
        _availableFilter = availableFilter;
        string availableLabel = Localization.Get(Localization.Available);
        _filters.Add(new FilterEntry(AvailableFilterKey, null, new Label(availableLabel), availableLabel, availableFilter, AvailableFilterKey));

        if (SupportsEquipmentRecipeFilters())
        {
            RegisterEquipmentRecipeFilters();
        }
    }

    private void UnregisterColumnFilters(Column column)
    {
        for (int i = _filters.Count - 1; i >= 0; i--)
        {
            FilterEntry filter = _filters[i];
            if (filter.Column != column)
            {
                continue;
            }

            filter.Widget.OnChange -= ApplyFilters;
            _filters.RemoveAt(i);
        }
    }

    private bool HasActiveFilter(Column column)
    {
        return _filters.Any(filter => filter.Column == column && filter.Widget.IsActive);
    }

    private bool HasRegisteredColumnFilter(string columnDefName)
    {
        return _filters.Any(filter => filter.Key == columnDefName);
    }

    private void ReleaseUnusedFilterColumns()
    {
        if (_applyingConfiguration == false && TryDeferWhileDrawing(ReleaseUnusedFilterColumns))
        {
            return;
        }

        bool removedColumn = false;
        foreach (Column column in _filterColumns.Values.ToList())
        {
            if (HasActiveFilter(column))
            {
                continue;
            }

            _filterColumns.Remove(column.Def);
            UnregisterColumnFilters(column);
            removedColumn = true;
        }

        if (removedColumn && _applyingConfiguration == false)
        {
            _tableSession.SetFilterOnlyColumnWorkers(
                _filterColumns.Keys.Select(column => column.defName));
        }
    }

    private void ApplyFilters()
    {
        if (_applyingConfiguration)
        {
            ApplyFiltersNow();
            return;
        }

        if (TryDeferWhileDrawing(ApplyFilters))
        {
            return;
        }

        QueueCurrentConfiguration(TableIntent.SetFilters(CaptureTableFilterStates()));
    }

    private void ApplyFiltersNow()
    {

        List<Filter> activeFilters = _filters
            .Select(filter => filter.Widget)
            .Where(filter => filter.IsActive)
            .ToList();

        _rows.Clear();
        for (int i = 0; i < _topRowsCount; i++)
        {
            _rows.Add(_rowOrder[i]);
        }

        if (activeFilters.Count == 0)
        {
            for (int i = _topRowsCount; i < _rowOrder.Count; i++)
            {
                _rows.Add(_rowOrder[i]);
            }
        }
        else
        {
            for (int i = _topRowsCount; i < _rowOrder.Count; i++)
            {
                int row = _rowOrder[i];
                bool matches = true;
                for (int j = 0; j < activeFilters.Count; j++)
                {
                    try
                    {
                        if (activeFilters[j].Eval(row) == false)
                        {
                            matches = false;
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Failed to evaluate filter on row {row}: {exception}");
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    _rows.Add(row);
                }
            }
        }

        _scrollPosition.y = 0f;
    }

    private void ResetFilters()
    {
        if (_applyingConfiguration)
        {
            ResetFiltersNow();
            return;
        }

        QueueCurrentConfiguration(TableIntent.SetFilters(Array.Empty<TableFilterState>()));
    }

    private void ResetFiltersNow()
    {
        foreach (FilterEntry filter in _filters)
        {
            if (filter.Widget.IsActive)
            {
                filter.Widget.Reset();
            }
        }
        ReleaseUnusedFilterColumns();
    }

    private List<TableFilterState> CaptureTableFilterStates()
    {
        return CaptureFilterPresetStates()
            .Select(state => new TableFilterState(state.columnDefName, state.filterId, state.label, state.state))
            .ToList();
    }

    private List<FilterPresetState> CaptureFilterPresetStates()
    {
        List<FilterPresetState> states = [];
        foreach (FilterEntry filter in _filters)
        {
            if (filter.Widget is IPresettableFilter presettableFilter == false || filter.Widget.IsActive == false)
            {
                continue;
            }

            states.Add(new FilterPresetState
            {
                columnDefName = filter.Column?.Def.defName ?? filter.Key,
                filterId = filter.FilterId,
                label = filter.LabelText,
                state = presettableFilter.SerializeState(),
            });
        }

        return states;
    }

    private void ApplyFilterPresetStates(List<FilterPresetState> states)
    {
        ResetFiltersNow();

        foreach (FilterPresetState state in states)
        {
            if (state.columnDefName.StartsWith("__", StringComparison.Ordinal) == false
                && HasRegisteredColumnFilter(state.columnDefName) == false)
            {
                ColumnDef? columnDef = _tableWorker.CompatibleColumns.FirstOrDefault(column => column.defName == state.columnDefName);
                if (columnDef != null)
                {
                    EnsureFilterColumn(columnDef);
                }
            }

            FilterEntry? matchingFilter = state.filterId.Length > 0
                ? _filters.FirstOrDefault(filter => filter.FilterId == state.filterId)
                : _filters.FirstOrDefault(filter => filter.Key == state.columnDefName && filter.LabelText == state.label);

            if (matchingFilter is { } filterEntry && filterEntry.Widget is IPresettableFilter presettableFilter)
            {
                try
                {
                    presettableFilter.DeserializeState(state.state);
                }
                catch (Exception exception)
                {
                    Log.Error($"Failed to restore filter preset for column \"{state.columnDefName}\" and label \"{state.label}\": {exception}");
                }
            }
        }

        ApplyFiltersNow();
    }

    private bool HasActiveLiveTableFilter()
    {
        return _availableFilter?.IsActive == true;
    }

    private bool IsAvailable(int row)
    {
        if (_objects[row] is not DefBasedObject { Def: ThingDef thingDef })
        {
            return false;
        }

        return InventoryStateTracker.IsOwnedByPlayer(thingDef);
    }

    private bool SupportsEquipmentRecipeFilters()
    {
        List<string> columnTags = _tableWorker.Def.columnTags;
        return columnTags.Contains("Thing_IsApparel")
            || columnTags.Contains("Thing_IsRangedWeapon")
            || columnTags.Contains("Thing_IsMeleeWeapon");
    }

    private void RegisterEquipmentRecipeFilters()
    {
        BooleanFilter hasRecipeFilter = new(HasRecipe);
        hasRecipeFilter.OnChange += ApplyFilters;
        string hasRecipeLabel = Localization.Get(Localization.HasRecipe);
        _filters.Add(new FilterEntry(HasRecipeFilterKey, null, new Label(hasRecipeLabel), hasRecipeLabel, hasRecipeFilter, HasRecipeFilterKey));

        Filter materialFilter = new MTMFilter<ThingDef?>(
            GetMaterialFilterValue,
            MakeThingDefFilterOptions(GetMaterialFilterOptions()));
        materialFilter.OnChange += ApplyFilters;
        string materialLabel = Localization.Get(Localization.Material);
        _filters.Add(new FilterEntry(MaterialFilterKey, null, new Label(materialLabel), materialLabel, materialFilter, MaterialFilterKey));

        Filter recipeIngredientsFilter = new MTMFilter<ThingDef?>(
            GetRecipeIngredientsFilterValue,
            MakeThingDefFilterOptions(GetRecipeIngredientFilterOptions()));
        recipeIngredientsFilter.OnChange += ApplyFilters;
        string recipeIngredientsLabel = Localization.Get(Localization.RecipeIngredients);
        _filters.Add(new FilterEntry(RecipeIngredientsFilterKey, null, new Label(recipeIngredientsLabel), recipeIngredientsLabel, recipeIngredientsFilter, RecipeIngredientsFilterKey));

        Filter recipeBenchFilter = new MTMFilter<ThingDef?>(
            GetRecipeBenchFilterValue,
            MakeThingDefFilterOptions(GetRecipeBenchFilterOptions()));
        recipeBenchFilter.OnChange += ApplyFilters;
        string recipeBenchLabel = Localization.Get(Localization.RecipeBench);
        _filters.Add(new FilterEntry(RecipeBenchFilterKey, null, new Label(recipeBenchLabel), recipeBenchLabel, recipeBenchFilter, RecipeBenchFilterKey));
    }

    private bool HasRecipe(int row)
    {
        return _objects[row] is DefBasedObject { Def: ThingDef thingDef }
            && thingDef.GetRecipeDefs()?.Count > 0;
    }

    private IEnumerable<ThingDef?> GetMaterialFilterValue(int row)
    {
        if (_objects[row] is not DefBasedObject { Def: ThingDef thingDef } @object)
        {
            return _emptyThingDefSet;
        }

        if (@object.StuffDef != null)
        {
            return [@object.StuffDef];
        }

        return thingDef.GetAllowedStuffs()?.Cast<ThingDef?>() ?? _emptyThingDefSet;
    }

    private IEnumerable<ThingDef?> GetRecipeIngredientsFilterValue(int row)
    {
        if (_objects[row] is not DefBasedObject { Def: ThingDef thingDef } @object)
        {
            return _emptyThingDefSet;
        }

        return GetRecipeIngredients(thingDef, @object.StuffDef);
    }

    private IEnumerable<ThingDef?> GetRecipeBenchFilterValue(int row)
    {
        if (_objects[row] is not DefBasedObject { Def: ThingDef thingDef })
        {
            return _emptyThingDefSet;
        }

        HashSet<RecipeDef>? recipes = thingDef.GetRecipeDefs();
        if (recipes == null || recipes.Count == 0)
        {
            return _emptyThingDefSet;
        }

        HashSet<ThingDef?> benches = [];
        foreach (RecipeDef recipe in recipes)
        {
            HashSet<ThingDef>? recipeUsers = recipe.GetAllRecipeUsers();
            if (recipeUsers != null)
            {
                foreach (ThingDef recipeUser in recipeUsers)
                {
                    benches.Add(recipeUser);
                }
            }
        }

        return benches;
    }

    private IEnumerable<ThingDef?> GetMaterialFilterOptions()
    {
        return GetEquipmentThingDefs()
            .SelectMany(thingDef => thingDef.GetAllowedStuffs() ?? [])
            .Distinct();
    }

    private IEnumerable<ThingDef?> GetRecipeIngredientFilterOptions()
    {
        return GetEquipmentThingDefs()
            .SelectMany(thingDef => GetRecipeIngredients(thingDef, null))
            .Distinct();
    }

    private IEnumerable<ThingDef?> GetRecipeBenchFilterOptions()
    {
        return GetEquipmentThingDefs()
            .SelectMany(thingDef => thingDef.GetRecipeDefs() ?? [])
            .SelectMany(recipe => recipe.GetAllRecipeUsers() ?? [])
            .Distinct();
    }

    private IEnumerable<ThingDef> GetEquipmentThingDefs()
    {
        return _objects
            .OfType<DefBasedObject>()
            .Select(@object => @object.Def)
            .OfType<ThingDef>()
            .Distinct();
    }

    private static IEnumerable<ThingDef?> GetRecipeIngredients(ThingDef thingDef, ThingDef? stuffDef)
    {
        HashSet<RecipeDef>? recipes = thingDef.GetRecipeDefs();
        if (recipes == null || recipes.Count == 0)
        {
            return _emptyThingDefSet;
        }

        HashSet<ThingDef?> ingredients = [];
        foreach (RecipeDef recipe in recipes)
        {
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                if (stuffDef != null && ingredient.filter.Allows(stuffDef))
                {
                    ingredients.Add(stuffDef);
                    continue;
                }

                foreach (ThingDef ingredientThingDef in ingredient.filter.AllowedThingDefs)
                {
                    ingredients.Add(ingredientThingDef);
                }
            }
        }

        return ingredients;
    }

    private static IEnumerable<NTMFilterOption<ThingDef?>> MakeThingDefFilterOptions(IEnumerable<ThingDef?> thingDefs)
    {
        return thingDefs
            .OrderBy(thingDef => thingDef?.LabelCap.RawText)
            .Select<ThingDef?, NTMFilterOption<ThingDef?>>(
                thingDef => thingDef == null
                    ? new NTMFilterOption<ThingDef?>()
                    : new NTMFilterOption<ThingDef?>(thingDef, thingDef.LabelCap, new LegacyThingDefIcon(thingDef)));
    }

    private readonly record struct FilterEntry(string Key, Column? Column, FilterLabelWidget Label, string LabelText, Filter Widget, string FilterId);

    private enum FilterPickerGroup
    {
        Table,
        Visible,
        Hidden,
    }

    private readonly record struct FilterPickerOption(
        string Label,
        FilterPickerGroup Group,
        FilterEntry? TableFilter,
        ColumnDef? ColumnDef);

    private sealed class FiltersWindow : Window
    {
        protected override float Margin => GUIStyles.Global.Pad;

        private const string SearchControlName = "StatsForked_FilterSearch";
        private const float ControlHeight = 30f;
        private const float PickerRowHeight = 30f;
        private const float RemoveButtonSize = 28f;

        private readonly ObjectTable<TObject> _parent;
        private readonly HashSet<string> _draftFilterIds = [];
        private Vector2 _scrollPosition;
        private Vector2 _pickerScrollPosition;
        private bool _showPicker;
        private bool _focusSearchField;
        private string _filterSearch = "";

        public FiltersWindow(ObjectTable<TObject> parent)
        {
            _parent = parent;
            closeOnClickedOutside = true;
            doCloseX = true;
            draggable = true;
            optionalTitle = Localization.Get(Localization.Filters);
        }

        public override void DoWindowContents(Rect rect)
        {
            if (_showPicker && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                ClosePicker();
                Event.current.Use();
            }

            rect
                .CutTop(out Rect controlsRect, ControlHeight)
                .TakeRest(out Rect filtersRect);

            DrawControls(controlsRect);
            filtersRect.yMin += GUIStyles.Global.Pad;

            if (_showPicker)
            {
                DrawPicker(filtersRect);
                return;
            }

            List<FilterEntry> displayedFilters = GetDisplayedFilters();
            if (displayedFilters.Count == 0)
            {
                Widgets.Label(filtersRect, Localization.Get(Localization.NoFiltersApplied));
                return;
            }

            float labelWidth = Mathf.Min(displayedFilters
                .Select(filter => GetDisplayLabel(filter).Size.x)
                .Max(), 240f);
            float rowGap = GUIStyles.Global.PadSm;
            float contentHeight = 0f;
            float filterWidth = Mathf.Max(
                filtersRect.width - labelWidth - GUIStyles.Global.Pad - RemoveButtonSize - GUIStyles.Global.PadSm - GenUI.ScrollBarWidth,
                160f);
            Vector2 filterContainerSize = new(filterWidth, filtersRect.height);

            foreach (FilterEntry filter in displayedFilters)
            {
                contentHeight += Mathf.Max(filter.Label.Size.y, filter.Widget.GetSize(filterContainerSize).y) + rowGap;
            }

            Rect viewRect = new(0f, 0f, Mathf.Max(filtersRect.width - GenUI.ScrollBarWidth, 1f), Mathf.Max(filtersRect.height, contentHeight));
            using (new GUIScrollScope(filtersRect, ref _scrollPosition, viewRect))
            {
                Rect rowRect = new(0f, 0f, viewRect.width, 0f);
                foreach (FilterEntry filter in displayedFilters)
                {
                    rowRect.height = Mathf.Max(filter.Label.Size.y, filter.Widget.GetSize(filterContainerSize).y);
                    if (DrawFilterRow(rowRect, filter, labelWidth))
                    {
                        break;
                    }
                    rowRect.y = rowRect.yMax + rowGap;
                }
            }
        }

        public override void PostClose()
        {
            _parent.ReleaseUnusedFilterColumns();
            _parent._filtersWindow = null;
            base.PostClose();
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = new(Mathf.Min(UI.screenWidth * 0.55f, 640f), Mathf.Min(UI.screenHeight * 0.65f, 500f));
            Vector2 position = UI.MousePositionOnUIInverted;

            if (position.x + size.x > UI.screenWidth)
            {
                position.x = UI.screenWidth - size.x - GUIStyles.Global.Pad;
            }

            if (position.y + size.y > UI.screenHeight)
            {
                position.y = UI.screenHeight - size.y - GUIStyles.Global.Pad;
            }

            position.x = Mathf.Max(position.x, GUIStyles.Global.Pad);
            position.y = Mathf.Max(position.y, GUIStyles.Global.Pad);
            windowRect = new Rect(position, size);
        }

        private void DrawControls(Rect rect)
        {
            rect.CutLeft(out Rect addFilterButtonRect, 130f);
            if (Widgets.ButtonText(addFilterButtonRect, Localization.Get(Localization.AddFilter)))
            {
                if (_showPicker)
                {
                    ClosePicker();
                }
                else
                {
                    _showPicker = true;
                    _filterSearch = "";
                    _pickerScrollPosition = Vector2.zero;
                    _focusSearchField = true;
                }
            }

            rect.CutRight(out Rect resetButtonRect, 100f);
            bool hasActiveFilters = _parent._filters.Any(filter => filter.Widget.IsActive);
            bool guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && hasActiveFilters;
            if (Widgets.ButtonText(resetButtonRect, Localization.Get(Localization.ResetAll)))
            {
                _parent.ResetFilters();
                _draftFilterIds.Clear();
            }
            GUI.enabled = guiEnabled;
        }

        private Widget GetDisplayLabel(FilterEntry filter)
        {
            if (filter.Column != null)
            {
                string columnLabel = filter.Column.Def.LabelCap;
                string filterLabel = filter.LabelText;
                string displayLabel = columnLabel.Length == 0
                    || filter.Label is not Label
                    || string.Equals(columnLabel, filterLabel, StringComparison.OrdinalIgnoreCase)
                    ? columnLabel.Length > 0 ? columnLabel : filterLabel
                    : $"{columnLabel}: {filterLabel}";

                if (_parent._columns.Contains(filter.Column) == false)
                {
                    displayLabel = $"{displayLabel} [{Localization.Get(Localization.HiddenColumn)}]";
                }

                return new Label(displayLabel);
            }

            return filter.Label;
        }

        private bool DrawFilterRow(Rect rect, FilterEntry filter, float labelWidth)
        {
            rect
                .CutLeft(out Rect labelRect, labelWidth)
                .CutLeft(GUIStyles.Global.Pad)
                .CutRight(out Rect removeButtonRect, RemoveButtonSize)
                .CutRight(GUIStyles.Global.PadSm)
                .TakeRest(out Rect filterRect);

            Widget label = GetDisplayLabel(filter);
            labelRect.y += (labelRect.height - label.Size.y) / 2f;
            labelRect.height = label.Size.y;
            label.Draw(labelRect);
            if (label is Label textLabel)
            {
                labelRect.Tip(textLabel.Text);
            }

            Vector2 filterSize = filter.Widget.GetSize(filterRect.size);
            filterRect.y += (filterRect.height - filterSize.y) / 2f;
            filterRect.height = filterSize.y;
            filter.Widget.Draw(filterRect, filterRect.size);

            removeButtonRect.y += (removeButtonRect.height - RemoveButtonSize) / 2f;
            removeButtonRect.height = RemoveButtonSize;
            removeButtonRect.Tip(Localization.Get(Localization.RemoveFilter));
            if (removeButtonRect.ButtonImageSubtle(TexButton.Delete))
            {
                RemoveFilter(filter);
                return true;
            }

            return false;
        }

        private List<FilterEntry> GetDisplayedFilters()
        {
            HashSet<string> activeColumnKeys = _parent._filters
                .Where(filter => filter.Column != null && filter.Widget.IsActive)
                .Select(filter => filter.Key)
                .ToHashSet();

            return _parent._filters
                .Where(filter => filter.Widget.IsActive
                    || _draftFilterIds.Contains(filter.FilterId)
                    || filter.Column != null && activeColumnKeys.Contains(filter.Key))
                .ToList();
        }

        private void RemoveFilter(FilterEntry filter)
        {
            if (filter.Column == null)
            {
                if (filter.Widget.IsActive)
                {
                    filter.Widget.Reset();
                }
                _draftFilterIds.Remove(filter.FilterId);
                return;
            }

            List<FilterEntry> group = _parent._filters
                .Where(candidate => candidate.Key == filter.Key)
                .ToList();
            foreach (FilterEntry groupFilter in group)
            {
                if (groupFilter.Widget.IsActive)
                {
                    groupFilter.Widget.Reset();
                }
                _draftFilterIds.Remove(groupFilter.FilterId);
            }
            _parent.ReleaseUnusedFilterColumns();
        }

        private void DrawPicker(Rect rect)
        {
            rect.CutTop(out Rect searchRowRect, ControlHeight);
            searchRowRect.CutLeft(out Rect searchLabelRect, 110f).CutLeft(GUIStyles.Global.PadSm).TakeRest(out Rect searchFieldRect);
            Widgets.Label(searchLabelRect, Localization.Get(Localization.SearchFilters));

            GUI.SetNextControlName(SearchControlName);
            _filterSearch = Widgets.TextField(searchFieldRect, _filterSearch);
            if (_focusSearchField && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(SearchControlName);
                _focusSearchField = false;
            }

            rect.yMin = searchRowRect.yMax + GUIStyles.Global.Pad;
            List<FilterPickerOption> options = GetPickerOptions();
            if (options.Count == 0)
            {
                Widgets.Label(rect, Localization.Get(Localization.NoFilterResults));
                return;
            }

            float contentHeight = 0f;
            foreach (FilterPickerGroup group in Enum.GetValues(typeof(FilterPickerGroup)))
            {
                if (options.Any(option => option.Group == group))
                {
                    contentHeight += Text.LineHeight + GUIStyles.Global.PadSm;
                    contentHeight += options.Count(option => option.Group == group) * PickerRowHeight;
                    contentHeight += GUIStyles.Global.Pad;
                }
            }

            Rect viewRect = new(0f, 0f, Mathf.Max(rect.width - GenUI.ScrollBarWidth, 1f), Mathf.Max(rect.height, contentHeight));
            using (new GUIScrollScope(rect, ref _pickerScrollPosition, viewRect))
            {
                float y = 0f;
                foreach (FilterPickerGroup group in Enum.GetValues(typeof(FilterPickerGroup)))
                {
                    List<FilterPickerOption> groupOptions = options.Where(option => option.Group == group).ToList();
                    if (groupOptions.Count == 0)
                    {
                        continue;
                    }

                    Rect headingRect = new(0f, y, viewRect.width, Text.LineHeight);
                    Widgets.Label(headingRect, GetPickerGroupLabel(group));
                    y = headingRect.yMax + GUIStyles.Global.PadSm;

                    foreach (FilterPickerOption option in groupOptions)
                    {
                        Rect optionRect = new(0f, y, viewRect.width, PickerRowHeight);
                        if (Widgets.ButtonText(optionRect, option.Label))
                        {
                            SelectPickerOption(option);
                            return;
                        }
                        y = optionRect.yMax;
                    }

                    y += GUIStyles.Global.Pad;
                }
            }
        }

        private List<FilterPickerOption> GetPickerOptions()
        {
            HashSet<string> displayedFilterIds = GetDisplayedFilters()
                .Select(filter => filter.FilterId)
                .ToHashSet();
            List<FilterPickerOption> options = [];

            foreach (FilterEntry tableFilter in _parent._filters
                         .Where(filter => filter.Column == null && displayedFilterIds.Contains(filter.FilterId) == false)
                         .OrderBy(filter => filter.LabelText))
            {
                options.Add(new FilterPickerOption(tableFilter.LabelText, FilterPickerGroup.Table, tableFilter, null));
            }

            foreach (ColumnDef columnDef in _parent._tableWorker.CompatibleColumns.OrderBy(column => column.LabelCap.RawText))
            {
                bool alreadyDisplayed = _parent._filters.Any(filter =>
                    filter.Key == columnDef.defName && displayedFilterIds.Contains(filter.FilterId));
                if (alreadyDisplayed)
                {
                    continue;
                }

                bool isVisible = _parent._columns.Any(column => column.Def == columnDef);
                options.Add(new FilterPickerOption(
                    columnDef.LabelCap,
                    isVisible ? FilterPickerGroup.Visible : FilterPickerGroup.Hidden,
                    null,
                    columnDef));
            }

            if (_filterSearch.Length > 0)
            {
                options = options
                    .Where(option => option.Label.IndexOf(_filterSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            return options;
        }

        private static string GetPickerGroupLabel(FilterPickerGroup group)
        {
            return group switch
            {
                FilterPickerGroup.Table => Localization.Get(Localization.TableFilters),
                FilterPickerGroup.Visible => Localization.Get(Localization.VisibleColumns),
                FilterPickerGroup.Hidden => Localization.Get(Localization.HiddenColumns),
                _ => "",
            };
        }

        private void SelectPickerOption(FilterPickerOption option)
        {
            if (option.TableFilter is { } tableFilter)
            {
                _draftFilterIds.Add(tableFilter.FilterId);
            }
            else if (option.ColumnDef != null)
            {
                _parent.AddColumnFilter(option.ColumnDef);
                foreach (FilterEntry filter in _parent._filters.Where(filter => filter.Key == option.ColumnDef.defName))
                {
                    _draftFilterIds.Add(filter.FilterId);
                }
            }

            ClosePicker();
        }

        private void ClosePicker()
        {
            _showPicker = false;
            _filterSearch = "";
            _pickerScrollPosition = Vector2.zero;
            GUI.FocusControl(null);
        }
    }
}
