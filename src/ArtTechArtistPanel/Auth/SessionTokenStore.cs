using Microsoft.JSInterop;
using ArtTechArtistPanel.Configuration;

namespace ArtTechArtistPanel.Auth;

public interface ISessionTokenStore
{
    bool IsAvailable { get; }
    Task<string?> ReadAsync();
    Task WriteAsync(string? refreshToken);
}

public sealed class SessionTokenStore(IJSRuntime js, ApiOptions options) : ISessionTokenStore
{
    private string Key => "arttech.refresh:" + options.BaseUri;
    public bool IsAvailable { get; private set; } = true;

    public async Task<string?> ReadAsync()
    {
        try { return await js.InvokeAsync<string?>("arttechSession.read", Key); }
        catch (JSException) { IsAvailable = false; return null; }
    }

    public async Task WriteAsync(string? refreshToken)
    {
        try { await js.InvokeVoidAsync("arttechSession.write", Key, refreshToken); }
        catch (JSException) { IsAvailable = false; }
    }
}
