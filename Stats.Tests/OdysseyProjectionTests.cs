using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using NUnit.Framework;
using Stats.Compat.Odyssey;
using Verse;

namespace Stats.Tests;

[TestFixture]
public sealed class OdysseyProjectionTests
{
    private IOdysseyProjection _originalProjection = null!;

    [SetUp]
    public void CaptureProjection()
    {
        _originalProjection = OdysseyProjection.Current;
    }

    [TearDown]
    public void RestoreProjection()
    {
        OdysseyProjection.Current = _originalProjection;
    }

    [Test]
    public void In_memory_projection_exposes_typed_book_gravship_orbital_and_weapon_values()
    {
        var projection = new FakeProjection
        {
            Thing = new OdysseyThingProjection(
                description: "A book",
                bookOutcomes: new[] { "Learn", "Heal" },
                fishCategories: new[] { "Fish" },
                gravship: new GravshipProjection("Engine", 12.5m, 3m, 0.2m, 2m, 100m, 0.75m),
                orbital: new OrbitalInfrastructureProjection(new[] { "Oxygen" }, 0.5m, 4.25m),
                uniqueWeaponTraits: new[] { "Sharp", "Reliable" })
        };

        OdysseyProjection.Current = projection;
        OdysseyThingProjection result = OdysseyProjection.Current.ProjectThing(null!);

        Assert.That(result.BookOutcomes, Is.EqualTo(new[] { "Learn", "Heal" }));
        Assert.That(result.Gravship.Range, Is.EqualTo(12.5m));
        Assert.That(result.Gravship.DirectionInfluence, Is.EqualTo(0.75m));
        Assert.That(result.Orbital.Functions, Is.EqualTo(new[] { "Oxygen" }));
        Assert.That(result.UniqueWeaponTraits, Is.EqualTo(new[] { "Sharp", "Reliable" }));
    }

    [Test]
    public void Projection_adapter_contains_fishing_records_without_exposing_reflection_to_workers()
    {
        var projection = new FakeProjection();
        var outcome = new TestDef { defName = "Outcome" };
        projection.Records[OdysseyRecordKind.FishingOutcome] = new[] { outcome };
        projection.Fishing = new FishingOutcomeProjection(label: "bad catch", severity: 0.4m);
        OdysseyProjection.Current = projection;

        Assert.That(projection.GetRecords(OdysseyRecordKind.FishingOutcome), Is.EqualTo(new[] { outcome }));
        Assert.That(OdysseyProjection.ProjectFishingOutcome(outcome).Severity, Is.EqualTo(0.4m));
        Assert.That(OdysseyProjection.ProjectFishingOutcome(outcome).Label, Is.EqualTo("bad catch"));
    }

    [Test]
    public void Reflection_projection_maps_fishing_outcome_members()
    {
        var definition = NewDef<FishingOutcomeDefinition>();
        definition.letterLabel = "Storm damage";
        definition.fishType = "Cod";
        definition.letterDef = "Letter_Fishing";
        definition.letterText = "The line snapped.";
        definition.damageDef = "Scratch";
        definition.damageAmountRange = "1-3";
        definition.addsHediff = "Bruise";
        definition.hediffSeverity = "0.25";

        FishingOutcomeProjection result = new ReflectionOdysseyProjection().ProjectFishingOutcome(definition);

        Assert.That(result.Label, Is.EqualTo("Storm damage"));
        Assert.That(result.FishType, Is.EqualTo("Cod"));
        Assert.That(result.LetterDefinition, Is.EqualTo("Letter_Fishing"));
        Assert.That(result.LetterText, Is.EqualTo("The line snapped."));
        Assert.That(result.DamageDefinition, Is.EqualTo("Scratch"));
        Assert.That(result.DamageAmountRange, Is.EqualTo("1-3"));
        Assert.That(result.Hediff, Is.EqualTo("Bruise"));
        Assert.That(result.Severity, Is.EqualTo(0.25m));
    }

