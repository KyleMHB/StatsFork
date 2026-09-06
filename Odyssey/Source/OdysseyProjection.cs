using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace Stats.Compat.Odyssey;

internal interface IOdysseyProjection
{
    OdysseyThingProjection ProjectThing(ThingDef definition);
    FishingOutcomeProjection ProjectFishingOutcome(Def definition);
    IReadOnlyList<Def> GetRecords(OdysseyRecordKind kind);
}

public enum OdysseyRecordKind
{
    FishingOutcome,
}

internal static class OdysseyProjection
{
    private static IOdysseyProjection _current = new ReflectionOdysseyProjection();

    internal static IOdysseyProjection Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal static FishingOutcomeProjection ProjectFishingOutcome(Def definition)
    {
        return Current.ProjectFishingOutcome(definition);
    }
}

public sealed class OdysseyThingProjection
{
    internal string? Description { get; }
    internal IReadOnlyList<string> BookOutcomes { get; }
    internal IReadOnlyList<string> FishCategories { get; }
    internal GravshipProjection Gravship { get; }
    internal OrbitalInfrastructureProjection Orbital { get; }
    internal IReadOnlyList<string> UniqueWeaponTraits { get; }
    internal bool HasBook { get; }
    internal bool HasGravship { get; }
    internal bool HasOrbitalScanner { get; }
    internal bool HasOxygenPusher { get; }
    internal bool HasUniqueWeapon { get; }

    internal OdysseyThingProjection(
        string? description = null,
        IReadOnlyList<string>? bookOutcomes = null,
        IReadOnlyList<string>? fishCategories = null,
        GravshipProjection? gravship = null,
        OrbitalInfrastructureProjection? orbital = null,
        IReadOnlyList<string>? uniqueWeaponTraits = null,
        bool hasBook = false,
        bool hasGravship = false,
        bool hasOrbitalScanner = false,
        bool hasOxygenPusher = false,
        bool hasUniqueWeapon = false)
    {
        Description = description;
        BookOutcomes = Copy(bookOutcomes);
        FishCategories = Copy(fishCategories);
        Gravship = gravship ?? new GravshipProjection();
        Orbital = orbital ?? new OrbitalInfrastructureProjection();
        UniqueWeaponTraits = Copy(uniqueWeaponTraits);
        HasBook = hasBook;
        HasGravship = hasGravship;
        HasOrbitalScanner = hasOrbitalScanner;
        HasOxygenPusher = hasOxygenPusher;
        HasUniqueWeapon = hasUniqueWeapon;
    }

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? values)
    {
        return values == null ? Array.Empty<string>() : values.ToArray();
    }
}

public sealed class GravshipProjection
{
    internal string? ComponentType { get; }
    internal decimal? Range { get; }
    internal decimal? SubstructureSupport { get; }
    internal decimal? FuelSavings { get; }
    internal decimal? MaxSimultaneous { get; }
    internal decimal? MaxDistance { get; }
    internal decimal? DirectionInfluence { get; }

    internal GravshipProjection(
        string? componentType = null,
        decimal? range = null,
        decimal? substructureSupport = null,
        decimal? fuelSavings = null,
        decimal? maxSimultaneous = null,
        decimal? maxDistance = null,
        decimal? directionInfluence = null)
    {
        ComponentType = componentType;
        Range = range;
        SubstructureSupport = substructureSupport;
        FuelSavings = fuelSavings;
        MaxSimultaneous = maxSimultaneous;
        MaxDistance = maxDistance;
        DirectionInfluence = directionInfluence;
    }
}

public sealed class OrbitalInfrastructureProjection
{
    internal IReadOnlyList<string> Functions { get; }
    internal decimal? LowPowerFactor { get; }
    internal decimal? AirPerSecond { get; }

    internal OrbitalInfrastructureProjection(
        IReadOnlyList<string>? functions = null,
        decimal? lowPowerFactor = null,
        decimal? airPerSecond = null)
    {
        Functions = functions == null ? Array.Empty<string>() : functions.ToArray();
        LowPowerFactor = lowPowerFactor;
        AirPerSecond = airPerSecond;
    }
}

public sealed class FishingOutcomeProjection
{
    internal string? Label { get; }
    internal string? FishType { get; }
    internal string? LetterDefinition { get; }
    internal string? LetterText { get; }
    internal string? DamageDefinition { get; }
    internal string? DamageAmountRange { get; }
    internal string? Hediff { get; }
    internal decimal? Severity { get; }

