using System.Net;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Contracts;

namespace ArtTechArtistPanel.Auth;

public sealed record AccessLease(string Token, int Generation);

public sealed class AuthSession(IdentityApiClient identity, ISessionTokenStore storage, TimeProvider clock)
{
    // Serializes login, storage writes and refresh. Logout invalidates responses immediately,
    // before waiting for this gate, so an in-flight response cannot restore a logged-out session.
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? accessToken;
    private string? refreshToken;
    private DateTimeOffset expiresAt;
    private int generation;
    public bool IsInitialized { get; private set; }
    public bool IsAuthenticated => accessToken is not null;
    public bool StorageAvailable => storage.IsAvailable;
    public int Generation => generation;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (IsInitialized) return;
            var current = generation;
            refreshToken = await storage.ReadAsync();
            if (current != generation) return;
            if (refreshToken is not null)
            {
                try { await AcceptAsync(await identity.RefreshAsync(refreshToken), current); }
                catch (ApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                { await ClearUnderGateAsync(current); }
            }
            IsInitialized = true;
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }

    public async Task LoginAsync(Credentials credentials, CancellationToken cancellationToken = default)
    {
        var current = ++generation;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (current != generation) throw new OperationCanceledException();
            accessToken = refreshToken = null;
            await storage.WriteAsync(null);
            var tokens = await identity.LoginAsync(credentials, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await AcceptAsync(tokens, current);
            if (current != generation) throw new OperationCanceledException();
            IsInitialized = true;
        }
        finally { gate.Release(); }
    }

    public async Task<AccessLease?> GetAccessAsync(AccessLease? rejected = null)
    {
        await gate.WaitAsync();
        try
        {
            if (rejected is not null && rejected.Generation != generation) return null;
            if (accessToken is null) return null;
            // A different request may already have refreshed the rejected token.
            bool rejectedCurrent = rejected?.Token == accessToken;
            if (!rejectedCurrent && clock.GetUtcNow() < expiresAt - TimeSpan.FromSeconds(30))
                return new(accessToken, generation);
            var current = generation;
            try
            {
                if (refreshToken is null) { await ClearUnderGateAsync(current); return null; }
                await AcceptAsync(await identity.RefreshAsync(refreshToken), current);
                return current == generation && accessToken is not null ? new(accessToken, current) : null;
            }
            catch (ApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            { await ClearUnderGateAsync(current); return null; }
        }
        finally { gate.Release(); }
    }

    public async Task LogoutAsync(int? expectedGeneration = null)
    {
        if (expectedGeneration.HasValue && expectedGeneration != generation) return;
        var current = ++generation;
        accessToken = refreshToken = null;
        IsInitialized = true;
        Changed?.Invoke();
        await gate.WaitAsync();
        try { if (current == generation) await storage.WriteAsync(null); }
        finally { gate.Release(); }
    }

    private async Task AcceptAsync(TokenResponse tokens, int current)
    {
        if (current != generation) return;
        await storage.WriteAsync(tokens.RefreshToken);
        if (current != generation) return;
        accessToken = tokens.AccessToken;
        refreshToken = tokens.RefreshToken;
        expiresAt = clock.GetUtcNow().AddSeconds(tokens.ExpiresIn);
        Changed?.Invoke();
    }

    private async Task ClearUnderGateAsync(int current)
    {
        if (current != generation) return;
        ++generation;
        accessToken = refreshToken = null;
        await storage.WriteAsync(null);
        Changed?.Invoke();
    }
}
