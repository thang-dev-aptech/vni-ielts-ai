namespace Vni.Ielts.Application.Usage;

/// <summary>What one provider call consumed, as the adapter saw it.</summary>
public sealed record EvaluationUsage(string Provider, string Model, long TokensIn, long TokensOut);

/// <summary>
/// The channel through which an Infrastructure adapter tells the caller what
/// a provider call cost, without the port's return type carrying it.
///
/// <para>
/// <b>`[QUYẾT ĐỊNH kỹ thuật, 07/09/2026]` — an ambient scope, not a wider
/// port.</b> The Writing ledger row needs <c>userId</c>, <c>sessionId</c>,
/// <c>provider</c>, <c>model</c> and the token counts. The runner knows the
/// session; the router knows the tokens; nothing knows both. Three ways to
/// join them were weighed:
/// </para>
/// <list type="bullet">
/// <item>
/// Extend <c>IWritingEvaluationCostMetric.Record</c> with user and session.
/// Rejected: the metric is an Infrastructure interface with no business
/// knowing a learner, and every layer between the runner and the router
/// would have to carry the ids down for it — the request record is
/// deliberately "primitives and a rubric, no session, no learner".
/// </item>
/// <item>
/// Put the numbers on <c>ClaimedEvaluation</c>. Rejected for now: that
/// record is the model's <i>claim</i> and every test that builds one would
/// need to know about billing; it is also a shared contract another slice
/// is touching this week.
/// </item>
/// <item>
/// This: the runner opens a scope before it calls the evaluator, the
/// existing cost-metric hook — already called by the router on every
/// successful response — writes the numbers into it, and the runner reads
/// them back after the await and writes the ledger row itself, in the
/// layer that owns the ledger.
/// </item>
/// </list>
/// <para>
/// <b>Cost of being wrong.</b> If a future evaluator does not report, the
/// row is still written with null tokens — the action is never lost, only
/// its price. If the scope were ever opened on one async flow and reported
/// on another, the report would land in nobody's scope and be dropped; that
/// is a silent loss of a number, never a wrong number, and never a block.
/// </para>
/// <para>
/// <b>Why mutation of a box and not <c>AsyncLocal.Value</c> assignment.</b>
/// A value assigned by a callee does not flow back up to the caller; a
/// mutable object the caller placed there is shared with every callee on
/// the same flow. The scope therefore holds a box the adapter fills in.
/// </para>
/// </summary>
public static class EvaluationUsageReport
{
    private static readonly AsyncLocal<Box?> Current = new();

    /// <summary>Opens a scope. Dispose it after the evaluator call and read <see cref="Scope.Usage"/>.</summary>
    public static Scope Begin()
    {
        var box = new Box();
        Current.Value = box;
        return new Scope(box);
    }

    /// <summary>
    /// Called by the adapter that saw the provider's response. A no-op when
    /// no scope is open, so the router can call it unconditionally.
    /// </summary>
    public static void Record(string provider, string model, long tokensIn, long tokensOut)
    {
        if (Current.Value is { } box)
            box.Usage = new EvaluationUsage(provider, model, tokensIn, tokensOut);
    }

    internal sealed class Box
    {
        public EvaluationUsage? Usage;
    }

    public sealed class Scope : IDisposable
    {
        private readonly Box _box;

        internal Scope(Box box) => _box = box;

        /// <summary>Null when nothing reported — the row is written without a price.</summary>
        public EvaluationUsage? Usage => _box.Usage;

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, _box)) Current.Value = null;
        }
    }
}
