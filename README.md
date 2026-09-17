# ArtTech artist panel

Standalone Blazor WebAssembly targeting .NET 10. This is an independent Git repository.
The browser calls the existing ArtTechBackend HTTP API. There is no panel server,
Identity store, EF Core dependency, direct PostgreSQL access, or SignalR circuit.

Implemented flows include registration/login, token refresh, protected `/profile`,
profile onboarding/editing, fresh anonymous public-profile verification, and owned
exhibition listing, creation, metadata editing and publication management.

## Local setup

Requirements: .NET 10 SDK, the existing backend with its local PostgreSQL setup,
and a browser. Node is needed only for the optional Playwright smoke test.

1. In `src/ArtTechArtistPanel/wwwroot`, copy `appsettings.Development.example.json`
   to `appsettings.Development.json`. Set `Api:BaseUrl` to your backend's reachable
   HTTP(S) base address. Set `ShareLinks:PublicBaseUrl` to the panel's externally
   reachable origin/base path; canonical links are built beneath it.
   Create this file **before building**. It is ignored by Git and excluded from publish.
2. In the backend's ignored development configuration, add
   `Cors:AllowedOrigins` as an array containing the panel's exact origin
   (scheme, host, port; no path or trailing slash). Alternatively use
   `Cors__AllowedOrigins__0` in the backend process environment.
   Preserve the existing database connection and demo settings.
3. Run the backend using its documented local setup. Use HTTPS for real credentials.
   Trust the local development certificate with `dotnet dev-certs https --trust`
   when using local HTTPS. An HTTPS panel must use an HTTPS API.
4. From this repository, run:

```powershell
dotnet restore ArtTechArtistPanel.slnx
dotnet build ArtTechArtistPanel.slnx --no-restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$panelListenUrl = Read-Host 'Panel listening URL (matching the configured CORS origin)'
dotnet run --project src/ArtTechArtistPanel --no-launch-profile --no-build --urls $panelListenUrl
```

Open the panel at that URL. Register a disposable test account or log in. A new
account sees onboarding. Saving creates its public profile immediately.

Missing/invalid `Api:BaseUrl` produces a configuration message rather than requests
to an unintended backend. If you add configuration files or change package/static
assets while a `--no-build` dev server is running, rebuild and restart that server.

No machine-specific URLs are stored in shared configuration. Browser configuration
is public: never put passwords, connection strings, API secrets, or tokens in it.

## Project map

- `src/ArtTechArtistPanel/Program.cs`: startup, configuration and service registration.
- `Configuration/`: validates the backend base address.
- `Contracts/`: small HTTP DTOs; no cross-repository project references.
- `Api/`: Identity/profile/exhibition clients, bearer handler and ProblemDetails handling.
- `Auth/`: session lifetime, serialized refresh, session storage and UI auth state.
- `Pages/`: registration, login, profile, exhibitions, home redirect and not-found page.
- `Layout/` and `Components/`: panel shell, error display and protected-route redirect.
- `tests/ArtTechArtistPanel.Tests/`: xUnit and bUnit tests.
- `tests/browser/`: optional Playwright smoke test against a real API.

## Session behavior

- Register sends `{ email, password }` to `/register`, then separately logs in.
  If registration succeeds but login fails, the page confirms account creation and
  links to login instead of offering another registration request.
- Login calls `/login?useCookies=false`. Identity's tokens are opaque, not JWTs.
- Access tokens exist only in memory. Expiry comes from `expiresIn`; requests
  refresh when within 30 seconds of expiry. There is no background refresh timer.
- Only the refresh token is stored in `sessionStorage`, with a key scoped to the
  configured backend. Reload restores the session through `/refresh` before routing.
  Storage failure falls back to a memory-only session with a visible message.
- A semaphore serializes refresh/login/storage changes. Concurrent requests share
  a refreshed token. The authenticated HTTP handler retries a `401` only once,
  with a newly buffered JSON request. It refuses requests outside the API base URL.
- Refresh rejection or a repeated `401` clears the session. Network/5xx failures
  are retryable errors and do not erase the refresh token. Failed writes are never
  automatically replayed for network errors because the server may have saved them.
- `AuthenticationStateProvider` supplies a minimal authenticated UI identity.
  `/profile` and all exhibition routes use `[Authorize]` and `AuthorizeRouteView`.
  Login keeps its existing `/profile` destination; external return URLs are never used.
- Logout clears tokens and removes protected UI. Generation checks discard late
  responses from a previous session. Profile/exhibition requests are cancelled on navigation.
- `403` is an access denial, not an expired token. There is no refresh/login loop.
  The backend's `ActiveUser` policy remains the authority. An inactive profile is
  readable by its active owner with a disabled form and no public preview.

## Profile behavior

`GET /api/artist/profile`: `404` means onboarding; `200` populates the edit form.
POST and PUT send **only** `displayName` and `bio`. Names are trimmed (1–200
characters); biography is optional (up to 2,000). Backend validation remains final.
IDs, owner IDs, profile codes, activity and image URLs are never sent in save requests.

