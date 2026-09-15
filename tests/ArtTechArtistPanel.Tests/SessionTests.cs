using System.Net;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Auth;

namespace ArtTechArtistPanel.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task ConcurrentRejectedTokensShareOneRefresh()
    {
        int refreshes = 0;
        using var f = new SessionFixture(async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/refresh")
            {
                Interlocked.Increment(ref refreshes);
                await Task.Yield();
                return StubHandler.Tokens("renewed", "renewed-refresh");
            }
            return StubHandler.Tokens();
        });
        await f.Login();
        var rejected = (await f.Session.GetAccessAsync())!;
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => f.Session.GetAccessAsync(rejected)));
        Assert.Equal(1, refreshes);
        Assert.All(results, result => Assert.Equal("renewed", result!.Token));
    }

    [Fact]
    public async Task RefreshOutagePreservesSessionAndWriteIsNotSent()
    {
        using var f = new SessionFixture(r => r.RequestUri!.AbsolutePath == "/refresh"
            ? throw new HttpRequestException("Offline") : Task.FromResult(StubHandler.Tokens()));
        await f.Login();
        f.Clock.Now += TimeSpan.FromHours(1);
        using var http = f.Authenticated(_ => throw new InvalidOperationException("Expired token must not be sent"));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.PostAsync("api/artist/profile", new StringContent("{}")));
        Assert.True(f.Session.IsAuthenticated);
        Assert.Equal("refresh", f.Store.Value);
    }

    [Fact]
    public async Task RejectedRefreshEndsSessionBeforeWrite()
    {
        using var f = new SessionFixture(r => Task.FromResult(r.RequestUri!.AbsolutePath == "/refresh"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Tokens()));
        await f.Login();
        f.Clock.Now += TimeSpan.FromHours(1);
        using var http = f.Authenticated(_ => throw new InvalidOperationException("No write after rejected refresh"));
        using var response = await http.PostAsync("api/artist/profile", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(f.Session.IsAuthenticated);
        Assert.Null(f.Store.Value);
    }

    [Fact]
    public async Task LoginCreatesUiIdentityAndLogoutClearsStorage()
    {
        using var f = new SessionFixture();
        using var provider = new PanelAuthenticationStateProvider(f.Session);
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        await f.Login();
        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal("refresh", f.Store.Value);
        await f.Session.LogoutAsync();
        Assert.Null(f.Store.Value);
        Assert.Null(await f.Session.GetAccessAsync());
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task ExpiringAccessUsesOneRefreshForConcurrentRequests()
    {
        int refreshes = 0;
        using var f = new SessionFixture(async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/refresh")
            {
                Interlocked.Increment(ref refreshes);
                await Task.Yield();
                return StubHandler.Tokens("new-access", "new-refresh");
            }
            return StubHandler.Tokens();
        });
        await f.Login();
        f.Clock.Now += TimeSpan.FromHours(1);
        var leases = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => f.Session.GetAccessAsync()));
        Assert.Equal(1, refreshes);
        Assert.All(leases, x => Assert.Equal("new-access", x!.Token));
        Assert.Equal("new-refresh", f.Store.Value);
    }

    [Fact]
    public async Task ReloadRefreshesOnceAndInvalidRefreshEndsSession()
    {
        int calls = 0;
        using var f = new SessionFixture(r =>
        {
            Assert.Equal("/refresh", r.RequestUri!.AbsolutePath);
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        f.Store.Value = "expired";
        await Task.WhenAll(f.Session.InitializeAsync(), f.Session.InitializeAsync());
        Assert.Equal(1, calls);
        Assert.True(f.Session.IsInitialized);
        Assert.False(f.Session.IsAuthenticated);
        Assert.Null(f.Store.Value);
    }

    [Fact]
    public async Task ReloadNetworkFailureCanBeRetriedWithoutLosingRefresh()
    {
        int calls = 0;
        using var f = new SessionFixture(_ => ++calls == 1
            ? throw new HttpRequestException() : Task.FromResult(StubHandler.Tokens()));
        f.Store.Value = "stored-refresh";
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Session.InitializeAsync());
        Assert.False(f.Session.IsInitialized);
        Assert.Equal("stored-refresh", f.Store.Value);
        await f.Session.InitializeAsync();
        Assert.True(f.Session.IsAuthenticated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutDiscardsLateLoginOrRefresh(bool refreshing)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new SessionFixture(async r =>
        {
            if (!refreshing || r.RequestUri!.AbsolutePath == "/refresh")
            { started.SetResult(); await finish.Task; }
            return StubHandler.Tokens();
        });
        Task pending;
        if (refreshing)
        {
            await f.Login();
            f.Clock.Now += TimeSpan.FromHours(1);
            pending = f.Session.GetAccessAsync();
        }
        else pending = f.Login();
        await started.Task;
        var logout = f.Session.LogoutAsync();
        Assert.False(f.Session.IsAuthenticated);
        finish.SetResult();
        if (refreshing) await pending;
        else await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        await logout;
        Assert.Null(f.Store.Value);
        Assert.False(f.Session.IsAuthenticated);
    }

    [Fact]
    public async Task Authenticated401RefreshesAndReplaysJsonExactlyOnce()
    {
        int refreshes = 0, sends = 0;
        using var f = new SessionFixture(r =>
        {
            if (r.RequestUri!.AbsolutePath == "/refresh") refreshes++;
            return Task.FromResult(StubHandler.Tokens(refreshes == 0 ? "old" : "new"));
        });
        await f.Login();
        using var http = f.Authenticated(async r =>
        {
            Assert.Equal("{\"displayName\":\"Artist\"}", await r.Content!.ReadAsStringAsync());
            Assert.Equal(++sends == 1 ? "old" : "new", r.Headers.Authorization!.Parameter);
            return new HttpResponseMessage(sends == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });
        using var result = await http.PostAsync("api/artist/profile", new StringContent("{\"displayName\":\"Artist\"}"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, sends);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Repeated401EndsSessionWithoutLooping()
    {
        int sends = 0;
        using var f = new SessionFixture();
        await f.Login();
        using var http = f.Authenticated(_ => { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); });
        using var result = await http.GetAsync("api/artist/profile");
        Assert.Equal(2, sends);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.False(f.Session.IsAuthenticated);
        Assert.Null(f.Store.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ForbiddenAndServerErrorsAreNotRetried(HttpStatusCode status)
    {
        int sends = 0, identityCalls = 0;
        using var f = new SessionFixture(_ => { identityCalls++; return Task.FromResult(StubHandler.Tokens()); });
        await f.Login();
        using var http = f.Authenticated(_ => { sends++; return Task.FromResult(new HttpResponseMessage(status)); });
        using var result = await http.GetAsync("api/artist/profile");
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(1, sends);
        Assert.Equal(1, identityCalls);
        Assert.True(f.Session.IsAuthenticated);
    }

    [Fact]
    public async Task ExternalUrlCannotReceiveBearerToken()
    {
        using var f = new SessionFixture();
        await f.Login();
        using var http = f.Authenticated(_ => throw new InvalidOperationException("Should never be called"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("https://other.example.test/"));
    }

    [Fact]
    public async Task OldRequestCannotEndNewUsersSession()
    {
        using var f = new SessionFixture();
        await f.Login();
        var old = (await f.Session.GetAccessAsync())!;
        await f.Session.LogoutAsync();
        await f.Login();
        Assert.Null(await f.Session.GetAccessAsync(old));
        await f.Session.LogoutAsync(old.Generation);
        Assert.True(f.Session.IsAuthenticated);
    }
}
