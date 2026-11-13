namespace Sahar.Core.Update;

/// <summary>The public half of the key that signs update manifests (ECDSA P-256, SubjectPublicKeyInfo, base64).
/// The private half is only in the repo's Actions secret SAHAR_UPDATE_KEY, and only a publish from main signs.</summary>
public static class UpdateKeys
{
    public const string Production = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEE2Y0HOsfg/OG01vspHYkHpqL0iRIsUbZugiBEWDF/w4r7lMn8T53CXSXr6TRZIpiS4dAQS+HL6IGu2AuA5tBjg==";
}
