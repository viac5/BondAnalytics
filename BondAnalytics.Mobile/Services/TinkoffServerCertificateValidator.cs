using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BondAnalytics.Mobile.Services;

internal static class TinkoffServerCertificateValidator
{
    private const string ApiHost = "invest-public-api.tinkoff.ru";
    private const string TrustedRootThumbprint = "8FF915CCAB7BC16F8C5C8099D53E0E115B3AEC2F";
    private const string TrustedIntermediateThumbprint = "6741AB02CF6598C09652DC34D2DC095904E32B52";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private static readonly X509Certificate2 TrustedRoot = LoadTrustedRoot();
    private static readonly X509Certificate2 TrustedIntermediate = LoadTrustedIntermediate();

    public static bool Validate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? systemChain,
        SslPolicyErrors policyErrors)
    {
        if (!string.Equals(request.RequestUri?.IdnHost, ApiHost, StringComparison.OrdinalIgnoreCase) ||
            certificate is null ||
            (policyErrors & (SslPolicyErrors.RemoteCertificateNameMismatch |
                             SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            Android.Util.Log.Warn("TinkoffTLS",
                $"Rejected certificate: host={request.RequestUri?.IdnHost}, subject={certificate?.Subject}, errors={policyErrors}.");
            return false;
        }

        if (policyErrors == SslPolicyErrors.None)
            return true;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(TrustedRoot);
        chain.ChainPolicy.ExtraStore.Add(TrustedIntermediate);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        if (systemChain is not null)
        {
            foreach (var element in systemChain.ChainElements)
            {
                if (!string.Equals(element.Certificate.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(element.Certificate.Thumbprint, TrustedRoot.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(element.Certificate.Thumbprint, TrustedIntermediate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }
        }

        var isValid = chain.Build(certificate);
        if (!isValid)
        {
            var statuses = string.Join(", ", chain.ChainStatus.Select(status =>
                $"{status.Status}: {status.StatusInformation.Trim()}"));
            Android.Util.Log.Warn("TinkoffTLS",
                $"Custom certificate chain rejected: subject={certificate.Subject}, errors={policyErrors}, chain={statuses}.");
        }

        return isValid;
    }

    private static X509Certificate2 LoadTrustedRoot()
    {
        var context = Android.App.Application.Context
            ?? throw new InvalidOperationException("Android application context is unavailable.");
        using var stream = context.Resources?.OpenRawResource(Resource.Raw.russian_trusted_root_ca)
            ?? throw new InvalidOperationException("The T-Invest trust anchor resource is unavailable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var certificate = X509CertificateLoader.LoadCertificate(buffer.ToArray());
        if (!string.Equals(certificate.Thumbprint, TrustedRootThumbprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(certificate.Subject, certificate.Issuer, StringComparison.Ordinal))
        {
            certificate.Dispose();
            throw new CryptographicException("The bundled T-Invest trust anchor is invalid.");
        }

        return certificate;
    }

    private static X509Certificate2 LoadTrustedIntermediate()
    {
        var context = Android.App.Application.Context
            ?? throw new InvalidOperationException("Android application context is unavailable.");
        using var stream = context.Resources?.OpenRawResource(Resource.Raw.russian_trusted_sub_ca)
            ?? throw new InvalidOperationException("The T-Invest intermediate certificate resource is unavailable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var certificate = X509CertificateLoader.LoadCertificate(buffer.ToArray());
        var isCertificateAuthority = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .Any(extension => extension.CertificateAuthority);
        if (!string.Equals(certificate.Thumbprint, TrustedIntermediateThumbprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(certificate.Issuer, TrustedRoot.Subject, StringComparison.Ordinal) ||
            !isCertificateAuthority)
        {
            certificate.Dispose();
            throw new CryptographicException("The bundled T-Invest intermediate certificate is invalid.");
        }

        return certificate;
    }
}
