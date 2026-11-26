using Errandesk.Contracts;

namespace Errandesk.Client.Sync.Tests;

public sealed class ClientUpdateLayoutTests
{
    [Theory]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("1.2.0", "1.2.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData(" 1.2.3\r\n", "1.2.3.0")]
    [InlineData("1.2.3-beta.1", "1.2.3.0")]
    [InlineData("1.2.3+abcdef", "1.2.3.0")]
    public void ParseVersion_FillsMissingComponentsAndIgnoresSuffixes(string text, string expected) => Assert.Equal(Version.Parse(expected), ClientUpdateLayout.ParseVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("latest")]
    [InlineData("<!DOCTYPE html><html></html>")]
    public void ParseVersion_WithoutVersion_ReturnsNull(string? text) => Assert.Null(ClientUpdateLayout.ParseVersion(text));

    [Fact]
    public void ParseVersion_ComparesVersionsWithDifferentComponentCountsAsEqual() => Assert.Equal(ClientUpdateLayout.ParseVersion("0.1.0"), ClientUpdateLayout.ParseVersion("0.1.0.0"));

    [Theory]
    [InlineData("https://updates.example.com/errandesk", "https://updates.example.com/errandesk/")]
    [InlineData("https://updates.example.com/errandesk/", "https://updates.example.com/errandesk/")]
    [InlineData(" http://192.168.0.10:8081 ", "http://192.168.0.10:8081/")]
    public void NormalizeBaseUrl_EndsWithSlash(string value, string expected) => Assert.Equal(expected, ClientUpdateLayout.NormalizeBaseUrl(value)?.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("updates.example.com")]
    [InlineData("/updates/")]
    [InlineData("ftp://updates.example.com/")]
    [InlineData("https://updates.example.com/?channel=beta")]
    [InlineData("https://updates.example.com/?")]
    [InlineData("https://updates.example.com/#top")]
    public void NormalizeBaseUrl_WithUnusableValue_ReturnsNull(string? value) => Assert.Null(ClientUpdateLayout.NormalizeBaseUrl(value));

    [Theory]
    [InlineData(ClientUpdateLayout.WindowsFolderName, ClientUpdateLayout.VersionFileName, "https://updates.example.com/errandesk/windows/version")]
    [InlineData(ClientUpdateLayout.WindowsFolderName, ClientUpdateLayout.WindowsX64InstallerFileName, "https://updates.example.com/errandesk/windows/Installer-x64.exe")]
    [InlineData(ClientUpdateLayout.WindowsFolderName, ClientUpdateLayout.WindowsArm64InstallerFileName, "https://updates.example.com/errandesk/windows/Installer-arm64.exe")]
    [InlineData(ClientUpdateLayout.AndroidFolderName, ClientUpdateLayout.AndroidPackageFileName, "https://updates.example.com/errandesk/android/Errandesk-latest.apk")]
    [InlineData(ClientUpdateLayout.WindowsFolderName, "Errandesk Setup.exe", "https://updates.example.com/errandesk/windows/Errandesk%20Setup.exe")]
    public void GetFileUri_ResolvesBelowTheBaseUrl(string platformFolderName, string fileName, string expected)
    {
        var baseUrl = ClientUpdateLayout.NormalizeBaseUrl("https://updates.example.com/errandesk");

        Assert.NotNull(baseUrl);
        Assert.Equal(expected, ClientUpdateLayout.GetFileUri(baseUrl, platformFolderName, fileName).AbsoluteUri);
    }
}