Saving displays the canonical response, then fetches `/api/profiles/{profileCode}`
through the anonymous client with browser credentials omitted and cache mode `no-store`.
The public preview renders that fresh response, not the form data. Biography is text.
Save success and public verification failure are separate states, so a failed read
does not imply the save failed. A manual refresh retries the public check.

ValidationProblemDetails errors and general ProblemDetails appear in the form area.
Empty/non-JSON errors have fallback messages. Recoverable save failures preserve
input. On a create `409`, the panel checks whether a profile now exists and switches
to editing while retaining input; it never silently overwrites that profile.

## Exhibition behavior

Protected routes are `/exhibitions`, `/exhibitions/new`, and `/exhibitions/{id}`.
The authenticated navigation includes Exhibitions. An artist without a profile
receives a link to profile onboarding. Inactive profiles can read exhibitions but
cannot create, edit or change publication.

The list preserves backend ordering, shows title/description/display order/creation
date/status, and provides detail links and publication actions. A new exhibition
is created through the real POST API and opens at the ID returned by the server.
Only `title`, `description`, and `sortOrder` are sent on create/update. Title is
trimmed and required (1–200 characters); description is optional (up to 2000);
display order is an integer from 0 through 2147483647. IDs, codes, owner fields,
timestamps, and lifecycle state are never included in save requests.

Draft shows Publish, Published shows Deactivate, and Deactivated shows Republish.
Deactivation has an inline confirm/cancel step. Transition POSTs send `{}` and use
the returned status and metadata; the backend remains the lifecycle authority.
Unknown states have no publication actions. Published exhibitions show their stable
ExhibitionCode and sharing controls. Draft and Deactivated exhibitions show why no
public link is available.

Forms/actions disable during writes and handlers reject duplicate submissions.
Unsaved metadata must be saved before publication actions; published metadata edits
are immediately public. Failed saves preserve input. 409 triggers a fresh read:
status/actions update while modified form input stays intact. If this read fails,
an explicit server-state refresh is offered. Lost transition responses also require
a fresh read before another transition, because the server may have completed it.
404 makes an existing editor unavailable without clearing its input. 403 displays
the backend error without attempting token refresh. Network/5xx failures use the
existing recoverable error messages and do not end the session.

The existing bearer handler, session storage, refresh serialization, single 401
retry, ProblemAlert, authentication provider, and generation checks are reused.
Changing detail routes cancels old requests and prevents late results replacing
the current form. Styling follows the existing panel, with wrapping cards/actions
and text status badges. This is functional MVP UI, not the final visual redesign.

## Public sharing

The panel constructs canonical links from configuration and server-returned public
codes only:

```text
{ShareLinks:PublicBaseUrl}/view/profile/{ProfileCode}
{ShareLinks:PublicBaseUrl}/view/exhibition/{ExhibitionCode}
```

Profile details expose sharing while the profile is active. Exhibition details expose
sharing only while the exhibition is Published. Copy uses the browser Clipboard API
with a selectable-link fallback. QR codes are generated locally as SVG using
`Net.Codecrete.QrCodeGenerator`; no link/code is sent to a QR service. The QR and SVG
download encode the canonical HTTP(S) link exactly; production configuration must use
HTTPS.

The public `/view/profile/{code}` and `/view/exhibition/{code}` routes are anonymous
fallback pages. Their **Open Android app** action translates the canonical route to
`arttechgallery://profile/{code}` or `arttechgallery://exhibition/{code}`. Unity still
makes the authoritative public API request, so a missing, inactive, Draft or
Deactivated resource does not become visible through sharing.

Production hosting must provide SPA fallback for `/view/...` and configure an HTTPS
`ShareLinks:PublicBaseUrl`. The custom-scheme button is the current MVP bridge. Direct,
verified HTTPS Android App Links require the production domain, signing certificate,
matching manifest hosts and `/.well-known/assetlinks.json`; they are not claimed yet.

## Validation commands

```powershell
dotnet restore ArtTechArtistPanel.slnx
dotnet build ArtTechArtistPanel.slnx --no-restore
dotnet test ArtTechArtistPanel.slnx --no-build --no-restore
dotnet publish src/ArtTechArtistPanel -c Release --no-restore -o artifacts/publish
git diff --check
```

Component/unit tests use HTTP doubles to exercise failures and races. The production
panel and browser smoke test use the real API. Backend integration tests remain in
the backend repository and require `ARTTECH_TEST_POSTGRES` for an isolated PostgreSQL
server; see `ArtTechBackend/docs/ARTIST_MANAGEMENT_FOUNDATION.md` in the workspace.

For the browser test, start both apps against a **disposable database** first. The
test creates two uniquely named accounts/profiles and intentionally leaves their
records in that database; discard the test database afterward. Never point it at
production or your normal development content.

```powershell
npm ci --prefix tests/browser --ignore-scripts
$env:ARTTECH_PANEL_URL = Read-Host 'Running test panel URL (ending in /)'
$env:ARTTECH_API_URL = Read-Host 'Running disposable test API URL (ending in /)'
node tests/browser/smoke.mjs
```

