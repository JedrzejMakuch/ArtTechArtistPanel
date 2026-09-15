using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ArtTechArtistPanel.Auth;

public sealed class PanelAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthSession session;
    public PanelAuthenticationStateProvider(AuthSession session)
    {
        this.session = session;
        session.Changed += OnChanged;
    }
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
        new AuthenticationState(new ClaimsPrincipal(session.IsAuthenticated
            ? new ClaimsIdentity([], "IdentityBearer") : new ClaimsIdentity())));
    private void OnChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    public void Dispose() => session.Changed -= OnChanged;
}
