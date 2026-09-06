using System.Collections.Generic;
using System.Linq;
using Stats.ColumnWorkers;
using Stats.ColumnWorkers.Cells;
using Stats.Filters;
using Stats.TableWorkers;
using Stats.Utils;
using UnityEngine;
using Verse;

namespace Stats.Compat.Odyssey;

public abstract class OdysseyThingTextColumnWorker(ColumnDef columnDef) : ColumnWorker<DefBasedObject, OdysseyThingTextColumnWorker.TextCell>
{
    public override ColumnDef Def => columnDef;
    public override ColumnType Type => ColumnType.String;

    protected override TextCell MakeCell(DefBasedObject @object)
    {
        return @object.Def is ThingDef thingDef ? new TextCell(GetText(OdysseyProjection.Current.ProjectThing(thingDef))) : default;
    }

    protected abstract string? GetText(OdysseyThingProjection projection);

    public override ICollection<CellField> GetCellFields(TableWorker tableWorker)
    {
        Filter textFieldFilter = new StringFilter(row => this[row].Text ?? "");
        int Compare(int row1, int row2) => Comparer<string?>.Default.Compare(this[row1].Text, this[row2].Text);
        return [new CellField(Def.TitleWidget, textFieldFilter, Compare)];
    }

    public readonly struct TextCell : ICell
    {
        public float Width { get; }
        public bool IsRefreshable => false;
        public readonly string? Text;

        private readonly TipSignal _tooltip;

        public TextCell(string? text)
        {
            Text = text;
            string preview = text?.Truncate(80) ?? "";
            Width = Verse.Text.CalcSize(preview).x;
            _tooltip = text ?? "";
        }

        public void Draw(Rect rect)
        {
            if (Text != null)
            {
                rect
                    .Label(Text.Truncate(80), GUIStyles.TableCell.String)
                    .Tip(_tooltip);
            }
        }
    }
}

public abstract class OdysseyThingNumberColumnWorker(ColumnDef columnDef, string formatString = "") : NumberColumnWorker<DefBasedObject, NumberCell>
{
    public override ColumnDef Def => columnDef;

    protected override NumberCell MakeCell(DefBasedObject @object)
    {
        return @object.Def is ThingDef thingDef && TryGetValue(OdysseyProjection.Current.ProjectThing(thingDef), out decimal value)
            ? new NumberCell(value, formatString)
            : default;
    }

    protected abstract bool TryGetValue(OdysseyThingProjection projection, out decimal value);
}

public sealed class OdysseyThingDescriptionColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.Description;
    }
}

public sealed class BookOutcomesColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.BookOutcomes.Count == 0 ? null : string.Join(", ", projection.BookOutcomes);
    }
}

public sealed class FishCategoriesColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.FishCategories.Count == 0 ? null : string.Join(", ", projection.FishCategories);
    }
}

public sealed class GravshipComponentTypeColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.Gravship.ComponentType;
    }
}

public sealed class GravshipRangeColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef)
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.Range.HasValue)
        {
            value = projection.Gravship.Range.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class GravshipSubstructureSupportColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef)
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.SubstructureSupport.HasValue)
        {
            value = projection.Gravship.SubstructureSupport.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class GravshipFuelSavingsColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef, "0.#%")
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.FuelSavings.HasValue)
        {
            value = projection.Gravship.FuelSavings.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class GravshipMaxSimultaneousColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef)
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.MaxSimultaneous.HasValue)
        {
            value = projection.Gravship.MaxSimultaneous.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class GravshipMaxDistanceColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef)
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.MaxDistance.HasValue)
        {
            value = projection.Gravship.MaxDistance.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class GravshipDirectionInfluenceColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef)
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Gravship.DirectionInfluence.HasValue)
        {
            value = projection.Gravship.DirectionInfluence.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class OrbitalInfrastructureFunctionColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.Orbital.Functions.Count == 0 ? null : string.Join(", ", projection.Orbital.Functions);
    }
}

public sealed class OrbitalInfrastructureLowPowerFactorColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef, "0.#%")
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Orbital.LowPowerFactor.HasValue)
        {
            value = projection.Orbital.LowPowerFactor.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class OrbitalInfrastructureAirPerSecondColumnWorker(ColumnDef columnDef) : OdysseyThingNumberColumnWorker(columnDef, "0.####")
{
    protected override bool TryGetValue(OdysseyThingProjection projection, out decimal value)
    {
        if (projection.Orbital.AirPerSecond.HasValue)
        {
            value = projection.Orbital.AirPerSecond.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class UniqueWeaponTraitsColumnWorker(ColumnDef columnDef) : OdysseyThingTextColumnWorker(columnDef)
{
    protected override string? GetText(OdysseyThingProjection projection)
    {
        return projection.UniqueWeaponTraits.Count == 0 ? null : string.Join(", ", projection.UniqueWeaponTraits);
    }
}
