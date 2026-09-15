using System.Net;
using System.Net.Http.Headers;
using ArtTechArtistPanel.Auth;

namespace ArtTechArtistPanel.Api;

public sealed class BearerTokenHandler(AuthSession session, Uri backend) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !backend.IsBaseOf(request.RequestUri))
            throw new InvalidOperationException("Authenticated requests must target the configured backend.");
        var lease = await session.GetAccessAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (lease is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        // Buffer the small JSON body for the single auth retry. Never retry network/5xx failures.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.Token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        AccessLease? renewed;
        try { renewed = await session.GetAccessAsync(lease); }
        catch { response.Dispose(); throw; }
        if (renewed is null) return response;
        response.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed.Token);
        if (body is not null)
        {
            retry.Content = new ByteArrayContent(body);
            foreach (var header in request.Content!.Headers)
                retry.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        response = await base.SendAsync(retry, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) await session.LogoutAsync(renewed.Generation);
        return response;
    }
}
