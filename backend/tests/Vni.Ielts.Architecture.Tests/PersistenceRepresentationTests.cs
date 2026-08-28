using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Architecture.Tests;

/// <summary>
/// F3.1 — the representations that have to survive MongoDB → PostgreSQL.
///
/// <b><see cref="PersistenceBoundaryTests"/> already proves the layers do not
/// reference a driver. It proves nothing about the shapes crossing between
/// them</b>, and the shapes are where a migration actually goes wrong: an id
/// that is an <c>ObjectId</c>, a band stored as a binary <c>double</c>, an
/// enum stored as its ordinal. None of those break anything the day they are
/// written. Each one makes the migration progressively more expensive, and
/// the ordinal case corrupts data on a change nobody thinks is risky.
///
/// <b>These rules were established by observing the driver, not by reading
/// about it.</b> A probe serialised one document with a bare field of each
/// kind and printed the resulting BSON types:
///
/// <code>
///   decimal?  →  Decimal128    (exact; safe by default)
///   decimal   →  Decimal128    (exact; safe by default)
///   enum      →  Int32 = 1     ← the ordinal, not the name
///   DateTime  →  DateTime      (UTC)
/// </code>
///
/// So `decimal` needs no attribute to be correct — the explicit
/// <c>[BsonRepresentation(Decimal128)]</c> on the band fields is documentation
/// rather than load-bearing — and <b>a bare enum property is a live
/// hazard</b>: it persists <c>Advanced = 1</c>, and inserting a member above
/// it later silently reinterprets every stored document as a different value.
///
/// Every mapper in Infrastructure already converts enums with
/// <c>.ToString()</c> / <c>Enum.Parse</c> by hand, so no document declares a
/// bare enum today. That is a convention held by thirty hand-written mappers
/// and nothing else. This is what makes it a rule.
/// </summary>
public sealed class PersistenceRepresentationTests
{
    private static readonly Assembly Infrastructure =
        typeof(Vni.Ielts.Infrastructure.DependencyInjection).Assembly;

