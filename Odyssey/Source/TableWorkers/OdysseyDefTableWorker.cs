using System;
using System.Collections.Generic;
using System.Linq;
using Stats.TableWorkers;
using Verse;

namespace Stats.Compat.Odyssey;

public abstract class OdysseyNamedDefTableWorker : TableWorker<Def>, IRefRecordsProvider<Def>
{
    public override List<Def> InitialObjects { get; }

    IEnumerable<Def> IRefRecordsProvider<Def>.Records => InitialObjects;

    public override event Action<Def>? OnObjectAdded;
    public override event Action<Def>? OnObjectRemoved;

    protected OdysseyNamedDefTableWorker(TableDef tableDef, OdysseyRecordKind recordKind) : base(tableDef)
    {
        InitialObjects = OdysseyProjection.Current.GetRecords(recordKind).ToList();
    }
}

public sealed class NegativeFishingOutcomeTableWorker(TableDef tableDef) : OdysseyNamedDefTableWorker(tableDef, OdysseyRecordKind.FishingOutcome);