    [Test]
    public void Reflection_projection_maps_orbital_fields()
    {
        ThingDef definition = NewThingDef(
            new CompProperties_LowPowerUnlessVacuum { lowPowerConsumptionFactor = 0.5f },
            new CompProperties_OxygenPusher { airPerSecondPerHundredCells = 4.25 });

        OdysseyThingProjection result = new ReflectionOdysseyProjection().ProjectThing(definition);

        Assert.That(result.Orbital.LowPowerFactor, Is.EqualTo(0.5m));
        Assert.That(result.Orbital.AirPerSecond, Is.EqualTo(4.25m));
        Assert.That(result.Orbital.Functions, Is.EqualTo(new[] { "Oxygen" }));
    }

    [Test]
    public void Reflection_projection_maps_unique_weapon_traits_and_weapon_traits_fallback()
    {
        OdysseyThingProjection traits = new ReflectionOdysseyProjection().ProjectThing(
            NewThingDef(new CompProperties_UniqueWeapon { traits = new List<object> { "Sharp", "Reliable" } }));
        OdysseyThingProjection fallback = new ReflectionOdysseyProjection().ProjectThing(
            NewThingDef(new CompProperties_UniqueWeapon { weaponTraits = new List<object> { "Ancient" } }));

        Assert.That(traits.UniqueWeaponTraits, Is.EqualTo(new[] { "Sharp", "Reliable" }));
        Assert.That(fallback.UniqueWeaponTraits, Is.EqualTo(new[] { "Ancient" }));
    }

    [Test]
    public void Reflection_projection_maps_gravship_stat_offsets_and_numeric_values()
    {
        ThingDef definition = NewThingDef(new CompProperties_GravshipFacility
        {
            statOffsets = new List<object>
            {
                new StatOffsetDefinition { stat = "GravshipRange", value = "12.5" },
                new StatOffsetDefinition { stat = "SubstructureSupport", value = 3 }
            }
        });

        OdysseyThingProjection result = new ReflectionOdysseyProjection().ProjectThing(definition);

        Assert.That(result.Gravship.Range, Is.EqualTo(12.5m));
        Assert.That(result.Gravship.SubstructureSupport, Is.EqualTo(3m));
    }

    [Test]
    public void Reflection_projection_contains_a_throwing_getter_without_throwing()
    {
        ThingDef definition = NewThingDef(new CompProperties_Book(throwOnRead: true));

        Assert.DoesNotThrow(() => new ReflectionOdysseyProjection().ProjectThing(definition));
    }

    [Test]
    public void Reflection_projection_uses_fallback_labels_and_handles_missing_members()
    {
        ThingDef definition = NewThingDef(new CompProperties_Book());
        var projection = new ReflectionOdysseyProjection();

        OdysseyThingProjection result = projection.ProjectThing(definition);

        Assert.That(result.BookOutcomes, Is.EqualTo(new[] { "OutcomeDoerTest" }));
        Assert.That(result.Gravship.Range, Is.Null);
        Assert.That(result.Orbital.Functions, Is.Empty);
        Assert.That(result.UniqueWeaponTraits, Is.Empty);
    }

    [Test]
    public void Reflection_projection_supports_both_gravship_component_types_and_numeric_conversion()
    {
        ThingDef definition = NewThingDef(
                new CompProperties_GravshipFacility { fuelSavingsPercent = "0.25", maxDistance = 123 },
            new CompProperties_GravshipThruster { componentTypeDef = "Thruster", maxSimultaneous = 2.5f });

        OdysseyThingProjection result = new ReflectionOdysseyProjection().ProjectThing(definition);

        Assert.That(result.Gravship.ComponentType, Is.EqualTo("Thruster"));
        Assert.That(result.Gravship.FuelSavings, Is.EqualTo(0.25m));
        Assert.That(result.Gravship.MaxDistance, Is.EqualTo(123m));
        Assert.That(result.Gravship.MaxSimultaneous, Is.EqualTo(2.5m));
    }

