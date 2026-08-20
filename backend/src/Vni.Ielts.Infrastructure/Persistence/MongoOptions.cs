namespace Vni.Ielts.Infrastructure.Persistence;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    /// <summary>
    /// Must point at a <b>replica set</b>, not a standalone node. Multi-document
    /// transactions are unavailable otherwise, and token deduction plus session
    /// creation must be atomic — see ADR-0011 and threat T22.
    /// </summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?replicaSet=rs0";

    public string Database { get; set; } = "vni_ielts";
}
