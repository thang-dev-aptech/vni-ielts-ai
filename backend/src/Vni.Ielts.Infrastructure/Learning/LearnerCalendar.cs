using Microsoft.Extensions.Configuration;
using Vni.Ielts.Application.Learning;

namespace Vni.Ielts.Infrastructure.Learning;

/// <summary>
/// <c>Learning:TimeZone</c>, default <c>Asia/Ho_Chi_Minh</c>. A configured
/// seam rather than a per-learner column: every learner is in one zone today,
/// and the day a second zone matters this becomes a lookup instead of a
/// constant. → G-11
/// </summary>
public sealed class LearnerCalendar : ILearnerCalendar
{
    public const string DefaultTimeZone = "Asia/Ho_Chi_Minh";

    private readonly TimeZoneInfo _zone;

    public LearnerCalendar(IConfiguration configuration)
    {
        TimeZoneId = configuration["Learning:TimeZone"] is { Length: > 0 } configured
            ? configured
            : DefaultTimeZone;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    }

    public string TimeZoneId { get; }

    public DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);
}
