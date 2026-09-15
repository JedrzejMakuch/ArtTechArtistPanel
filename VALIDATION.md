# Frontend Foundation validation

Validated on 2026-09-14–15 with .NET SDK 10.0.201 and an isolated PostgreSQL 18 cluster.

| Check | Result |
| --- | --- |
| Panel restore | Passed |
| Panel Debug build | Passed, zero warnings/errors |
| Panel xUnit/bUnit suite | 30 passed, zero failed/skipped |
| Panel Release publish | Passed |
| Backend restore/build | Passed, zero build warnings/errors |
| Backend PostgreSQL suite | 33 passed: 22 existing cases plus 11 browser/refresh cases |
| Chrome smoke against dev server | Passed |
| Chrome smoke against published Release assets | Passed |
| Desktop/mobile-width screenshot inspection | Readable layout; no narrow-screen horizontal overflow |
| Publish configuration inspection | Development configuration/examples excluded; shared API value remains empty |
| Git whitespace checks | Passed, including newly created text files |

The browser tests exercised real registration, login, onboarding POST, editing PUT,
fresh anonymous public-profile reads, reload through `/refresh`, invalid login,
logout/protected navigation, a second isolated account, and rejected refresh.
A separately identified injected 401 exercised one real refresh and one successful
write retry. There were no browser runtime errors. Screenshots are local ignored
artifacts, not repository assets.

All database tests used an isolated cluster. The browser run used a separate
disposable database. The application's normal development database was not used.
The disposable browser database was dropped and the temporary test hosts/cluster
were stopped after validation.
No Unity files, database models, or migrations changed.

The publish command reported the optional `wasm-tools` workload is absent; publish
still succeeded and the resulting Release assets passed the real browser flow.

Still environment-specific: HTTPS certificate behavior, actual deployment CORS/CSP
and SPA routing, Firefox/Safari, physical mobile browser behavior, and accessibility
with a screen reader. The local smoke run used loopback HTTP and disposable test
credentials; it does not certify a production deployment.

See README.md for commands, configuration, session limitations and reproduction.

## Exhibition Management UI validation — 2026-09-15

| Check | Result |
| --- | --- |
| `dotnet restore ArtTechArtistPanel.slnx` | Passed |
| `dotnet build ArtTechArtistPanel.slnx --no-restore` | Passed, 0 warnings/errors |
| `dotnet test ArtTechArtistPanel.slnx --no-build --no-restore` | 59 passed, 0 failed/skipped (30 existing + 29 added) |
| `dotnet publish src/ArtTechArtistPanel -c Release --no-restore -o artifacts/publish` | Passed |
| Chrome smoke against final published Release assets | Passed, no browser runtime errors |
| Responsive checks | No horizontal overflow at 320, 390, 1280 pixels; desktop/list/mobile/editor screenshots inspected |
| Published configuration | Only shared empty API configuration; development/examples excluded |
| Git whitespace checks | Passed, including all untracked text files |

New component/client tests cover empty/populated lists and backend ordering,
metadata-only creation/editing, canonical server responses, all three lifecycle
states and actions, deactivation confirmation/cancel, duplicate-write prevention,
validation/403/404/5xx errors, recoverable network errors, 409 re-fetch (including
failed re-fetch), preservation of unsaved edits, lost transition response recovery,
inactive/missing profiles, protected routes, late responses after route change or
logout, and the existing bearer handler's single refresh/retry for exhibitions.
All previous profile/session tests passed unchanged.

The real browser flow included registration/login/profile onboarding, exhibition
list/create draft/edit/publish/deactivate/republish, stable ExhibitionCode, anonymous
200/404 visibility checks, detail-route reload/session restoration, list publication
actions, and a genuine 409 produced by a competing publication against the actual
API. It also checked missing-profile guidance, cross-owner detail rejection,
all protected exhibition routes while logged out, profile regression, rejected
refresh, escaped text, and desktop/narrow-width layouts. Successful domain API
responses were not mocked. Existing injected-401 testing remains explicitly
separate from the successful real API flows.

Browser validation used a randomly named disposable database on an isolated
PostgreSQL 18 server. Backend configuration was supplied only to the test process;
the normal development database and source configuration were untouched. The
existing backend automatically applied its existing migrations to this disposable
database. No backend source, migration or Unity files were changed.

