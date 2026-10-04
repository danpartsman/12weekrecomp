using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace WebRecomp.Web.Services;

public sealed class CanonicalRoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        AddCanonicalRole(identity, principal, "ADMINISTRATOR", "Administrator");
        AddCanonicalRole(identity, principal, "COACH", "Coach");

        return Task.FromResult(principal);
    }

    private static void AddCanonicalRole(
        ClaimsIdentity identity,
        ClaimsPrincipal principal,
        string storedRole,
        string canonicalRole)
    {
        if (principal.IsInRole(canonicalRole))
        {
            return;
        }

        var hasStoredRole = principal.Claims.Any(
            claim =>
                claim.Type == ClaimTypes.Role
                && string.Equals(
                    claim.Value,
                    storedRole,
                    StringComparison.OrdinalIgnoreCase));

        if (hasStoredRole)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, canonicalRole));
        }
    }
}