The script uses installed Chrome by default; set `ARTTECH_BROWSER_CHANNEL=msedge`
to use installed Edge. It tests the real register/login/profile/public-read flow,
reload refresh, logout, invalid login, a separate second user, invalid refresh,
and narrow-screen overflow. The exhibition flow also creates/edits/publishes,
deactivates/republishes, verifies anonymous visibility and stable codes, reloads
the detail route, exercises list actions, and tests missing-profile/cross-owner
handling. A competing real publication produces a genuine 409 to verify re-fetch.
One explicitly injected `401` checks the real refresh
and write retry. No domain API is mocked for the successful vertical slice.
Screenshots go to ignored `artifacts/`. Tokens and passwords are not logged.

## Publish/deployment

Publish output is a static site under `artifacts/publish/wwwroot`. Development
configuration/examples are excluded. Supply `Api:BaseUrl` and
`ShareLinks:PublicBaseUrl` in the published `appsettings.json` during deployment;
the shared source values are intentionally blank.
Server environment variables cannot directly configure downloaded WASM code.
If your host serves precompressed `.br`/`.gz` configuration files, regenerate those
sidecars after supplying configuration, or disable precompressed serving for JSON.
Never serve an old compressed configuration beside the updated JSON.

To smoke-test the release output locally, stop the development server and run
`node tests/browser/serve-published.mjs` with the same two test URL environment
variables. This loopback-only test host serves the publish directory and supplies
API configuration in memory; it is not a production hosting component. Run the
browser smoke script in a second terminal using the same variables.

The static host must serve WASM/JS/JSON with correct MIME types and support SPA
fallback to `index.html` for routes such as `/profile`. Do not rewrite missing
framework assets or configuration requests to HTML. Use HTTPS, revalidate HTML/config,
and allow long caching only for fingerprinted assets. Review CSP for the chosen
Blazor runtime and host; do not copy a CSP that blocks WASM or the configured API.

## Known limitations and remaining deployment checks

- JavaScript/XSS can access browser tokens, including session storage. No durable
  remember-me option exists. Browser restore/duplicated-tab behavior may preserve
  session storage; closing a tab is not a revocation mechanism.
- Logout is local to the tab. Other tabs/devices are not signed out. Backend tokens
  remain usable until expiry/security-stamp rules reject them. Identity refresh
  does not provide one-use token rotation/reuse detection. No revocation service
  or frontend server was added.
- Login/refresh do not check custom `IsActive`; protected management requests do.
- Real email delivery, confirmation/recovery UI, 2FA UI, and account suspension
  workflows are not part of this milestone. Accounts requiring 2FA cannot complete
  this minimal login UI. Server Identity validation/password policy remains final.
- Profiles use the backend's existing last-write-wins update semantics.
- Persist/protect backend Data Protection keys when deploying so token validity
  does not depend on ephemeral host keys. Configure HTTPS, CORS and static routing
  for the actual deployment; no speculative production infrastructure is included.
- Local Chrome smoke runs use disposable credentials over loopback HTTP. HTTPS
  certificates, production hosting/CSP, Firefox/Safari, and actual mobile devices
  require validation in the intended deployment environment.

## Artwork management

Profile and exhibition routes open details views. Choose **Edit profile** or
**Edit exhibition** to edit; Save and Cancel return to details. A profile-less new
account still starts with onboarding. Exhibition details are the central management
page and include publication controls plus the artwork list. Artwork create/edit
saves and confirmed deletion return to that parent page. No new edit-route or auth
architecture was introduced.

From an owned exhibition, choose **Manage artworks**. Protected routes are:

- `/exhibitions/{exhibitionId}/artworks`: ordered list and empty/retry states.
- `/exhibitions/{exhibitionId}/artworks/new`: create artwork.
- `/exhibitions/{exhibitionId}/artworks/{id}`: edit or confirm permanent deletion.

The forms use the real nested backend API through the existing bearer handler.
Editable fields are title, description, creation year, physical width/height in
centimeters, image selection and display order. Create requires one JPG or PNG;
edit retains the current image unless a replacement is selected. Files are
buffered in memory only for the request and are never stored in browser storage.
The client limits selections to 10 MiB and obvious JPG/PNG types; the backend
remains authoritative for content, pixel dimensions, validation, ownership and
publication visibility. Width/height are 1–1000 cm with at most two fractional
decimal places. Lower order values appear first; ties/gaps follow the backend's
deterministic ordering.

Artwork changes are allowed in all exhibition states. Published changes take
effect immediately. Inactive profiles are read-only. Failed saves keep input;
deletion needs confirmation and never deletes the referenced image. Requests are
disabled while pending, and late responses after navigation/logout are ignored.

The DTO still carries the server-provided image URL for public/viewer
compatibility, but it is not an editable form field. The panel does not fetch
image previews; the backend owns validation and local runtime storage. Production
storage is a later milestone.
Unsaved edits do not survive navigation/reload. Creation has no idempotency key;
after a lost response, check the list before retrying. Concurrent edits follow
the existing backend behavior without revision-conflict detection.

Production cloud storage and statistics are not implemented. Functional link/QR
sharing is implemented; verified production-domain Android App Links remain deployment work.
