using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ArtTechArtistPanel;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Auth;
using ArtTechArtistPanel.Configuration;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var options = new ApiOptions(builder.Configuration["Api:BaseUrl"]);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ISessionTokenStore, SessionTokenStore>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider, PanelAuthenticationStateProvider>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = options.BaseUri, Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddScoped<IdentityApiClient>();
builder.Services.AddScoped(sp => new ArtistProfileApiClient(
    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<AuthSession>(), options.BaseUri!)
    { InnerHandler = new HttpClientHandler() }) { BaseAddress = options.BaseUri, Timeout = TimeSpan.FromSeconds(30) },
    sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped(sp => new ExhibitionApiClient(
    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<AuthSession>(), options.BaseUri!)
    { InnerHandler = new HttpClientHandler() }) { BaseAddress = options.BaseUri, Timeout = TimeSpan.FromSeconds(30) }));

builder.Services.AddScoped(sp => new ArtworkApiClient(
    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<AuthSession>(), options.BaseUri!)
    { InnerHandler = new HttpClientHandler() }) { BaseAddress = options.BaseUri, Timeout = TimeSpan.FromSeconds(30) }));

await builder.Build().RunAsync();
