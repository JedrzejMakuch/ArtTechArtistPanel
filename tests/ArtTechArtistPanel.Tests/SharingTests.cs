using ArtTechArtistPanel.Components;
using ArtTechArtistPanel.Pages;
using ArtTechArtistPanel.Sharing;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ArtTechArtistPanel.Tests;

public sealed class SharingTests : BunitContext
{
    [Fact]
    public void CanonicalLinksAreStableEscapedAndSeparateFromAppUris()
    {
        var links = new ShareLinkService("https://gallery.example/art/");
        Assert.Equal("https://gallery.example/art/view/profile/artist-123", links.CreatePublicLink(PublicContentKind.Profile, "artist-123"));
        Assert.Equal("https://gallery.example/art/view/exhibition/show-456", links.CreatePublicLink(PublicContentKind.Exhibition, "show-456"));
        Assert.Equal("arttechgallery://exhibition/show-456", ShareLinkService.CreateAndroidLink(PublicContentKind.Exhibition, "show-456"));
        Assert.Throws<ArgumentException>(() => links.CreatePublicLink(PublicContentKind.Profile, "../private"));
        Assert.Throws<InvalidOperationException>(() => new ShareLinkService("").CreatePublicLink(PublicContentKind.Profile, "code"));
    }

    [Fact]
    public void PublishedShareRendersQrWithCanonicalLinkAndCopiesExactlyOnce()
    {
        Services.AddSingleton(new ShareLinkService("https://gallery.example"));
        var copy = JSInterop.SetupVoid("navigator.clipboard.writeText", "https://gallery.example/view/exhibition/show-456");
        copy.SetVoidResult();
        var cut = Render<SharePanel>(p => p
            .Add(x => x.Kind, PublicContentKind.Exhibition).Add(x => x.Code, "show-456").Add(x => x.Title, "Show"));
        Assert.Contains("https://gallery.example/view/exhibition/show-456", cut.Markup);
        Assert.Empty(cut.FindAll(".share-qr"));
        cut.FindAll("button").Single(x => x.TextContent == "Show QR").Click();
        Assert.Single(cut.FindAll(".share-qr svg"));
        Assert.StartsWith("data:image/svg+xml;base64,", cut.Find("a[download]").GetAttribute("href"));
        cut.FindAll("button").Single(x => x.TextContent == "Copy link").Click();
        cut.WaitForAssertion(() => Assert.Contains("Link copied", cut.Markup));
        copy.VerifyInvoke("navigator.clipboard.writeText", 1);
    }

    [Fact]
    public void PrivateStateAndMissingConfigurationExposeNoLinkOrQr()
    {
        Services.AddSingleton(new ShareLinkService(""));
        var disabled = Render<SharePanel>(p => p.Add(x => x.Kind, PublicContentKind.Exhibition)
            .Add(x => x.Code, "draft-code").Add(x => x.Enabled, false).Add(x => x.UnavailableMessage, "Publish first."));
        Assert.Contains("Publish first", disabled.Markup); Assert.Empty(disabled.FindAll("a"));
        var unconfigured = Render<SharePanel>(p => p.Add(x => x.Kind, PublicContentKind.Profile).Add(x => x.Code, "artist"));
        Assert.Contains("not configured", unconfigured.Markup); Assert.Empty(unconfigured.FindAll("a"));
    }

    [Theory]
    [InlineData("profile", "artist-1", "arttechgallery://profile/artist-1")]
    [InlineData("exhibition", "show-2", "arttechgallery://exhibition/show-2")]
    public void PublicLandingUsesSameCodeForAndroidAdapter(string kind, string code, string expected)
    {
        Services.AddSingleton(new ShareLinkService("https://gallery.example"));
        var cut = Render<PublicLink>(p => p.Add(x => x.Kind, kind).Add(x => x.Code, code));
        Assert.Equal(expected, cut.Find("a.button-link").GetAttribute("href"));
        Assert.Contains("No viewer account", cut.Markup);
    }

    [Fact]
    public void PublicLandingRejectsMalformedKindOrCode()
    {
        Services.AddSingleton(new ShareLinkService("https://gallery.example"));
        Assert.Contains("Invalid link", Render<PublicLink>(p => p.Add(x => x.Kind, "other").Add(x => x.Code, "code")).Markup);
        Assert.Contains("Invalid link", Render<PublicLink>(p => p.Add(x => x.Kind, "profile").Add(x => x.Code, "../code")).Markup);
    }
}