    /// <summary>
    /// Every persistence document type — the classes carrying <c>[BsonId]</c>
    /// or <c>[BsonElement]</c>, plus the nested types they compose.
    ///
    /// Found by attribute rather than by a <c>*Document</c> name convention:
    /// several nested shapes (a section, a question, a band boundary) carry
    /// <c>[BsonElement]</c> without the suffix, and a rule that silently skips
    /// the types it cannot name is not a rule.
    /// </summary>
    private static IReadOnlyList<Type> PersistenceTypes() =>
    [
        .. Infrastructure
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t =>
                t.GetCustomAttribute<BsonIgnoreExtraElementsAttribute>() is not null
                || t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p =>
                        p.GetCustomAttribute<BsonIdAttribute>() is not null
                        || p.GetCustomAttribute<BsonElementAttribute>() is not null))
            .OrderBy(t => t.FullName, StringComparer.Ordinal),
    ];

    private static IEnumerable<PropertyInfo> PropertiesOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>Unwraps <c>T?</c>, <c>List&lt;T&gt;</c> and <c>T[]</c> to the leaf type.</summary>
    private static Type Leaf(Type type)
    {
        var current = Nullable.GetUnderlyingType(type) ?? type;

        if (current.IsArray) return Leaf(current.GetElementType()!);

        if (current.IsGenericType)
        {
            var args = current.GetGenericArguments();
            // The value side of a map, the element of a list — both are where
            // a bad representation hides from a shallow check.
            if (args.Length is 1 or 2) return Leaf(args[^1]);
        }

        return current;
    }

    [Fact]
    public void There_are_persistence_types_to_check()
    {
        // Guards the rules below against silently passing on an empty set —
        // the failure mode where a refactor renames or relocates every
        // document and every rule reports success over nothing at all.
        Assert.True(
            PersistenceTypes().Count >= 20,
            $"Expected the persistence document types to be discoverable, found "
                + $"{PersistenceTypes().Count}. If the documents moved, fix the discovery "
                + "above rather than letting these rules pass over an empty set.");
    }

    [Fact]
    public void No_persistence_document_stores_an_enum()
    {
        var offenders = PersistenceTypes()
            .SelectMany(t => PropertiesOf(t).Select(p => (Type: t, Property: p)))
            .Where(x => Leaf(x.Property.PropertyType).IsEnum)
            .Select(x => $"{x.Type.Name}.{x.Property.Name} : {x.Property.PropertyType.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"""
            A persistence document declares an enum-typed property.

              {string.Join("\n  ", offenders)}

            The driver serialises a bare enum as its ORDINAL — verified, not
            assumed: a probe stored `Second` from a two-member enum and the
            document held `Int32 = 1`. Inserting or reordering a member above
            it then reinterprets every already-stored document as a different
            value, with no error at any point.

            Store the NAME instead, the way every existing mapper does:
              document: declare the property as `string`, defaulted to "Active"
              to document: `Status = entity.Status.ToString()`
              to domain: `Enum.TryParse<UserStatus>(doc.Status, out var s) ? s : Fallback`

            -> docs/decisions/0003-database-mongodb-first-postgresql-target.md
            """);
    }

    [Fact]
    public void No_persistence_document_stores_a_binary_floating_point_number()
    {
        var offenders = PersistenceTypes()
            .SelectMany(t => PropertiesOf(t).Select(p => (Type: t, Property: p)))
            .Where(x => Leaf(x.Property.PropertyType) == typeof(double)
                || Leaf(x.Property.PropertyType) == typeof(float))
            .Select(x => $"{x.Type.Name}.{x.Property.Name} : {x.Property.PropertyType.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"""
            A persistence document stores a binary floating-point number.

              {string.Join("\n  ", offenders)}

            IEEE-754 cannot hold 6.5, 0.1 or 0.6 exactly, and every number this
            product persists that could want a fraction is a band score or a
            task weighting — values compared for equality and summed into a
            reported result.

            Use `decimal`. It needs no attribute: the driver already maps it to
            Decimal128, which is exact.

            -> docs/domain/band-scoring.md
            """);
    }

    [Fact]
    public void No_persistence_document_stores_an_ObjectId()
    {
        var offenders = PersistenceTypes()
            .SelectMany(t => PropertiesOf(t).Select(p => (Type: t, Property: p)))
            .Where(x => Leaf(x.Property.PropertyType) == typeof(ObjectId))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"""
            A persistence document stores an ObjectId.

              {string.Join("\n  ", offenders)}

            `ObjectId` is a MongoDB type with no PostgreSQL equivalent, and the
            domain already generates its own identifiers — letting the driver
            assign a second one gives one entity two ids. Store the domain's
            own string id.

            -> backend/src/Vni.Ielts.Domain/Common/Ids.cs
            """);
    }

    [Fact]
    public void Every_document_id_is_a_string()
    {
        var offenders = PersistenceTypes()
            .SelectMany(t => PropertiesOf(t).Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.GetCustomAttribute<BsonIdAttribute>() is not null)
            .Where(x => x.Property.PropertyType != typeof(string))
            .Select(x => $"{x.Type.Name}.{x.Property.Name} : {x.Property.PropertyType.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"""
            A [BsonId] property is not a string.

              {string.Join("\n  ", offenders)}

            Every domain id is a string (`Guid.NewGuid().ToString("n")`, wrapped
            in a record struct) precisely so it survives a database change
            without the domain noticing.

            -> backend/src/Vni.Ielts.Domain/Common/Ids.cs
            """);
    }

    /// <summary>
    /// The domain side of the same contract.
    ///
    /// <b>A timestamp is the one representation both layers can get wrong
    /// independently.</b> The documents store bare <c>DateTime</c>, and every
    /// mapper re-attaches <c>DateTimeKind.Utc</c> by hand on the way back —
    /// because a `DateTime` read from BSON arrives `Unspecified`, and an
    /// `Unspecified` kind converted to a `DateTimeOffset` silently acquires
    /// the SERVER's local offset. On a machine at UTC+7 that moves every exam
    /// deadline by seven hours.
    ///
    /// The domain avoids the whole class of bug by never holding a `DateTime`
    /// at all. This is that rule, made checkable.
    /// </summary>
    [Fact]
    public void No_domain_type_exposes_a_bare_DateTime()
    {
        var offenders = DomainAssembly.Instance
            .GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Type: t, Property: p)))
            .Where(x => Leaf(x.Property.PropertyType) == typeof(DateTime))
            .Select(x => $"{x.Type.Name}.{x.Property.Name} : {x.Property.PropertyType.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"""
            A domain type exposes a bare DateTime.

              {string.Join("\n  ", offenders)}

            Use `DateTimeOffset`. A `DateTime` crossing the persistence
            boundary arrives with `Kind = Unspecified`, and converting that to
            a `DateTimeOffset` silently applies the server's local offset — on
            a machine at UTC+7 every exam deadline moves by seven hours, with
            no error anywhere.

            -> ADR-0007 (the exam timer is server-authoritative)
            """);
    }
}
