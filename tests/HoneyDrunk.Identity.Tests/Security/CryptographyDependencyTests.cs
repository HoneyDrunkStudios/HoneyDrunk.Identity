using System.Reflection;
using System.Security.Cryptography.Pkcs;

namespace HoneyDrunk.Identity.Tests.Security;

/// <summary>Guards the PKCS implementation actually selected by the runtime.</summary>
public sealed class CryptographyDependencyTests
{
    /// <summary>Rejects shared-framework fallback to vulnerable .NET 10 builds.</summary>
    [Fact]
    public void LoadedPkcsAssembly_MeetsSecurityAdvisoryMinimum()
    {
        var assembly = typeof(SignedCms).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informationalVersion);
        Assert.True(
            Version.TryParse(informationalVersion.Split('+')[0], out var version),
            $"Unrecognized PKCS informational version: {informationalVersion}");

        // GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7 are fixed in 10.0.13.
        Assert.True(
            version >= new Version(10, 0, 13),
            $"Loaded vulnerable PKCS {informationalVersion} from {assembly.Location}");
    }
}