The initial sandboxed backend launch failed on Windows Event Log permissions;
the approved process launch succeeded. Release publish reported the optional
`wasm-tools` workload was absent, but completed and passed Chrome validation.
Development-server browser testing was not repeated for this slice.

Remaining checks: production HTTPS/CORS/CSP/static routing, Firefox/Safari,
physical mobile browsers and screen-reader accessibility. Browser widths are
viewport checks, not physical-device testing. Unsaved edits are not persisted
across navigation/reload. Creation has no server idempotency key: after a lost
response, check the list before manually retrying a create. Concurrent metadata
edits retain backend last-write-wins semantics. Artwork management and final
visual design were separate milestones at that checkpoint. Artwork UI is now
validated below.

## Artwork Management UI — completed 2026-09-15

| Check | Result |
| --- | --- |
| Panel restore | Passed |
| Panel build | Passed, 0 warnings/errors |
| Full panel xUnit/bUnit suite | 85 passed, 0 failed/skipped (59 existing + 26 artwork cases) |
| Release publish | Passed; optional wasm-tools workload remains absent |
| Backend restore/build | Passed, 0 warnings/errors |
| Full PostgreSQL suite | 82 passed, 0 failed/skipped |
| Chrome on final Release output | Passed, no browser runtime errors |
| Responsive layout | Editor at 1280/390/320 px and list at 320 px: no horizontal overflow; screenshot reviewed |
| Tracked/untracked whitespace checks | Passed |

Component/client coverage includes ordered/empty lists, metadata-only create/edit,
canonical responses, invalid dimensions/URLs, inactive-profile read-only state,
400/403/404/409/5xx/network errors, failed-save input retention, confirmation/cancel,
failed deletion, duplicate writes, stale responses after logout/navigation,
protected route attributes and the existing single-refresh retry for DELETE.
All existing profile/exhibition/session tests remain passing.

The real browser flow uses the actual API and a disposable PostgreSQL database:
registration/login/profile -> exhibition lifecycle -> artwork list/create/edit,
dimensions/image URL/order -> public metadata and real demo-image HTTP checks ->
reload/session restoration -> delete cancellation/confirmation -> empty list and
anonymous 404. It also checks escaped text, protected artwork routes after logout,
cross-owner rejection, existing profile/session behavior and exhibition conflict
recovery. Successful domain responses are not mocked. The foundation's injected
401 check remains an explicitly separate fault-injection test.

No backend source, schema, migration or Unity change was needed. No normal
development database was used. Test servers and the disposable database were
cleaned up after validation.

Remaining checks: production hosting/HTTPS/CORS/CSP, Firefox/Safari, screen readers
and physical mobile browsers. Viewport checks are not device testing. No fresh
Unity/Android run was performed. Image availability/storage, navigation-away draft
persistence and revision/idempotency features remain outside this slice.

## Navigation correction — final recovery validation

The existing implementation was recovered without resets. Before interruption,
85 updated panel tests and Release publish had passed; browser validation was
unfinished. Recovery added two focused details/edit/cancel tests and corrected the
details page title and pending-action edit guard.

- Panel restore passed; final build: 0 warnings/errors.
- Full panel suite: 87 passed, 0 failed/skipped.
- Release publish passed; optional wasm-tools workload notice remains.
- Full PostgreSQL suite rerun: 82 passed, 0 failed/skipped.
- Final Release Chrome smoke passed with no runtime errors, including existing
  auth/profile/exhibition/artwork regressions and corrected navigation.
- Profile GET responses: exactly one 404 during the first user's onboarding;
  no additional profile 404s after creation, reload, login or management navigation.
  Backend ownership lookup and frontend null/onboarding handling were correct.
- Populated exhibition details and artwork forms: no overflow at 320/390/1280 px;
  320 px details screenshot reviewed. Existing profile responsive checks passed.
- Tracked and untracked whitespace checks passed.

Existing profiles now open details with explicit Edit; successful saves return to
details. Exhibition creation and edits return to its central details view, which
includes artwork actions. Artwork creation/edit/delete return to that parent view.
Cancel discards only unsaved form changes; failed saves retain input. No backend,
API, schema, Unity, auth/session or styling changes were needed. Validation used
only the disposable UX database; validation hosts/database were cleaned up.

Other browsers, screen readers, production hosting and physical mobile checks
remain manual work. Storage/upload and the next milestone were not started.
