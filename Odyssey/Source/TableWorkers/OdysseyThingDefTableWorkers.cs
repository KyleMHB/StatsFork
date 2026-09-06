using System;
using System.Collections.Generic;
using RimWorld;
using Stats.TableWorkers.ThingDef;
using Verse;

namespace Stats.Compat.Odyssey;

public abstract class OdysseyThingDefTableWorker(TableDef tableDef) : ThingDefTableWorker(tableDef)
{
    private static readonly StringComparer DefNameComparer = StringComparer.Ordinal;

    protected static bool IsOdysseyThingDef(ThingDef thingDef)
    {
        string? packageId = thingDef.modContentPack?.PackageIdPlayerFacing;
        return packageId != null && packageId.Equals("Ludeon.RimWorld.Odyssey", StringComparison.OrdinalIgnoreCase);
    }

    protected static bool HasThingCategory(ThingDef thingDef, string categoryDefName)
    {
        return thingDef.thingCategories?.Any(category => category.defName == categoryDefName) == true;
    }

    protected static bool DefNameIn(ThingDef thingDef, HashSet<string> defNames)
    {
        return defNames.Contains(thingDef.defName);
    }

    protected static HashSet<string> DefNameSet(params string[] defNames)
    {
        return new HashSet<string>(defNames, DefNameComparer);
    }
}

public sealed class BookTableWorker(TableDef tableDef) : OdysseyThingDefTableWorker(tableDef)
{
    protected override bool IsValidThingDef(ThingDef thingDef)
    {
        return IsOdysseyThingDef(thingDef)
            && (thingDef.thingClass?.Name == "Book" || OdysseyProjection.Current.ProjectThing(thingDef).HasBook);
    }
}

public sealed class FishTableWorker(TableDef tableDef) : OdysseyThingDefTableWorker(tableDef)
{
    protected override bool IsValidThingDef(ThingDef thingDef)
    {
        return IsOdysseyThingDef(thingDef)
            && (HasThingCategory(thingDef, "Fish") || thingDef.defName.StartsWith("Fish_", StringComparison.Ordinal));
    }
}

public sealed class GravshipSystemTableWorker(TableDef tableDef) : OdysseyThingDefTableWorker(tableDef)
{
    private static readonly HashSet<string> GravshipDefNames = DefNameSet(
        "GravshipHull",
        "GravEngine",
        "GravFieldExtender",
        "PilotConsole",
        "ChemfuelTank",
        "LargeChemfuelTank",
        "SmallThruster",
        "LargeThruster",
        "FuelOptimizer",
        "SignalJammer",
        "GravcorePowerCell",
        "PilotSubpersonaCore",
        "GravshipShieldGenerator",
        "GravAnchor"
    );

    protected override bool IsValidThingDef(ThingDef thingDef)
    {
        return IsOdysseyThingDef(thingDef)
            && (DefNameIn(thingDef, GravshipDefNames)
                || OdysseyProjection.Current.ProjectThing(thingDef).HasGravship);
    }
}

public sealed class OrbitalInfrastructureTableWorker(TableDef tableDef) : OdysseyThingDefTableWorker(tableDef)
{
    private static readonly HashSet<string> OrbitalInfrastructureDefNames = DefNameSet(
        "OrbitalScanner",
        "OxygenPump",
        "VacBarrier"
    );

    protected override bool IsValidThingDef(ThingDef thingDef)
    {
        return IsOdysseyThingDef(thingDef)
            && (DefNameIn(thingDef, OrbitalInfrastructureDefNames)
                || OdysseyProjection.Current.ProjectThing(thingDef).HasOrbitalScanner
                || OdysseyProjection.Current.ProjectThing(thingDef).HasOxygenPusher);
    }
}

public sealed class UniqueWeaponTableWorker(TableDef tableDef) : OdysseyThingDefTableWorker(tableDef)
{
    protected override bool IsValidThingDef(ThingDef thingDef)
    {
        return IsOdysseyThingDef(thingDef)
            && OdysseyProjection.Current.ProjectThing(thingDef).HasUniqueWeapon
            && (thingDef.IsRangedWeapon || thingDef.IsMeleeWeapon);
    }
}
