using Vni.Ielts.Application.Media;

namespace Vni.Ielts.Application.Tests.Media;

public sealed class MediaCleanupErrorCodeTests
{
    [Fact]
    public void Normalize_maps_undefined_enum_and_unknown_wire_to_Failed()
    {
        Assert.Equal(
            MediaCleanupErrorCode.Failed,
            MediaCleanupErrorCodes.Normalize((MediaCleanupErrorCode)99));
        Assert.Equal(
            MediaCleanupErrorCodes.Failed,
            MediaCleanupErrorCodes.ToWire((MediaCleanupErrorCode)99));
        Assert.Equal(MediaCleanupErrorCodes.Failed, MediaCleanupErrorCodes.NormalizeWire("FREE_FORM"));
        Assert.Equal(MediaCleanupErrorCodes.Failed, MediaCleanupErrorCodes.NormalizeWire(null));
    }

    [Fact]
    public void ToWire_emits_only_allow_listed_codes()
    {
        Assert.Contains(MediaCleanupErrorCodes.ToWire(MediaCleanupErrorCode.Canceled), MediaCleanupErrorCodes.Allowed);
        Assert.Contains(MediaCleanupErrorCodes.ToWire(MediaCleanupErrorCode.Timeout), MediaCleanupErrorCodes.Allowed);
        Assert.Contains(MediaCleanupErrorCodes.ToWire(MediaCleanupErrorCode.Failed), MediaCleanupErrorCodes.Allowed);
        Assert.Equal(
            MediaCleanupErrorCodes.Timeout,
            MediaCleanupErrorCodes.ToWire(MediaCleanupErrorCode.Timeout));
    }

    [Fact]
    public void From_maps_timeout_and_canceled_without_exception_text()
    {
        Assert.Equal(MediaCleanupErrorCode.Canceled, MediaCleanupErrorCodes.From(new OperationCanceledException("secret")));
        Assert.Equal(MediaCleanupErrorCode.Timeout, MediaCleanupErrorCodes.From(new TimeoutException("secret")));
        Assert.Equal(MediaCleanupErrorCode.Failed, MediaCleanupErrorCodes.From(new InvalidOperationException("secret")));
    }
}
