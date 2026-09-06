using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Stats.Utils;
using Stats.Utils.Extensions;
using UnityEngine;
using Verse;
using ButtonStyle = Stats.GUIStyles.TableToolbarButton;
using Style = Stats.GUIStyles.TableToolbar;

namespace Stats;

internal sealed partial class ObjectTable<TObject>
{
    private sealed class Toolbar
    {
        private readonly ObjectTable<TObject> _parent;
        private readonly Button _filtersButton;
        private readonly Button _columnsMenuButton;
        private readonly Button _columnPresetsButton;
        private readonly Button _variantsButton;
        private readonly Button _valuesButton;
        private readonly float _qualityButtonWidth;
        private ColumnsFloatMenu ColumnsMenu => field ??= MakeColumnsMenu();

        public Toolbar(ObjectTable<TObject> parent)
        {
            _parent = parent;
            _filtersButton = new Button(Assets.TableFiltersTabIcon, Localization.Get(Localization.Filters));
            _columnsMenuButton = new Button(Assets.TableColumnsMenuIcon, Localization.Get(Localization.Columns));
            _columnPresetsButton = new Button(Verse.TexButton.Paste, Localization.Get(Localization.Presets));
            _variantsButton = new Button(Verse.Widgets.CheckboxOffTex, Localization.Get(Localization.Variants));
            _valuesButton = new Button(TexButton.Info, Localization.Get(Localization.Values));
            _qualityButtonWidth = QualityCategories()
                .Select(quality => $"{Localization.Get(Localization.Quality)}: {quality.GetLabel()}")
                .Max(label => ButtonStyle.PadHor * 2f + label.CalcSize(ButtonStyle.LabelStyle).x);
        }

        public void Draw(Rect rect)
        {
            // Layout
            Rect remainingRect = rect;
            Rect variantsButtonRect = default;
            Rect qualityButtonRect = default;
            remainingRect = remainingRect.CutLeft(out Rect filtersTabButtonRect, _filtersButton.Width);
            remainingRect = remainingRect.CutLeft(Style.Gap);
            remainingRect = remainingRect.CutLeft(out Rect columnsMenuButtonRect, _columnsMenuButton.Width);

            if (_parent.SupportsVariants)
            {
                remainingRect = remainingRect.CutLeft(Style.Gap);
                remainingRect = remainingRect.CutLeft(out variantsButtonRect, _variantsButton.Width);
            }

            remainingRect = remainingRect.CutLeft(Style.Gap);
            remainingRect = remainingRect.CutLeft(out Rect valuesButtonRect, _valuesButton.Width);

            if (_parent.SupportsQuality)
            {
                remainingRect = remainingRect.CutLeft(Style.Gap);
                remainingRect = remainingRect.CutLeft(out qualityButtonRect, _qualityButtonWidth);
            }

            remainingRect = remainingRect.CutLeft(Style.Gap);
            remainingRect = remainingRect.CutLeft(out Rect columnPresetsButtonRect, _columnPresetsButton.Width);

            remainingRect = remainingRect.CutRight(out Rect infoIconRect, remainingRect.height);

            if (Event.current.type == EventType.Repaint)
            {
                rect.DrawBorderBottom(GUIStyles.MainTabWindow.BorderColor);
            }

            // Buttons
            bool filtersTabButtonWasClicked = _filtersButton.Draw(filtersTabButtonRect);
            bool columnsMenuButtonWasClicked = _columnsMenuButton.Draw(columnsMenuButtonRect);
            bool columnPresetsButtonWasClicked = _columnPresetsButton.Draw(columnPresetsButtonRect);
            bool variantsButtonWasClicked = _parent.SupportsVariants && _variantsButton.Draw(
                variantsButtonRect,
                _parent.ShowVariants ? Verse.Widgets.CheckboxOnTex : Verse.Widgets.CheckboxOffTex);
            bool valuesButtonWasClicked = _valuesButton.Draw(
                valuesButtonRect,
                _parent.ExpandMultiValueCells ? Verse.Widgets.CheckboxOnTex : Verse.Widgets.CheckboxOffTex);
            valuesButtonRect.Tip(Localization.Get(
                _parent.ExpandMultiValueCells
                    ? Localization.ValuesCompactTip
                    : Localization.ValuesExpandTip));
            bool qualityButtonWasClicked = _parent.SupportsQuality && DrawQualityButton(qualityButtonRect);
            infoIconRect
                .ContractedBy(ButtonStyle.PadVer)
                .DrawTextureFitted(TexButton.Info)
                .Tip(_manual);

            // Events
            if (filtersTabButtonWasClicked)
            {
                _parent.ToggleFiltersTab();
            }
            else if (columnsMenuButtonWasClicked)
            {
                ColumnsMenu.RefreshSelection();
                ColumnsMenu.Open();
            }
            else if (columnPresetsButtonWasClicked)
            {
                MakePresetsMenu().Open();
            }
            else if (variantsButtonWasClicked)
            {
                _parent.ToggleVariants();
            }
            else if (qualityButtonWasClicked)
            {
                MakeQualityMenu().Open();
            }
            else if (valuesButtonWasClicked)
            {
                _parent.SetExpandedMultiValueCells(!_parent.ExpandMultiValueCells);
            }
        }

