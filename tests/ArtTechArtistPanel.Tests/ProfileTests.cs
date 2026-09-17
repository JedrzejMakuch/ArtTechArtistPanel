using System.Net;
using System.Text.Json;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Contracts;
using ArtTechArtistPanel.Pages;
using ArtTechArtistPanel.Sharing;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ArtTechArtistPanel.Tests;

public sealed class ProfileTests : BunitContext
{
    public ProfileTests() => Services.AddSingleton(new ShareLinkService("https://gallery.example/app"));
    [Fact]
    public void ExistingProfileOpensDetailsAndCancelDoesNotRefetchOrSave()
    {
        using var session = new SessionFixture(); int reads = 0;
        using var own = StubHandler.Client(r => { Assert.Equal(HttpMethod.Get, r.Method); reads++; return Task.FromResult(StubHandler.Json(Profile())); });
        using var publicHttp = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(new PublicArtistProfileDto("abc123", "Artist", "Biography"))));
        Services.AddSingleton(session.Session); Services.AddSingleton(new ArtistProfileApiClient(own, publicHttp));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        Assert.Empty(cut.FindAll("form")); Assert.Equal("Artist", cut.Find(".profile-details h2").TextContent);
        void Click(string text) => cut.FindAll("button").Single(x => x.TextContent == text).Click();
        Click("Edit profile"); cut.Find("#display-name").Change("Unsaved"); Click("Cancel");
        Assert.Empty(cut.FindAll("form")); Assert.Equal("Artist", cut.Find(".profile-details h2").TextContent);
        Click("Edit profile"); Assert.Equal("Artist", cut.Find("#display-name").GetAttribute("value")); Assert.Equal(1, reads);
    }

    [Fact]
    public async Task OldPublicReadCannotReplaceVerificationAfterSave()
    {
        using var f = new SessionFixture();
        int reads = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var own = StubHandler.Client(r => Task.FromResult(StubHandler.Json(Profile())));
        using var anonymous = StubHandler.Client(r =>
        {
            if (++reads == 2) { waiting.SetResult(); return oldRead.Task; }
            return Task.FromResult(StubHandler.Json(new PublicArtistProfileDto("abc123", reads == 1 ? "Initial" : "After save", "Bio")));
        });
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, anonymous));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        var refresh = cut.Find(".public-preview button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await waiting.Task;
        cut.FindAll("button").Single(x => x.TextContent == "Edit profile").Click();
        await cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Contains("After save", cut.Find(".public-preview").TextContent));
        oldRead.SetResult(StubHandler.Json(new PublicArtistProfileDto("abc123", "Stale response", "Bio")));
        await refresh;
        Assert.DoesNotContain("Stale response", cut.Markup);
        Assert.Contains("After save", cut.Markup);
    }

    private static OwnArtistProfileDto Profile(bool active = true) =>
        new(Guid.NewGuid(), "abc123", "Artist", "Biography", "", active, DateTime.UtcNow);

    [Fact]
    public void MissingProfileCreatesThenEditsAndLoadsAnonymousPublicResponse()
    {
        using var f = new SessionFixture();
        OwnArtistProfileDto? stored = null;
        int creates = 0, updates = 0, publicReads = 0;
        using var own = StubHandler.Client(async r =>
        {
            if (r.Method == HttpMethod.Get) return stored is null
                ? new(HttpStatusCode.NotFound) : StubHandler.Json(stored);
            var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "bio", "displayName" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
            if (r.Method == HttpMethod.Post) creates++; else updates++;
            stored = Profile() with { DisplayName = json.RootElement.GetProperty("displayName").GetString()!,
                Bio = json.RootElement.GetProperty("bio").GetString()! };
            return StubHandler.Json(stored);
        });
        using var anonymous = StubHandler.Client(r =>
        {
            publicReads++;
            Assert.Null(r.Headers.Authorization);
            Assert.EndsWith("/api/profiles/abc123", r.RequestUri!.AbsoluteUri);
            // Deliberately different: confirms the preview renders the fresh API response.
            return Task.FromResult(StubHandler.Json(new PublicArtistProfileDto("abc123", "Public response", stored!.Bio)));
        });
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, anonymous));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        cut.WaitForAssertion(() => Assert.Contains("Introduce yourself", cut.Markup));
        cut.Find("#display-name").Change("  Artist name  ");
        cut.Find("#bio").Change("First bio");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Public response", cut.Find(".public-preview").TextContent));
        Assert.Equal(1, creates);
        Assert.Equal("Artist name", stored!.DisplayName);
        Assert.Empty(cut.FindAll("form"));
        cut.FindAll("button").Single(x => x.TextContent == "Edit profile").Click();
        cut.Find("#bio").Change("Updated bio");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Updated bio", cut.Find(".public-preview").TextContent));
        Assert.Equal(1, updates);
        Assert.Equal(2, publicReads);
    }

    [Fact]
    public void InactiveProfileIsReadableAndDisabledWithoutPublicFetch()
    {
        using var f = new SessionFixture();
        using var own = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(Profile(false))));
        using var anonymous = StubHandler.Client(_ => throw new InvalidOperationException("No public read for inactive profile"));
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, anonymous));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        Assert.Empty(cut.FindAll("form")); Assert.DoesNotContain("Edit profile", cut.Markup);
        Assert.Contains("inactive", cut.Markup);
        Assert.Empty(cut.FindAll(".public-preview"));
    }

    [Fact]
    public void ValidationAndBackendFailurePreserveUserInput()
    {
        using var f = new SessionFixture();
        int writes = 0;
        using var own = StubHandler.Client(r =>
        {
            if (r.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            writes++;
            return Task.FromResult(StubHandler.Json(new ApiProblem { Title = "Invalid profile", Errors = new() { ["displayName"] = ["Name rejected"] } }, HttpStatusCode.BadRequest));
        });
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, own));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        cut.Find("form").Submit();
        Assert.Equal(0, writes);
        cut.Find("#display-name").Change("Keep my name");
        cut.Find("#bio").Change("Keep my biography");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Name rejected", cut.Find("[role=alert]").TextContent));
        Assert.Equal("Keep my name", cut.Find("#display-name").GetAttribute("value"));
        Assert.Equal("Keep my biography", cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputTextArea>().Instance.Value);
    }

    [Fact]
    public void ForbiddenGetDoesNotShowOnboarding()
    {
        using var f = new SessionFixture();
        using var own = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(new ApiProblem { Title = "Access denied" }, HttpStatusCode.Forbidden)));
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, own));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        Assert.Contains("Access denied", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void BiographyIsRenderedAsText()
    {
        using var f = new SessionFixture();
        using var own = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(Profile())));
        using var anonymous = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(new PublicArtistProfileDto("abc123", "Artist", "<script>alert(1)</script>"))));
        Services.AddSingleton(f.Session);
        Services.AddSingleton(new ArtistProfileApiClient(own, anonymous));
        var cut = Render<ArtTechArtistPanel.Pages.Profile>();
        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("<script>alert(1)</script>", cut.Find(".public-preview .biography").TextContent);
    }
}
