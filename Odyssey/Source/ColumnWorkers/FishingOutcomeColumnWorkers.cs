using System.Collections.Generic;
using Stats.ColumnWorkers;
using Stats.ColumnWorkers.Cells;
using Stats.Filters;
using Stats.TableWorkers;
using Stats.Utils;
using UnityEngine;
using Verse;

namespace Stats.Compat.Odyssey;

public abstract class FishingOutcomeTextColumnWorker(ColumnDef columnDef) : ColumnWorker<Def, FishingOutcomeTextColumnWorker.TextCell>
{
    public override ColumnDef Def => columnDef;
    public override ColumnType Type => ColumnType.String;

    protected override TextCell MakeCell(Def def)
    {
        return new TextCell(GetText(OdysseyProjection.ProjectFishingOutcome(def)));
    }

    protected abstract string? GetText(FishingOutcomeProjection projection);

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

public abstract class FishingOutcomeNumberColumnWorker(ColumnDef columnDef, string formatString = "") : NumberColumnWorker<Def, NumberCell>
{
    public override ColumnDef Def => columnDef;

    protected override NumberCell MakeCell(Def def)
    {
        return TryGetValue(OdysseyProjection.ProjectFishingOutcome(def), out decimal value) ? new NumberCell(value, formatString) : default;
    }

    protected abstract bool TryGetValue(FishingOutcomeProjection projection, out decimal value);
}

public sealed class FishingOutcomeLabelColumnWorker(ColumnDef columnDef) : FishingOutcomeTextColumnWorker(columnDef)
{
    protected override string? GetText(FishingOutcomeProjection projection)
    {
        return projection.Label;
    }
}

public sealed class FishingOutcomeFishTypeColumnWorker(ColumnDef columnDef) : FishingOutcomeTextColumnWorker(columnDef)
{
    protected override string? GetText(FishingOutcomeProjection projection)
    {
        return projection.FishType;
    }
}

public sealed class FishingOutcomeLetterColumnWorker(ColumnDef columnDef) : FishingOutcomeTextColumnWorker(columnDef)
{
    protected override string? GetText(FishingOutcomeProjection projection)
    {
        string? letterDef = projection.LetterDefinition;
        string? letterText = projection.LetterText;

        if (letterDef.NullOrEmpty())
        {
            return letterText;
        }

        if (letterText.NullOrEmpty())
        {
            return letterDef;
        }

        return $"{letterDef}: {letterText}";
    }
}

public sealed class FishingOutcomeDamageColumnWorker(ColumnDef columnDef) : FishingOutcomeTextColumnWorker(columnDef)
{
    protected override string? GetText(FishingOutcomeProjection projection)
    {
        string? damageDef = projection.DamageDefinition;
        string? damageAmountRange = projection.DamageAmountRange;

        if (damageDef.NullOrEmpty())
        {
            return damageAmountRange;
        }

        return damageAmountRange.NullOrEmpty() ? damageDef : $"{damageDef} ({damageAmountRange})";
    }
}

public sealed class FishingOutcomeHediffColumnWorker(ColumnDef columnDef) : FishingOutcomeTextColumnWorker(columnDef)
{
    protected override string? GetText(FishingOutcomeProjection projection)
    {
        return projection.Hediff;
    }
}

public sealed class FishingOutcomeSeverityColumnWorker(ColumnDef columnDef) : FishingOutcomeNumberColumnWorker(columnDef, "0.###")
{
    protected override bool TryGetValue(FishingOutcomeProjection projection, out decimal value)
    {
        if (projection.Severity.HasValue)
        {
            value = projection.Severity.Value;
            return true;
        }

        value = 0m;
        return false;
    }
}

public sealed class FishingOutcomeContentSourceColumnWorker(ColumnDef columnDef) : OdysseyDefContentSourceColumnWorker(columnDef);
