using System.Net;
using System.Text.Json;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Auth;
using ArtTechArtistPanel.Configuration;
using ArtTechArtistPanel.Contracts;
using ArtTechArtistPanel.Pages;
using ArtTechArtistPanel.Sharing;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArtTechArtistPanel.Tests;

public sealed class ExhibitionTests : BunitContext
{
    public ExhibitionTests() => Services.AddSingleton(new ShareLinkService("https://gallery.example/app"));
    private static void Edit(IRenderedComponent<ExhibitionEditor> cut) { cut.FindAll("button").SingleOrDefault(x => x.TextContent == "Edit exhibition")?.Click(); }
    private static OwnExhibitionDto Exhibition(string status = "draft", string title = "My exhibition") =>
        new(Guid.NewGuid(), "abcd1234abcd1234", title, "Description", 2, status, DateTime.UtcNow);

    [Fact]
    public void DetailsContainArtworkActionsAndCancelRestoresSavedMetadata()
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int reads = 0;
        using var http = StubHandler.Client(r => { Assert.Equal(HttpMethod.Get, r.Method); reads++; return Task.FromResult(StubHandler.Json(stored)); });
        Setup(session, http); var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Assert.Empty(cut.FindAll("form")); Assert.Contains("No artworks yet", cut.Markup);
        Assert.Single(cut.FindAll($"a[href='exhibitions/{stored.Id}/artworks/new']"));
        Edit(cut); cut.Find("#exhibition-title").Change("Unsaved"); Button(cut, "Cancel").Click();
        Assert.Empty(cut.FindAll("form")); Assert.Equal(stored.Title, cut.Find(".exhibition-details h2").TextContent);
        Edit(cut); Assert.Equal(stored.Title, cut.Find("#exhibition-title").GetAttribute("value")); Assert.Equal(1, reads);
    }
    private static HttpResponseMessage Error(HttpStatusCode status) => StubHandler.Json(new ApiProblem
    { Title = $"Error {(int)status}", Errors = status == HttpStatusCode.BadRequest ? new() { ["title"] = ["Title rejected"] } : [] }, status);
    private void Setup(SessionFixture session, HttpClient http, bool? activeProfile = true)
    {
        Services.AddSingleton(session.Session);
        Services.AddSingleton(new ExhibitionApiClient(http));
        Services.AddSingleton(new ArtworkApiClient(StubHandler.Client(_ => Task.FromResult(StubHandler.Json(Array.Empty<OwnArtworkDto>())))));
        var profile = StubHandler.Client(_ => Task.FromResult(activeProfile is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : StubHandler.Json(new OwnArtistProfileDto(Guid.NewGuid(), "profile", "Artist", "", "", activeProfile.Value, DateTime.UtcNow))));
        Services.AddSingleton(new ArtistProfileApiClient(profile, profile));
    }
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<ExhibitionEditor> cut, string text) =>
        cut.FindAll("button").Single(x => x.TextContent == text);

    [Fact]
    public void EmptyAndPopulatedListsUseServerOrderAndShowActions()
    {
        using var session = new SessionFixture();
        OwnExhibitionDto[] rows = [];
        using var http = StubHandler.Client(r =>
        {
            Assert.Equal("/api/artist/exhibitions", r.RequestUri!.AbsolutePath);
            return Task.FromResult(StubHandler.Json(rows));
        });
        Setup(session, http);
        var cut = Render<Exhibitions>();
        Assert.Contains("No exhibitions yet", cut.Markup);
        Assert.Single(cut.FindAll("a[href='exhibitions/new']"));
        rows = [Exhibition("deactivated", "Third"), Exhibition("draft", "First"), Exhibition("published", "Second")];
        cut.FindAll("button").Single(x => x.TextContent == "Refresh list").Click();
        Assert.Equal(new[] { "Third", "First", "Second" }, cut.FindAll("h2 a").Select(x => x.TextContent));
        Assert.Equal(new[] { "Deactivated", "Draft", "Published" }, cut.FindAll(".exhibition-status").Select(x => x.TextContent));
        Assert.Contains("Republish", cut.Markup); Assert.Contains("Publish", cut.Markup); Assert.Contains("Deactivate", cut.Markup);
        Assert.Single(cut.FindAll(".exhibition-code"));
        Assert.DoesNotContain(rows[0].Id.ToString(), cut.Find("main, .exhibition-list").TextContent);
    }

    [Fact]
    public void CreateSendsOnlyMetadataAndNavigatesToServerId()
    {
        using var session = new SessionFixture();
        var created = Exhibition(); int writes = 0;
        using var http = StubHandler.Client(async r =>
        {
            writes++; Assert.Equal(HttpMethod.Post, r.Method);
            var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "description", "sortOrder", "title" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
            Assert.Equal("New draft", json.RootElement.GetProperty("title").GetString());
            Assert.Equal(4, json.RootElement.GetProperty("sortOrder").GetInt32());
            return StubHandler.Json(created, HttpStatusCode.Created);
        });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>();
        cut.Find("form").Submit(); Assert.Equal(0, writes);
        Edit(cut); cut.Find("#exhibition-title").Change("  New draft  ");
        cut.Find("#exhibition-description").Change("New description");
        cut.Find("#exhibition-order").Change("4");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith("exhibitions/" + created.Id, Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void EditAndLifecycleUseCanonicalResponsesAndConfirmation()
    {
        using var session = new SessionFixture();
        var stored = Exhibition(); int writes = 0;
        using var http = StubHandler.Client(async r =>
        {
            if (r.Method == HttpMethod.Get) return StubHandler.Json(stored);
            writes++;
            if (r.Method == HttpMethod.Put)
            {
                var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Assert.Equal("Edited", json.RootElement.GetProperty("title").GetString());
                Assert.Equal("Description", json.RootElement.GetProperty("description").GetString());
                stored = stored with { Title = "Canonical title" };
            }
            else
            {
                Assert.Equal("{}", await r.Content!.ReadAsStringAsync());
                stored = stored with { Status = r.RequestUri!.AbsolutePath.EndsWith("/publish") ? "published" : "deactivated" };
            }
            return StubHandler.Json(stored);
        });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Assert.Equal("Draft", cut.Find(".exhibition-status").TextContent);
        Edit(cut); cut.Find("#exhibition-title").Change("Edited");
        Assert.DoesNotContain(cut.FindAll("button"), x => x.TextContent == "Publish");
        cut.Find("form").Submit();
        Assert.Empty(cut.FindAll("form"));
        Assert.Equal("Canonical title", cut.Find(".exhibition-details h2").TextContent);
        Button(cut, "Publish").Click();
        Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Contains(stored.ExhibitionCode, cut.Find(".exhibition-code").TextContent);
        Button(cut, "Deactivate").Click(); Assert.Equal(2, writes);
        Button(cut, "Cancel").Click(); Assert.Equal(2, writes);
        Button(cut, "Deactivate").Click(); Button(cut, "Confirm deactivation").Click();
        Assert.Equal("Deactivated", cut.Find(".exhibition-status").TextContent);
        Button(cut, "Republish").Click();
        Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Equal(4, writes);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void FailedSavePreservesInputAndDisplaysProblem(HttpStatusCode status)
    {
        using var session = new SessionFixture(); var stored = Exhibition();
        using var http = StubHandler.Client(r => Task.FromResult(r.Method == HttpMethod.Get ? StubHandler.Json(stored) : Error(status)));
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Edit(cut); cut.Find("#exhibition-title").Change("Keep my edits");
        cut.Find("form").Submit();
        Assert.NotEmpty(cut.FindAll("[role=alert]"));
        Assert.Equal("Keep my edits", cut.Find("#exhibition-title").GetAttribute("value"));
        if (status == HttpStatusCode.BadRequest) Assert.Contains("Title rejected", cut.Markup);
        if (status == HttpStatusCode.NotFound) Assert.Contains("unavailable", cut.Markup);
    }

    [Fact]
    public void SaveConflictRefetchesStateWithoutDiscardingInput()
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int reads = 0;
        using var http = StubHandler.Client(r => Task.FromResult(r.Method == HttpMethod.Get
            ? StubHandler.Json(++reads == 1 ? stored : stored with { Status = "published", Title = "Other tab" }) : Error(HttpStatusCode.Conflict)));
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Edit(cut); cut.Find("#exhibition-title").Change("Unsaved input"); cut.Find("form").Submit();
        Assert.Equal(2, reads); Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Equal("Unsaved input", cut.Find("#exhibition-title").GetAttribute("value"));
        Assert.Contains("Error 409", cut.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransitionConflictRefetchesAuthoritativeState(bool list)
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int reads = 0;
        using var http = StubHandler.Client(r =>
        {
            if (r.Method == HttpMethod.Post) { stored = stored with { Status = "published" }; return Task.FromResult(Error(HttpStatusCode.Conflict)); }
            reads++; return Task.FromResult(list ? StubHandler.Json(new[] { stored }) : StubHandler.Json(stored));
        });
        Setup(session, http);
        IRenderedComponent<IComponent> cut = list ? Render<Exhibitions>() : Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        cut.FindAll("button").Single(x => x.TextContent == "Publish").Click();
        Assert.Equal(2, reads); Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Contains("Error 409", cut.Markup);
        Assert.Single(cut.FindAll("button"), x => x.TextContent == "Deactivate");
    }

    [Fact]
    public void NetworkFailureCanBeRetriedWithoutLosingFormOrSession()
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int writes = 0;
        using var http = StubHandler.Client(r => r.Method == HttpMethod.Get ? Task.FromResult(StubHandler.Json(stored))
            : ++writes == 1 ? throw new HttpRequestException("Offline") : Task.FromResult(StubHandler.Json(stored with { Title = "Keep" })));
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Edit(cut); cut.Find("#exhibition-title").Change("Keep"); cut.Find("form").Submit();
        Assert.Contains("Cannot reach", cut.Markup); Assert.Equal("Keep", cut.Find("#exhibition-title").GetAttribute("value"));
        cut.Find("form").Submit(); Assert.Contains("Exhibition saved.", cut.Markup); Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ListAndDetailReadErrorsAreRecoverable(HttpStatusCode status)
    {
        using var session = new SessionFixture(); bool fail = true; var stored = Exhibition();
        using var http = StubHandler.Client(r => Task.FromResult(fail ? Error(status) : r.RequestUri!.AbsolutePath.EndsWith(stored.Id.ToString()) ? StubHandler.Json(stored) : StubHandler.Json(new[] { stored })));
        Setup(session, http);
        var list = Render<Exhibitions>(); var detail = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Assert.NotEmpty(list.FindAll("[role=alert]")); Assert.Empty(detail.FindAll("form"));
        fail = false;
        list.Find("button").Click(); detail.Find("button").Click();
        Assert.Single(list.FindAll(".exhibition-card")); Assert.Empty(detail.FindAll("form")); Assert.Contains("Edit exhibition", detail.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void InactiveOrMissingProfileCannotCreate(bool? active)
    {
        using var session = new SessionFixture();
        using var http = StubHandler.Client(_ => Task.FromResult(StubHandler.Json(Array.Empty<OwnExhibitionDto>())));
        Setup(session, http, active);
        var list = Render<Exhibitions>(); var create = Render<ExhibitionEditor>();
        Assert.Empty(list.FindAll("a[href='exhibitions/new']"));
        if (active is null) Assert.Empty(create.FindAll("form"));
        else Assert.True(create.Find("fieldset").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateCreateOrTransitionOnlySendsOnce(bool transition)
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int writes = 0;
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = StubHandler.Client(r => { if (r.Method == HttpMethod.Get) return Task.FromResult(StubHandler.Json(stored)); writes++; return pending.Task; });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, transition ? stored.Id : null));
        if (!transition) { Edit(cut); cut.Find("#exhibition-title").Change("Draft"); }
        var first = transition ? Button(cut, "Publish").ClickAsync(new MouseEventArgs()) : cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Equal(1, writes));
        if (transition) await Button(cut, "Publish").ClickAsync(new MouseEventArgs()); else await cut.Find("form").SubmitAsync();
        Assert.Equal(1, writes); Assert.True(transition ? Button(cut, "Publish").HasAttribute("disabled") : cut.Find("fieldset").HasAttribute("disabled"));
        pending.SetResult(StubHandler.Json(stored with { Status = transition ? "published" : "draft" })); await first;
    }

    [Fact]
    public async Task LateLoadCannotReplaceAnotherRoute()
    {
        using var session = new SessionFixture(); var first = Exhibition(title: "Stale"); var second = Exhibition(title: "Current");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = StubHandler.Client(r => r.RequestUri!.AbsolutePath.EndsWith(first.Id.ToString()) ? pending.Task : Task.FromResult(StubHandler.Json(second)));
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, first.Id));
        Assert.Contains("Loading exhibition", cut.Markup);
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ExhibitionEditor.Id)] = second.Id })));
        pending.SetResult(StubHandler.Json(first));
        cut.WaitForAssertion(() => Assert.Equal("Current", cut.Find(".exhibition-details h2").TextContent));
    }

    [Fact]
    public async Task LogoutDiscardsLateExhibitionResponse()
    {
        using var session = new SessionFixture(); await session.Login();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = StubHandler.Client(_ => pending.Task); Setup(session, http);
        var cut = Render<Exhibitions>();
        await session.Session.LogoutAsync(); pending.SetResult(StubHandler.Json(new[] { Exhibition(title: "Private title") }));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading your exhibitions", cut.Markup));
        Assert.DoesNotContain("Private title", cut.Markup);
    }

    [Theory]
    [InlineData("exhibitions")]
    [InlineData("exhibitions/new")]
    [InlineData("exhibitions/11111111-1111-1111-1111-111111111111")]
    public async Task ProtectedRoutesRedirectAnonymousUsers(string route)
    {
        using var session = new SessionFixture(); await session.Session.InitializeAsync();
        Services.AddSingleton(new ApiOptions("https://api.example.test")); Services.AddSingleton(session.Session);
        Services.RemoveAll<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        Services.AddAuthorizationCore(); Services.AddCascadingAuthenticationState();
        Services.AddSingleton<AuthenticationStateProvider>(new PanelAuthenticationStateProvider(session.Session));
        Services.AddSingleton(session.Identity);
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.EndsWith("login", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void TransitionErrorsNeverOptimisticallyChangeState(HttpStatusCode status)
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int writes = 0;
        using var http = StubHandler.Client(r =>
        {
            if (r.Method == HttpMethod.Get) return Task.FromResult(StubHandler.Json(stored));
            writes++; return Task.FromResult(Error(status));
        });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Button(cut, "Publish").Click();
        Assert.Equal("Draft", cut.Find(".exhibition-status").TextContent);
        Assert.NotEmpty(cut.FindAll("[role=alert]")); Assert.Equal(1, writes);
        if (status == HttpStatusCode.ServiceUnavailable) Assert.True(Button(cut, "Publish").HasAttribute("disabled"));
        if (status == HttpStatusCode.NotFound) Assert.Contains("unavailable", cut.Markup);
    }

    [Fact]
    public void LostTransitionResponseRequiresReadBeforeRetryAndUsesServerResult()
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int reads = 0;
        using var http = StubHandler.Client(r =>
        {
            if (r.Method == HttpMethod.Get) { reads++; return Task.FromResult(StubHandler.Json(stored)); }
            stored = stored with { Status = "published", Title = "Server metadata" };
            throw new HttpRequestException("Response lost after commit");
        });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Button(cut, "Publish").Click();
        Assert.True(Button(cut, "Publish").HasAttribute("disabled"));
        Button(cut, "Refresh server state").Click();
        Assert.Equal(2, reads); Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Equal("Server metadata", cut.Find(".exhibition-details h2").TextContent);
    }

    [Fact]
    public void ConflictWithFailedReadKeepsInputAndCanRefreshLater()
    {
        using var session = new SessionFixture(); var stored = Exhibition(); int reads = 0;
        using var http = StubHandler.Client(r =>
        {
            if (r.Method != HttpMethod.Get) return Task.FromResult(Error(HttpStatusCode.Conflict));
            return ++reads == 2 ? throw new HttpRequestException() : Task.FromResult(StubHandler.Json(stored with { Status = reads > 2 ? "published" : "draft" }));
        });
        Setup(session, http);
        var cut = Render<ExhibitionEditor>(p => p.Add(x => x.Id, stored.Id));
        Edit(cut); cut.Find("#exhibition-title").Change("Keep unsaved"); cut.Find("form").Submit();
        Assert.Equal(2, cut.FindAll("[role=alert]").Count);
        Button(cut, "Refresh server state").Click();
        Assert.Equal("Published", cut.Find(".exhibition-status").TextContent);
        Assert.Equal("Keep unsaved", cut.Find("#exhibition-title").GetAttribute("value"));
    }

    [Fact]
    public async Task ExhibitionClientUsesExistingBearerRefreshAndExactTransitionBody()
    {
        int refreshes = 0, sends = 0;
        using var session = new SessionFixture(r =>
        {
            if (r.RequestUri!.AbsolutePath == "/refresh") refreshes++;
            return Task.FromResult(StubHandler.Tokens(refreshes == 0 ? "old" : "renewed"));
        });
        await session.Login(); var stored = Exhibition("published");
        using var http = session.Authenticated(async r =>
        {
            Assert.EndsWith($"api/artist/exhibitions/{stored.Id}/publish", r.RequestUri!.AbsoluteUri);
            Assert.Equal("{}", await r.Content!.ReadAsStringAsync());
            Assert.Equal(++sends == 1 ? "old" : "renewed", r.Headers.Authorization!.Parameter);
            return sends == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Json(stored);
        });
        Assert.Equal(stored, await new ExhibitionApiClient(http).TransitionAsync(stored.Id, "publish"));
        Assert.Equal(1, refreshes); Assert.Equal(2, sends); Assert.True(session.Session.IsAuthenticated);
    }
}
