using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Daynote.Infrastructure.Instance;

/// <summary>
/// Builds a named-pipe ACL that grants full control to the current user SID only. No other user,
/// including administrators, is granted access, so the activation channel stays private to this user
/// (DESIGN Section 1; plan Todo 10 current-user-only pipe).
/// </summary>
[SupportedOSPlatform("windows")]
public static class CurrentUserPipeSecurity
{
    public static PipeSecurity Create()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier owner = identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no user SID.");

        var security = new PipeSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            owner,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    /// <summary>The current user's SID value, used to scope per-user mutex and pipe names.</summary>
    public static string CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
    }
}
