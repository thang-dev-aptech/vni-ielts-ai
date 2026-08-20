namespace Vni.Ielts.Domain.Common;

/// <summary>
/// Anchors reflection over this assembly — architecture tests, and DI
/// scanning later. Exists so nothing has to hard-code an assembly name string.
/// </summary>
public static class DomainAssembly
{
    public static readonly System.Reflection.Assembly Instance =
        typeof(DomainAssembly).Assembly;
}
