using System.Security.Cryptography;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Freshly generated PEM private keys in the formats used by FCM service accounts and APNs .p8 keys.
/// </summary>
public static class TestKeys
{
    public static string CreateRsaPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    public static string CreateEllipticCurvePrivateKeyPem()
    {
        using var ellipticCurve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ellipticCurve.ExportPkcs8PrivateKeyPem();
    }
}
