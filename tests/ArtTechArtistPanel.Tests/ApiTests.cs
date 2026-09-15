using System.Net;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Configuration;

namespace ArtTechArtistPanel.Tests;

public sealed class ApiTests
{
    [Theory]
    [InlineData("")]
    [InlineData("file:///tmp/test")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test?token=secret")]
    public void RejectsMissingOrUnsafeApiConfiguration(string value) => Assert.NotNull(new ApiOptions(value).Error);

    [Fact]
    public void ApiBaseSupportsDeploymentPath() => Assert.Equal("https://example.test/backend/", new ApiOptions("https://example.test/backend").BaseUri!.AbsoluteUri);

    [Theory]
    [InlineData("")]
    [InlineData("<html>proxy error</html>")]
    [InlineData("null")]
    public async Task EmptyOrNonJsonErrorsHaveUsableFallback(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(body) };
        var ex = await Assert.ThrowsAsync<ApiException>(() => ApiProblem.EnsureSuccessAsync(response));
        Assert.Contains("unavailable", ex.Problem.Title);
    }

    [Fact]
    public async Task RegistrationDoesNotRequireTokenResponse()
    {
        using var http = StubHandler.Client(r =>
        {
            Assert.Equal("/register", r.RequestUri!.AbsolutePath);
            Assert.Null(r.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await new IdentityApiClient(http).RegisterAsync(new() { Email = "artist@example.test", Password = "test" });
    }
}
