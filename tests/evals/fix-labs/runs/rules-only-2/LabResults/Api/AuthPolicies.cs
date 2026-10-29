namespace LabResults.Api;

public static class AuthPolicies
{
    public const string Clinician = "clinician";

    /// <summary>The Keycloak user id; inbound claim mapping is off, so it keeps its JWT name.</summary>
    public const string SubjectClaim = "sub";
}