    internal FishingOutcomeProjection(
        string? label = null,
        string? fishType = null,
        string? letterDefinition = null,
        string? letterText = null,
        string? damageDefinition = null,
        string? damageAmountRange = null,
        string? hediff = null,
        decimal? severity = null)
    {
        Label = label;
        FishType = fishType;
        LetterDefinition = letterDefinition;
        LetterText = letterText;
        DamageDefinition = damageDefinition;
        DamageAmountRange = damageAmountRange;
        Hediff = hediff;
        Severity = severity;
    }
}

internal sealed class ReflectionOdysseyProjection : IOdysseyProjection
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] GravshipCompTypeNames = ["CompProperties_GravshipFacility", "CompProperties_GravshipThruster"];

    public OdysseyThingProjection ProjectThing(ThingDef definition)
    {
        try
        {
            object? book = GetComp(definition, "CompProperties_Book");
            object? facility = GetComp(definition, GravshipCompTypeNames[0]);
            object? thruster = GetComp(definition, GravshipCompTypeNames[1]);
            object? lowPower = GetComp(definition, "CompProperties_LowPowerUnlessVacuum");
            object? oxygen = GetComp(definition, "CompProperties_OxygenPusher");
            object? uniqueWeapon = GetComp(definition, "CompProperties_UniqueWeapon");

            return new OdysseyThingProjection(
                description: definition.description,
                bookOutcomes: ProjectBookOutcomes(book),
                fishCategories: ProjectFishCategories(definition),
                gravship: ProjectGravship(facility, thruster),
                orbital: ProjectOrbital(definition, lowPower, oxygen),
                uniqueWeaponTraits: ProjectUniqueWeaponTraits(uniqueWeapon),
                hasBook: book != null,
                hasGravship: facility != null || thruster != null,
                hasOrbitalScanner: HasCompClass(definition, "CompOrbitalScanner"),
                hasOxygenPusher: oxygen != null,
                hasUniqueWeapon: uniqueWeapon != null);
        }
        catch
        {
            return new OdysseyThingProjection(description: definition?.description);
        }
    }

    public IReadOnlyList<Def> GetRecords(OdysseyRecordKind kind)
    {
        try
        {
            string defTypeName = kind switch
            {
                OdysseyRecordKind.FishingOutcome => "NegativeFishingOutcomeDef",
                _ => string.Empty,
            };
            if (defTypeName.Length == 0)
            {
                return Array.Empty<Def>();
            }

            Type? defType = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .FirstOrDefault(type => type.Name == defTypeName && typeof(Def).IsAssignableFrom(type));
            if (defType == null)
            {
                return Array.Empty<Def>();
            }

            Type databaseType = typeof(DefDatabase<>).MakeGenericType(defType);
            PropertyInfo? property = databaseType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
            return property?.GetValue(null) is IEnumerable<Def> defs ? defs.ToArray() : Array.Empty<Def>();
        }
        catch
        {
            return Array.Empty<Def>();
        }
    }

    public FishingOutcomeProjection ProjectFishingOutcome(Def definition)
    {
        try
        {
            return new FishingOutcomeProjection(
                label: GetString(GetMemberValue(definition, "letterLabel")) ?? definition.LabelCap.RawText,
                fishType: GetString(GetMemberValue(definition, "fishType")),
                letterDefinition: GetString(GetMemberValue(definition, "letterDef")),
                letterText: GetString(GetMemberValue(definition, "letterText")),
                damageDefinition: GetString(GetMemberValue(definition, "damageDef")),
                damageAmountRange: GetString(GetMemberValue(definition, "damageAmountRange")),
                hediff: GetString(GetMemberValue(definition, "addsHediff")),
                severity: TryGetDecimal(GetMemberValue(definition, "hediffSeverity"), out decimal severity) ? severity : null);
        }
        catch
        {
            return new FishingOutcomeProjection(label: definition?.LabelCap.RawText);
        }
    }

    private static GravshipProjection ProjectGravship(object? facility, object? thruster)
    {
        object? range = GetStatOffset(facility, "GravshipRange") ?? GetStatOffset(thruster, "GravshipRange");
        object? substructure = GetStatOffset(facility, "SubstructureSupport") ?? GetStatOffset(thruster, "SubstructureSupport");
        return new GravshipProjection(
            componentType: GetString(GetGravshipMember(facility, thruster, "componentTypeDef")),
            range: TryGetDecimal(range, out decimal rangeValue) ? rangeValue : null,
            substructureSupport: TryGetDecimal(substructure, out decimal substructureValue) ? substructureValue : null,
            fuelSavings: TryGetDecimal(GetGravshipMember(facility, thruster, "fuelSavingsPercent"), out decimal fuelSavings) ? fuelSavings : null,
            maxSimultaneous: TryGetDecimal(GetGravshipMember(facility, thruster, "maxSimultaneous"), out decimal maxSimultaneous) ? maxSimultaneous : null,
            maxDistance: TryGetDecimal(GetGravshipMember(facility, thruster, "maxDistance"), out decimal maxDistance) ? maxDistance : null,
            directionInfluence: TryGetDecimal(GetGravshipMember(facility, thruster, "directionInfluence"), out decimal directionInfluence) ? directionInfluence : null);
    }

    private static OrbitalInfrastructureProjection ProjectOrbital(ThingDef definition, object? lowPower, object? oxygen)
    {
        List<string> functions = [];
        if (HasCompClass(definition, "CompOrbitalScanner"))
        {
            functions.Add("Orbital scanner");
        }
        if (oxygen != null)
        {
            functions.Add("Oxygen");
        }
        if (definition.thingClass?.Name == "Building_VacBarrier")
        {
            functions.Add("Vac barrier");
        }

        return new OrbitalInfrastructureProjection(
            functions,
            TryGetDecimal(GetMemberValue(lowPower, "lowPowerConsumptionFactor"), out decimal lowPowerFactor) ? lowPowerFactor : null,
            TryGetDecimal(GetMemberValue(oxygen, "airPerSecondPerHundredCells"), out decimal airPerSecond) ? airPerSecond : null);
    }

    private static IReadOnlyList<string> ProjectBookOutcomes(object? book)
    {
        IEnumerable<object> doers = Enumerate(GetMemberValue(book, "doers"));
        return doers
            .Select(doer => GetString(GetMemberValue(doer, "label"))
                ?? GetString(GetMemberValue(doer, "outcomeDoer"))
                ?? doer.GetType().Name)
            .Where(label => string.IsNullOrEmpty(label) == false)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<string> ProjectFishCategories(ThingDef definition)
    {
        return definition.thingCategories?
            .Select(category => category.LabelCap.RawText)
            .Where(label => string.IsNullOrEmpty(label) == false)
            .ToArray() ?? Array.Empty<string>();
    }

    private static IReadOnlyList<string> ProjectUniqueWeaponTraits(object? uniqueWeapon)
    {
        IEnumerable<object> traits = Enumerate(GetMemberValue(uniqueWeapon, "traits"));
        if (!traits.Any())
        {
            traits = Enumerate(GetMemberValue(uniqueWeapon, "weaponTraits"));
        }

        return traits
            .Select(GetString)
            .Where(label => string.IsNullOrEmpty(label) == false)
            .ToArray()!;
    }

    private static object? GetGravshipMember(object? facility, object? thruster, string memberName)
    {
        return GetMemberValue(facility, memberName) ?? GetMemberValue(thruster, memberName);
    }

    private static object? GetStatOffset(object? component, string statDefName)
    {
        foreach (object statOffset in Enumerate(GetMemberValue(component, "statOffsets")))
        {
            if (GetString(GetMemberValue(statOffset, "stat")) == statDefName)
            {
                return GetMemberValue(statOffset, "value");
            }
        }

        return null;
    }

    private static object? GetComp(ThingDef definition, string typeName)
    {
        return definition.comps?.FirstOrDefault(comp => comp.GetType().Name == typeName);
    }

    private static bool HasCompClass(ThingDef definition, string typeName)
    {
        return definition.comps?.Any(comp => GetString(GetMemberValue(comp, "compClass")) == typeName) == true;
    }

    private static object? GetMemberValue(object? instance, string memberName)
    {
        if (instance == null)
        {
            return null;
        }

        try
        {
            Type type = instance.GetType();
            FieldInfo? field = type.GetField(memberName, MemberFlags);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo? property = type.GetProperty(memberName, MemberFlags);
            return property?.GetIndexParameters().Length == 0 ? property.GetValue(instance) : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<object> Enumerate(object? value)
    {
        if (value == null || value is string)
        {
            return Array.Empty<object>();
        }

        return value is IEnumerable enumerable ? enumerable.Cast<object>() : new[] { value };
    }

    private static string? GetString(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                Def def => def.LabelCap.RawText.NullOrEmpty() ? def.defName : def.LabelCap.RawText,
                Type type => type.Name,
                TaggedString taggedString => taggedString.RawText,
                string text => text,
                _ => value.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetDecimal(object? valueObject, out decimal value)
    {
        try
        {
            switch (valueObject)
            {
                case null:
                    value = 0m;
                    return false;
                case decimal decimalValue:
                    value = decimalValue;
                    return true;
                case IConvertible convertible:
                    value = convertible.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
                default:
                    value = 0m;
                    return decimal.TryParse(valueObject.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }
        catch
        {
            value = 0m;
            return false;
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
