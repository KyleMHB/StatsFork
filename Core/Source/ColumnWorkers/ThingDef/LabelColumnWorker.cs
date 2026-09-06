using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Stats.ColumnWorkers.Cells;
using Stats.Filters;
using Stats.TableWorkers;
using Stats.Utils;
using Stats.Utils.Extensions;
using Stats.Utils.Widgets;
using UnityEngine;
using Verse;
using static Stats.GUIStyles.TableCell;

namespace Stats.ColumnWorkers.ThingDef;

// Note to myself: Don't remove stuff label. It's important because
// modded stuffs may have the same color as vanilla ones or other modded stuffs.
// Replacing label with icon won't do, because ex. all of the leathers have the same
// icon but of different color.
public sealed class LabelColumnWorker(ColumnDef columnDef) : ColumnWorker<DefBasedObject, LabelColumnWorker.LabelCell>
{
    private enum ResearchStatus
    {
        Researched,
        NotResearched,
        NoResearchRequired,
    }

    public override ColumnType Type => ColumnType.String;
    public override ColumnDef Def => columnDef;
    public override bool ShouldDrawCellsNow => Event.current.type == EventType.Repaint || Event.current.IsLeftMouseInteraction();
    private Action? _researchCompletedHandler;

    protected override LabelCell MakeCell(DefBasedObject @object)
    {
        if (@object.Def is Verse.ThingDef thingDef)
        {
            return new LabelCell(thingDef, @object.StuffDef);
        }

        return default;
    }

    public override ICollection<CellField> GetCellFields(TableWorker tableWorker)
    {
        if (_researchCompletedHandler != null)
        {
            Events.ResearchCompleted -= _researchCompletedHandler;
            _researchCompletedHandler = null;
        }

        Filter textFieldFilter = new StringFilter((int row) => this[row].Text ?? "");
        int CompareText(int row1, int row2) => Comparer<string?>.Default.Compare(this[row1].Text, this[row2].Text);
        CellField textField = new(Def.TitleWidget, textFieldFilter, CompareText);

        OTMFilter<ResearchStatus> researchStatusFilter = new(
            row => GetResearchStatus(this[row].ThingDef),
            [
                new(ResearchStatus.Researched, Localization.Get(Localization.Researched)),
                new(ResearchStatus.NotResearched, Localization.Get(Localization.NotResearched)),
                new(ResearchStatus.NoResearchRequired, Localization.Get(Localization.NoResearchRequired)),
            ]
        );
        _researchCompletedHandler = () =>
        {
            if (researchStatusFilter.IsActive)
            {
                researchStatusFilter.NotifyChanged();
            }
        };
        Events.ResearchCompleted += _researchCompletedHandler;
        CellField researchStatusField = new(
            new Label(Localization.Get(Localization.ResearchStatus)),
            researchStatusFilter,
            (row1, row2) => GetResearchStatus(this[row1].ThingDef).CompareTo(GetResearchStatus(this[row2].ThingDef))
        );

        return [textField, researchStatusField];
    }

    public override void Dispose()
    {
        if (_researchCompletedHandler == null)
        {
            return;
        }

        Events.ResearchCompleted -= _researchCompletedHandler;
        _researchCompletedHandler = null;
    }

    private static ResearchStatus GetResearchStatus(Verse.ThingDef? thingDef)
    {
        if (thingDef == null)
        {
            return ResearchStatus.NoResearchRequired;
        }

        HashSet<ResearchProjectDef>? researchProjects = thingDef.GetResearchProjectDefs();
        if (researchProjects == null || researchProjects.Count == 0)
        {
            return ResearchStatus.NoResearchRequired;
        }

        return researchProjects.All(researchProjectDef => researchProjectDef.IsFinished)
            ? ResearchStatus.Researched
            : ResearchStatus.NotResearched;
    }

