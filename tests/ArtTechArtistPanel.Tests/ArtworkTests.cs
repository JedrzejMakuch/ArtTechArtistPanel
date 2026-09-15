using System.Net;
using System.Text.Json;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Contracts;
using ArtTechArtistPanel.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ArtTechArtistPanel.Tests;

public sealed class ArtworkTests : BunitContext
{
    private readonly Guid parent = Guid.NewGuid();
    private static OwnArtworkDto Row() => new() { Id = Guid.NewGuid(), Title = "Painting", Description = "Details", CreationYear = 2025,
        WidthCm = 80, HeightCm = 60, ImageUrl = "https://images.example.test/a.png", SortOrder = 5, IsActive = true };
    private void Setup(SessionFixture session, HttpClient http, bool active = true)
    {
        Services.AddSingleton(session.Session); Services.AddSingleton(new ArtworkApiClient(http));
        var profile = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(new OwnArtistProfileDto(Guid.NewGuid(), "code", "Artist", "", "", active, DateTime.UtcNow))));
        Services.AddSingleton(new ArtistProfileApiClient(profile, profile));
        Services.AddSingleton(new ExhibitionApiClient(StubHandler.Client(_ => Task.FromResult(StubHandler.Json(
            new OwnExhibitionDto(parent, "code", "Exhibition", "", 0, "published", DateTime.UtcNow))))));
    }
    private IRenderedComponent<ArtworkEditor> Editor(Guid? id = null) => Render<ArtworkEditor>(p => p.Add(x => x.ExhibitionId, parent).Add(x => x.Id, id));
    private static void Fill(IRenderedComponent<ArtworkEditor> cut)
    {
        cut.Find("#artwork-title").Change("New painting"); cut.Find("#artwork-year").Change("2025");
        cut.Find("#artwork-width").Change("80.25"); cut.Find("#artwork-height").Change("60.5");
        cut.Find("#artwork-image").Change("https://images.example.test/a.png"); cut.Find("#artwork-order").Change("9");
    }
    [Fact]
    public void EmptyAndPopulatedListsPreserveServerOrder()
    {
        using var session = new SessionFixture(); OwnArtworkDto[] rows = [];
        using var http = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(rows))); Setup(session, http);
        var cut = Render<Artworks>(p => p.Add(x => x.ExhibitionId, parent));
        Assert.Contains("No artworks yet", cut.Markup);
        rows = [Row(), new() { Id = Guid.NewGuid(), Title = "Second" }];
        cut.FindAll("button").Single().Click();
        Assert.Equal(new[] { "Painting", "Second" }, cut.FindAll("h2 a").Select(x => x.TextContent));
        Assert.Contains("Inactive artwork", cut.Markup);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SaveUsesOnlyEditableMetadataAndCanonicalResponse(bool edit)
    {
        using var session = new SessionFixture(); var row = Row(); int writes = 0;
        using var http = StubHandler.Client(async r =>
        {
            if (r.Method == HttpMethod.Get) return StubHandler.Json(row);
            writes++; Assert.Equal(edit ? HttpMethod.Put : HttpMethod.Post, r.Method);
            Assert.StartsWith($"/api/artist/exhibitions/{parent}/artworks", r.RequestUri!.AbsolutePath);
            var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "creationYear", "description", "heightCm", "imageUrl", "sortOrder", "title", "widthCm" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
            Assert.Equal(80.25m, json.RootElement.GetProperty("widthCm").GetDecimal());
            return StubHandler.Json(row);
        }); Setup(session, http); var cut = Editor(edit ? row.Id : null); Fill(cut); cut.Find("form").Submit();
        Assert.Equal(1, writes);
        if (edit) Assert.Equal("Painting", cut.Find("#artwork-title").GetAttribute("value"));
        Assert.EndsWith($"exhibitions/{parent}", Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(500)] [InlineData(0)]
    public void FailedSaveKeepsInputAndReportsError(int status)
    {
        using var session = new SessionFixture(); var row = Row();
        using var http = StubHandler.Client(r => r.Method == HttpMethod.Get ? Task.FromResult(StubHandler.Json(row)) :
            status == 0 ? Task.FromException<HttpResponseMessage>(new HttpRequestException()) : Task.FromResult(StubHandler.Json(new ApiProblem
            { Title = "Rejected", Errors = new() { ["title"] = ["Title rejected"] } }, (HttpStatusCode)status)));
        Setup(session, http); var cut = Editor(row.Id); Fill(cut); cut.Find("form").Submit();
        Assert.NotEmpty(cut.FindAll("[role=alert]")); Assert.Equal("New painting", cut.Find("#artwork-title").GetAttribute("value"));
        Assert.Equal(status == 404, cut.Find("fieldset").HasAttribute("disabled"));
    }
    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(500)] [InlineData(0)]
    public void ListFailureCanBeRetried(int status)
    {
        using var session = new SessionFixture(); bool fail = true;
        using var http = StubHandler.Client(_ => !fail ? Task.FromResult(StubHandler.Json(Array.Empty<OwnArtworkDto>())) : status == 0
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException()) : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        Setup(session, http); var cut = Render<Artworks>(p => p.Add(x => x.ExhibitionId, parent));
        Assert.NotEmpty(cut.FindAll("[role=alert]")); fail = false; cut.Find("button").Click(); Assert.Contains("No artworks yet", cut.Markup);
    }
    [Fact]
    public void DeleteRequiresConfirmationAndSupportsCancel()
    {
        using var session = new SessionFixture(); var row = Row(); int deletes = 0;
        using var http = StubHandler.Client(r =>
        {
            if (r.Method == HttpMethod.Get) return Task.FromResult(StubHandler.Json(row));
            Assert.Equal(HttpMethod.Delete, r.Method); deletes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }); Setup(session, http); var cut = Editor(row.Id);
        void Click(string name) => cut.FindAll("button").Single(x => x.TextContent == name).Click();
        Click("Delete artwork"); Assert.Equal(0, deletes); Click("Cancel"); Assert.Equal(0, deletes);
        Click("Delete artwork"); Click("Confirm deletion"); Assert.Equal(1, deletes);
        Assert.EndsWith($"exhibitions/{parent}", Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Fact]
    public void InvalidDimensionsAndUrlsDoNotWrite()
    {
        using var session = new SessionFixture(); int calls = 0;
        using var http = StubHandler.Client(_ => { calls++; return Task.FromResult(StubHandler.Json(Row())); }); Setup(session, http);
        var cut = Editor(); Fill(cut);
        foreach (var value in new[] { "0", "-1", "1001", "1.234", "not a number" })
        { cut.Find("#artwork-width").Change(value); cut.Find("form").Submit(); Assert.Equal(0, calls); }
        cut.Find("#artwork-width").Change("80"); cut.Find("#artwork-image").Change("javascript:alert(1)"); cut.Find("form").Submit(); Assert.Equal(0, calls);
    }
    [Fact]
    public void InactiveProfileIsReadOnly()
    {
        using var session = new SessionFixture(); using var http = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(Row())));
        Setup(session, http, false); var cut = Editor(Guid.NewGuid()); Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.DoesNotContain("Delete artwork", cut.Markup);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PendingWriteCannotBeSubmittedTwice(bool delete)
    {
        using var session = new SessionFixture(); var row = Row(); int writes = 0;
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var http = StubHandler.Client(r => { if (r.Method == HttpMethod.Get) return Task.FromResult(StubHandler.Json(row)); writes++; return pending.Task; });
        Setup(session, http); var cut = Editor(row.Id);
        if (delete) cut.FindAll("button").Single(x => x.TextContent == "Delete artwork").Click();
        var first = delete ? cut.FindAll("button").Single(x => x.TextContent == "Confirm deletion").ClickAsync(new()) : cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Equal(1, writes));
        if (delete) await cut.FindAll("button").Single(x => x.TextContent == "Confirm deletion").ClickAsync(new()); else await cut.Find("form").SubmitAsync();
        Assert.Equal(1, writes); pending.SetResult(delete ? new(HttpStatusCode.NoContent) : StubHandler.Json(row)); await first;
    }
    [Fact]
    public void RoutesRequireAuthorization()
    {
        Assert.NotEmpty(typeof(Artworks).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(typeof(ArtworkEditor).GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(500)] [InlineData(0)]
    public void FailedDeleteShowsErrorWithoutNavigating(int status)
    {
        using var session = new SessionFixture(); var row = Row();
        using var http = StubHandler.Client(r => r.Method == HttpMethod.Get ? Task.FromResult(StubHandler.Json(row)) : status == 0
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException()) : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        Setup(session, http); var cut = Editor(row.Id); var navigation = Services.GetRequiredService<NavigationManager>(); var before = navigation.Uri;
        cut.FindAll("button").Single(x => x.TextContent == "Delete artwork").Click();
        cut.FindAll("button").Single(x => x.TextContent == "Confirm deletion").Click();
        Assert.NotEmpty(cut.FindAll("[role=alert]")); Assert.Equal(before, navigation.Uri);
        Assert.Equal("Painting", cut.Find("#artwork-title").GetAttribute("value"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LateSaveAfterLogoutOrNavigationIsIgnored(bool logout)
    {
        using var session = new SessionFixture(); await session.Login(); var row = Row();
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var http = StubHandler.Client(r => r.Method == HttpMethod.Get ? Task.FromResult(StubHandler.Json(row)) : pending.Task);
        Setup(session, http); var cut = Editor(row.Id); Fill(cut); var save = cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("fieldset").HasAttribute("disabled")));
        if (logout) await session.Session.LogoutAsync();
        else cut.Render(p => p.Add(x => x.ExhibitionId, Guid.NewGuid()).Add(x => x.Id, row.Id));
        pending.SetResult(StubHandler.Json(new OwnArtworkDto { Id = row.Id, Title = "Stale response" })); await save;
        Assert.DoesNotContain("Stale response", cut.Markup); Assert.DoesNotContain("Artwork saved.", cut.Markup);
    }

    [Fact]
    public async Task ArtworkClientReusesSingleRefreshRetry()
    {
        using var session = new SessionFixture(); await session.Login(); int calls = 0;
        using var http = session.Authenticated(r =>
        {
            Assert.NotNull(r.Headers.Authorization); calls++;
            return Task.FromResult(calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : new(HttpStatusCode.NoContent));
        });
        await new ArtworkApiClient(http).DeleteAsync(parent, Guid.NewGuid()); Assert.Equal(2, calls);
    }
}
