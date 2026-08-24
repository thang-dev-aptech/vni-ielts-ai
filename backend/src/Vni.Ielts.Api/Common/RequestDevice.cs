using Microsoft.AspNetCore.Http;
using Vni.Ielts.Application.Identity;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Supplies the current request's User-Agent to the token service.
///
/// <para>
/// Lives in the Api because it is the only project that may know what an HTTP
/// request is — Infrastructure deliberately does not reference ASP.NET Core,
/// which is why this is a port rather than a direct read.
/// </para>
///
/// <para>
/// Returns null outside a request, which is the honest answer for the worker
/// and for tests. A session recorded with no device simply shows as unknown
/// rather than inventing one.
/// </para>
/// </summary>
public sealed class RequestDevice(IHttpContextAccessor accessor) : IRequestDevice
{
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is
    { Length: > 0 } value
        ? value
        : null;
}