    public readonly struct LabelCell : ICell
    {
        public float Width { get; }
        public bool IsRefreshable => false;
        public readonly Verse.ThingDef? ThingDef;
        public readonly string? Text;

        private readonly Verse.ThingDef? _stuffDef;
        private readonly ThingDefIcon? _icon;
        private readonly float _iconWidth;

        public LabelCell(Verse.ThingDef thingDef, Verse.ThingDef? stuffDef = null)
        {
            ThingDef = thingDef;
            _stuffDef = stuffDef;
            Text = stuffDef == null
                ? thingDef.LabelCap.RawText
                : $"{stuffDef.LabelAsStuff.CapitalizeFirst()} {thingDef.label}";
            float textWidth = Text.CalcSize(StringNoPad).x;
            _icon = new ThingDefIcon(thingDef, stuffDef);
            _iconWidth = _icon.Size.x;
            Width = _iconWidth + ContentSpacing + textWidth;
        }

        public void Draw(Rect rect)
        {
            if (ThingDef != null)
            {
                rect
                    .ContractedByObjectTableCellPadding()
                    .CutLeft(out Rect iconRect, _iconWidth)
                    .CutLeft(ContentSpacing)
                    .TakeRest(out Rect labelRect);

                iconRect = iconRect.Centered(_icon!.Size);

                if (Event.current.type == EventType.Repaint)
                {
                    _icon.Draw(iconRect);
                    Text!.Draw(labelRect, StringNoPad);
                }

                bool iconWasClicked = iconRect.ButtonGhostly();
                if (iconWasClicked)
                {
                    ThingDef.OpenInfoDialog(_stuffDef);
                }
            }
        }
    }

    /*
    
    TODO: 
    
    Instead of having this "filter", have two sets of tables for thing defs that can be made from stuff:
    - A table that lists every def + stuff variant. Default columns are ones whose values depend on stuff.
    - A table that lists bases (without default stuff). Default columns are ones whose values do not depend on stuff.

    Note:

    This being a filter is a hack. It doesn't work in "OR" mode and is semantically incorrect.
    
    There are 2 ways of fixing this issue.

    The easy way is to introduce some special/pre filters that would be displayed in window's toolbar 
    and be applied separatedly.

    The hard way is to allow for grouping table's rows by a column.
    The issue that is being solved by this filter is "show me only values that do not depend on stuff".
    Grouping can be implemented by implementing "Equals" on a cell.
    
    */
    //private sealed class StuffedVariantsDisplayModeToggleButton : FilterWidget
    //{
    //    private bool _IsActive = false;
    //    public override bool IsActive => _IsActive;
    //    public override event Action? OnChange;
    //    private Texture2D Texture => _IsActive ? Verse.Widgets.CheckboxOnTex : Verse.Widgets.CheckboxOffTex;
    //    private static readonly TipSignal Manual =
    //        "Click to show only distinct item material variants.\n\n" +
    //        "Material for each distinct variant is chosen based on item's type definition.";
    //    public StuffedVariantsDisplayModeToggleButton()
    //    {
    //    }
    //    protected override Vector2 GetSize()
    //    {
    //        return new Vector2(Text.LineHeight, Text.LineHeight);
    //    }
    //    public override void Draw(Rect rect, Vector2 _)
    //    {
    //        var origTextAnchor = Text.Anchor;
    //        Text.Anchor = TextAnchor.LowerLeft;

    //        var origGUIColor = GUI.color;
    //        if (_IsActive == false)
    //        {
    //            GUI.color = Globals.GUI.TextColorSecondary;
    //        }

    //        if (Widgets.Draw.ButtonImageSubtle(rect, Texture))
    //        {
    //            _IsActive = !_IsActive;

    //            OnChange?.Invoke();
    //        }

    //        Text.Anchor = origTextAnchor;
    //        GUI.color = origGUIColor;

    //        TooltipHandler.TipRegion(rect, Manual);
    //    }
    //    public override bool Eval(ObjectTableWidget.Cell cell)
    //    {
    //        if (_IsActive)
    //        {
    //            // Do not filter out stuffless things.
    //            // - You can filter them out with stuff filter.
    //            // - There are cases where one may want to compare
    //            //   things by stats unrelated to stuff. Ex. equipped
    //            //   stat offsets.
    //            return ((Cell)cell).IsMadeFromDefaultStuff;
    //        }

    //        return true;
    //    }
    //    public override void Reset()
    //    {
    //    }
    //    public override void NotifyChanged()
    //    {
    //        OnChange?.Invoke();
    //    }
    //}
}