        private bool DrawQualityButton(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                rect
                    .ContractedBy(ButtonStyle.PadHor, ButtonStyle.PadVer)
                    .Label($"{Localization.Get(Localization.Quality)}: {_parent.Quality.GetLabel()}", ButtonStyle.LabelStyle);
            }

            return rect.ButtonGhostly();
        }

        private FloatMenu MakeQualityMenu()
        {
            List<FloatMenuOption> options = [];
            foreach (QualityCategory quality in QualityCategories())
            {
                options.Add(new FloatMenuOption(
                    quality.GetLabel(),
                    () => _parent.SetQuality(quality),
                    _parent.Quality == quality ? Verse.Widgets.CheckboxOnTex : Verse.Widgets.CheckboxOffTex,
                    Color.white));
            }

            return new FloatMenu(options);
        }

        private static IEnumerable<QualityCategory> QualityCategories()
        {
            return System.Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>();
        }

        private sealed class Button
        {
            public readonly float Width;

            private readonly Texture2D _icon;
            private readonly float _iconScale;
            private readonly string _label;

            public Button(Texture2D icon, string label, float iconScale = 1f)
            {
                _icon = icon;
                _iconScale = iconScale;
                _label = label;
                float labelWidth = label.CalcSize(ButtonStyle.LabelStyle).x;
                Width = ButtonStyle.PadHor * 2f + ButtonStyle.IconWidth + labelWidth;
            }

            public bool Draw(Rect rect)
            {
                return Draw(rect, _icon);
            }

            public bool Draw(Rect rect, Texture2D icon)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    rect
                        .ContractedBy(ButtonStyle.PadHor, ButtonStyle.PadVer)
                        .CutLeft(out Rect iconRect, ButtonStyle.IconWidth)
                        .TakeRest(out Rect labelRect);

                    iconRect.DrawTextureFitted(icon, _iconScale);
                    labelRect.Label(_label, ButtonStyle.LabelStyle);
                }

                return rect.ButtonGhostly();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ColumnsFloatMenu MakeColumnsMenu()
        {
            List<ColumnDef> compatibleColumns = _parent._tableWorker.CompatibleColumns;
            int compatibleColumnsCount = compatibleColumns.Count;
            List<ColumnsFloatMenuOption> columnsMenuOptions = new(compatibleColumnsCount);
            for (int i = 0; i < compatibleColumnsCount; i++)
            {
                ColumnDef columnDef = compatibleColumns[i];
                ColumnsFloatMenuOption menuOption = new(columnDef, _parent);
                columnsMenuOptions.Add(menuOption);
            }
            columnsMenuOptions.SortBy(option => option.Label);

            return new ColumnsFloatMenu(_parent, columnsMenuOptions);
        }

