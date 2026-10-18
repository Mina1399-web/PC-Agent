# View WAL reader stabilization — 2026-10-18

## Scope and observed installed failure

Approved DEV VM AGENTPC / VirtualBox, UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running;
AgentPC\AgentPCdevtest. Source base 08ac23f78f6b4eab516deee384d7b15d5f7ac961,
initially clean. A complete Git bundle was created before edits.

The operator reported installation of 1.2.3, configuration preserved in
Shadow/readOnly and no restart required. Fresh read-only acceptance verified
registry version and all 1202 installed payload hashes. Both services were
Running (HostAgent PID9552, Helper PID5600). Overview nevertheless failed twice.
The diagnostic request reached response_frame and expired at 10027ms. Correlated
Application event properties identify incident_heads cancellation at 9947ms and
dispatch server_deadline at 10027ms. No raw payload or arbitrary log text saved.

Installed acceptance evidence (separate from the source regression):
%LOCALAPPDATA%\HostAgent-DEV-evidence\installed-1.2.3-acceptance-20261018T174730Z.

## Reproduction and changes

Two storage concurrency defects were reproduced on disposable real SQLite WAL
databases, without accessing installed databases or controlling services:

1. Incident reads used the default immediate transaction and waited for a
   writer slot even though all statements were reads. Change only this read
   transaction to deferred. All head/snapshot reads remain in one transaction.
2. Main-store connections used Shared cache. Even with a deferred transaction,
   same-process shared-cache table locks prevented reading committed incident
   rows while a writer held an uncommitted update. Use Private connection caches
   with WAL; do not enable read_uncommitted or relax durability.

No schema migration, persistent database format, write transaction, IPC deadline,
ACL, service/configuration/security change or retry increase is involved.

Regression holds an actual WAL writer open after replacing an incident's JSON
with an uncommitted valid JSON object. Readers must finish while that writer is
still open and return the committed incident ID, snapshot and exact confidence82.
It covers both archive/head reads and Private/Shared writer caches. The fixture
update is always rolled back before assertion completion.

An additional real Windows authenticated named-pipe integration test uses
DashboardDispatcher and SQLite with that writer held open. Overview must return
the committed incident identity/confidence and Shadow/readOnly state. It does
not connect to the installed endpoint.

## Fresh verification

Evidence: %LOCALAPPDATA%\HostAgent-DEV-evidence\view-read-contention-20261018T181514Z.
Runs use local SDK10.0.401 and runtime10.0.12, native250ms monitor, initial
RAM1024MiB, soft768MiB/hard512MiB and C:20GiB guards. Heavy checks run serially.

- First runner invocation failed before testing because native child working
  directory was not set; corrected runner, retained output.
- First test setup used invalid JSON and hit the schema CHECK; corrected fixture
  to valid JSON. This setup error is not RED evidence.
- `red-behavior`: 3PASS/2FAIL, both new reads waited for writer (expected RED).
- `green`: 5/5PASS after deferred transaction; new reads278ms/66ms.
- `red-shared-cache`: 5PASS/2FAIL, Shared writer cases still waited (expected RED).
- `green-private-wal`: 7/7PASS after Private cache; concurrent reads56–274ms.
- `focused-pipe-lifecycle`: 43/43PASS, real pipe regression82ms.

Focused command (the first three runs filtered only Task2PersistenceTests):

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter "FullyQualifiedName~Task2PersistenceTests|FullyQualifiedName~DashboardViewTests|FullyQualifiedName~ServiceLifecycleTests" --logger 'console;verbosity=normal'
```

Independent source reviewer verified VM and reviewed both revisions; Shared
cache gap was identified and then resolved with a failing regression. Final
review found no further blocking issue. Reviewer performed no builds/service
operations. Full Release and isolated runtime results are recorded below only
after completion.

### Complete Release verification

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

Build PASS, zero warnings/errors. Suite PASS473/473 (Core62,
Infrastructure396, Desktop15), no failures/skips, exit0,150.0s wrapper elapsed.
Both native monitor receipts report monitorFailure=null. `git diff --check`
passed. Independent read-only protected-service check still shows original
PID9552/5600 and all six critical installed hashes match the1.2.3 manifest.
Neither service was controlled or reconfigured.

Current-source isolated runtime observe run PASS,20.1s, two successful real View
requests374ms/36ms, zero failures/helper calls, minimum free1354.3MiB, no guard
abort. FixtureID e3f765d6854e4547aeb6c494d3df0add. Command:
`dotnet tests/HostAgent.PipeProbe/bin/Release/net10.0-windows/HostAgent.PipeProbe.dll isolated-runtime e3f765d6854e4547aeb6c494d3df0add observe`.
This is an ordinary-user private runtime smoke, not installed-service, soak or
large-dataset acceptance. The real lock-contention assertion is the separate
regression above.

## Limits

The installed service still runs the previous1.2.3 binary. No claim that its live
failure has disappeared is made. Exact production lock ownership was not traced;
the reproduced defects explain a real blocking path at the observed stage, but
do not exclude other causes. Installed dataset scale is not available to the
unelevated tool. ReadIncidentHeadsAsync still loads all heads and up to64 full
snapshots per incident before Dashboard selects20, so large-history/N+1 cost
remains a separate unverified risk. A corrected package requires operator
installation and fresh installed-runtime acceptance before closing that gate.
Shadow/readOnly remains enabled; main installed DB integrity, helper first-use
durable audit and service-context PDH remain separate open gates.