    [Test]
    public void Reflection_projection_returns_empty_records_when_type_or_database_is_absent()
    {
        IReadOnlyList<Def> records = new ReflectionOdysseyProjection().GetRecords(OdysseyRecordKind.FishingOutcome);

        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Reflection_projection_contains_exception_containment_for_throwing_members()
    {
        ThingDef definition = NewThingDef(new ThrowingCompProperties());

        Assert.DoesNotThrow(() => new ReflectionOdysseyProjection().ProjectThing(definition));
    }

    [Test]
    public void Odyssey_workers_do_not_reference_the_reflection_adapter_directly()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Odyssey", "Source"));
        string[] workerFiles = Directory.GetFiles(Path.Combine(sourceRoot, "ColumnWorkers"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(sourceRoot, "TableWorkers"), "*.cs", SearchOption.AllDirectories))
            .ToArray();

        Assert.That(workerFiles, Is.Not.Empty);
        foreach (string workerFile in workerFiles)
        {
            string source = File.ReadAllText(workerFile);
            Assert.That(source, Does.Not.Contain("OdysseyReflection"), workerFile);
            Assert.That(source, Does.Not.Contain("System.Reflection"), workerFile);
            Assert.That(source, Does.Not.Contain("BindingFlags"), workerFile);
            Assert.That(source, Does.Not.Contain("GetMemberValue"), workerFile);
            Assert.That(source, Does.Not.Contain("GetProperty("), workerFile);
            Assert.That(source, Does.Not.Contain("GetField("), workerFile);
        }
    }

    private sealed class FakeProjection : IOdysseyProjection
    {
        internal OdysseyThingProjection Thing { get; set; } = new();
        internal FishingOutcomeProjection Fishing { get; set; } = new();
        internal Dictionary<OdysseyRecordKind, IReadOnlyList<Def>> Records { get; } = new();

        public OdysseyThingProjection ProjectThing(ThingDef definition) => Thing;

        public IReadOnlyList<Def> GetRecords(OdysseyRecordKind kind) =>
            Records.TryGetValue(kind, out IReadOnlyList<Def>? records) ? records : Array.Empty<Def>();

        public FishingOutcomeProjection ProjectFishingOutcome(Def definition) => Fishing;
    }

    private sealed class TestDef : Def
    {
    }

    private sealed class CompProperties_Book : CompProperties
    {
        private readonly bool _throwOnRead;

        internal CompProperties_Book(bool throwOnRead = false)
        {
            _throwOnRead = throwOnRead;
        }

        public object doers => _throwOnRead
            ? throw new InvalidOperationException("test getter")
            : new List<object> { new OutcomeDoerTest() };
    }

    private sealed class FishingOutcomeDefinition : Def
    {
        public object? letterLabel;
        public object? fishType;
        public object? letterDef;
        public object? letterText;
        public object? damageDef;
        public object? damageAmountRange;
        public object? addsHediff;
        public object? hediffSeverity;
    }

    private sealed class CompProperties_LowPowerUnlessVacuum : CompProperties
    {
        public object? lowPowerConsumptionFactor;
    }

    private sealed class CompProperties_OxygenPusher : CompProperties
    {
        public object? airPerSecondPerHundredCells;
    }

    private sealed class CompProperties_UniqueWeapon : CompProperties
    {
        public List<object>? traits;
        public List<object>? weaponTraits;
    }

    private sealed class CompProperties_GravshipFacility : CompProperties
    {
        public object? fuelSavingsPercent;
        public object? maxDistance;
        public List<object>? statOffsets;
    }

    private sealed class CompProperties_GravshipThruster : CompProperties
    {
        public object? componentTypeDef;
        public object? maxSimultaneous;
    }

    private sealed class StatOffsetDefinition
    {
        public object? stat;
        public object? value;
    }

    private sealed class ThrowingCompProperties : CompProperties
    {
        public object Broken => throw new InvalidOperationException("test");
    }

    private sealed class OutcomeDoerTest
    {
    }

    private static ThingDef NewThingDef(params CompProperties[] comps)
    {
        var definition = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        definition.comps = comps.ToList();
        return definition;
    }

    private static T NewDef<T>() where T : Def
    {
        return (T)FormatterServices.GetUninitializedObject(typeof(T));
    }
}