        private FloatMenu MakePresetsMenu()
        {
            List<FloatMenuOption> options =
            [
                new FloatMenuOption(Localization.Get(Localization.SaveCurrent), () =>
                {
                    Find.WindowStack.Add(new PresetNameWindow("", _parent.SavePreset));
                }, TexButton.Save, Color.white)
            ];

            foreach (TablePreset preset in _parent.GetPresets())
            {
                options.Add(new FloatMenuOption($"{Localization.Get(Localization.Apply)}: {preset.name}", () => _parent.ApplyPreset(preset), Verse.TexButton.Paste, Color.white));
                options.Add(new FloatMenuOption($"{Localization.Get(Localization.Overwrite)}: {preset.name}", () => _parent.SavePreset(preset.name), Verse.TexButton.Save, Color.white));
                options.Add(new FloatMenuOption(
                    preset.isDefault ? $"{Localization.Get(Localization.ClearDefault)}: {preset.name}" : $"{Localization.Get(Localization.SetDefault)}: {preset.name}",
                    () =>
                    {
                        if (preset.isDefault)
                        {
                            _parent.ClearDefaultPreset(preset);
                        }
                        else
                        {
                            _parent.SetDefaultPreset(preset);
                        }
                    }));
                options.Add(new FloatMenuOption($"{Localization.Get(Localization.Delete)}: {preset.name}", () => _parent.DeletePreset(preset), TexButton.Delete, Color.white));
            }

            return new FloatMenu(options);
        }

        private sealed class ColumnsFloatMenu : FloatMenu
        {
            private readonly ObjectTable<TObject> _parent;
            private readonly List<ColumnsFloatMenuOption> _columnOptions;

            public ColumnsFloatMenu(ObjectTable<TObject> parent, List<ColumnsFloatMenuOption> options) : base(options.Cast<FloatMenuOption>().ToList())
            {
                _parent = parent;
                _columnOptions = options;
            }

            public void RefreshSelection()
            {
                int optionsCount = _columnOptions.Count;
                for (int i = 0; i < optionsCount; i++)
                {
                    ColumnsFloatMenuOption option = _columnOptions[i];
                    if (_parent._tableSession.Current.VisibleColumnDefNames.Contains(
                        option.ColumnDef.defName,
                        global::System.StringComparer.Ordinal))
                    {
                        option.Select();
                    }
                    else
                    {
                        option.Unselect();
                    }
                }
            }
        }

        private sealed class ColumnsFloatMenuOption : FloatMenuOption
        {
            public readonly ColumnDef ColumnDef;

            public ColumnsFloatMenuOption(ColumnDef columnDef, ObjectTable<TObject> parent)
                : base(columnDef.LabelCap, null, Verse.Widgets.CheckboxOnTex, Color.white)
            {
                ColumnDef = columnDef;
                tooltip = columnDef.description;
                action = () =>
                {
                    if (parent._tableSession.Current.VisibleColumnDefNames.Contains(
                        columnDef.defName,
                        global::System.StringComparer.Ordinal))
                    {
                        parent.RemoveColumn(columnDef);
                    }
                    else
                    {
                        parent.AddColumn(columnDef);
                    }
                };
                if (parent._tableSession.Current.VisibleColumnDefNames.Contains(
                    columnDef.defName,
                    global::System.StringComparer.Ordinal) == false)
                {
                    Unselect();
                }
                if (columnDef.title != null)
                {
                    extraPartOnGUI = rect =>
                    {
                        rect = rect.ContractedBy(0f, (rect.height - columnDef.TitleWidget.Size.y) / 2f);
                        columnDef.TitleWidget.Draw(rect);
                        return false;
                    };
                    extraPartWidth = columnDef.TitleWidget.Size.x + GUIStyles.Global.PadSm;
                    extraPartRightJustified = true;
                }
            }

            public void Select()
            {
                iconColor.a = 1f;
            }

            public void Unselect()
            {
                iconColor.a = 0f;
            }

            public override bool DoGUI(Rect rect, bool colonistOrdering, FloatMenu floatMenu)
            {
                bool wordWrap = Verse.Text.WordWrap;
                Verse.Text.WordWrap = false;

                base.DoGUI(rect, colonistOrdering, floatMenu);

                Verse.Text.WordWrap = wordWrap;

                return false;
            }
        }
    }
}
