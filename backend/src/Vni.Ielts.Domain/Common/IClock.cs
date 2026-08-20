namespace Vni.Ielts.Domain.Common;

/// <summary>
/// The server clock, as a dependency.
///
/// This exists because of CLAUDE.md rule 1: the exam timer is
/// server-authoritative. <c>startedAt</c> and <c>deadlineAt</c> are written
/// from the server clock and never accepted from a client.
///
/// Making time injectable is what lets that rule be *tested* — a deadline
/// test that has to wait sixty real minutes is a test nobody runs. It is
/// declared in Domain rather than Application because domain invariants
/// themselves reason about time (<c>IsWithinDeadline</c>).
///
/// Always UTC. Exam deadlines cross timezones and the server is the authority.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real one. Infrastructure registers this.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
