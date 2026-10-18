# View incident summary reads — 2026-10-18

## Fresh installed evidence, not carried-forward acceptance

Source base b5011680411df34ec6f0809b9635a269840ec7ea was clean. Environment
reverified: AGENTPC, AgentPC\AgentPCdevtest, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. Complete Git bundle
created before edits. Installed1.2.4 retains Shadow/readOnly; no service control,
elevation, configuration, installed database modification or security change by
the agent.

Fresh installed1.2.4 acceptance verified all1203 payload hashes, but View still
failed: client response_frame/client_deadline10043ms; server incident_heads
cancelled9829ms, dispatch server_deadline10082ms. Previous WAL locking regression
fixes remain valid but were insufficient to close this actual live gate.

Operator ran the reviewed elevated aggregate-only diagnostic successfully:
72 incident heads,81334 JSON characters;3117 snapshots,152998859 JSON characters;
largest history359; snapshot aggregate scan2563ms. Exit0, monitorFailure=null,
servicesUnchanged=true. Local stdout evidence was read again for this record:
HostAgent-DEV-releases/incident-cost-1.2.4/aggregate-read-65b971fcbef44fe6956430d16b504f8c/stdout.txt.
These are aggregate read-cost measurements, not database integrity checks or
proof of how many snapshots a particular timed-out request consumed.

## Confirmed implementation defect and narrow fix

Dashboard called ReadIncidentHeadsAsync, which loads all heads plus up to64 full
telemetry snapshots per incident before selecting20. Overview, incident detail
and investigation need only head/diagnosis data. This unnecessary archive
dependency is reproduced deterministically: a valid head plus a SQLite-valid
JSON archive canary that is not an IncidentSnapshot makes all three View commands
fail. Complete archive reads must still reject that canary.

New explicit store summary APIs read only incidents.payload_json. List reads
apply SQL limit1..100 before deserialization, ordered updated_at_utc DESC and
incident_id DESC; Dashboard asks for20. Detail uses parameterized exact ID lookup
with LIMIT1. Summary snapshots are empty. Latest diagnosis confidence and bounded
evidence are preserved. No incident_snapshots query, immediate transaction,
schema migration, deadline increase, cache clearing or incident deletion.

Operational64-snapshot restore, remediation, outbound and full archive callsites
are unchanged. Existing durable archive tests remain required. These consumers
can still incur large-history cost; this change is specifically for View.

## Fresh tests and review

Evidence directory:
%LOCALAPPDATA%\HostAgent-DEV-evidence\view-summary-read-20261018T200204Z.

Test-first RED: focused46 total,43PASS/3FAIL. All new command cases failed on the
unused archive dependency. First GREEN attempt had46PASS/1FAIL: new ordering
fixture reused a unique correlation key; corrected fixture keys, no product
behavior weakened. Final focused47/47PASS, including all three canaries and
summary contracts: limit/order/tie, old exact ID, confidence82, no snapshots,
missing/injection-literal IDs, invalid limits, and25 full durable archives.

Command:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter "FullyQualifiedName~Task2PersistenceTests|FullyQualifiedName~DashboardViewTests|FullyQualifiedName~ServiceLifecycleTests" --logger 'console;verbosity=normal'
```

Independent read-only source review found no new blocker and verified the
unchanged non-View consumers. `git diff --check` passed. Native250ms safeguards
remain initial1024MiB/soft768MiB/hard512MiB and20GiB disk. Complete Release results
are appended only after the run finishes.

Fresh Release build PASS, zero warnings/errors. Complete suite PASS477/477
(Core62/Infrastructure400/Desktop15), zero failures/skips, exit0. Both native
monitor receipts report monitorFailure=null. Exact commands:

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

## Open gates

Installed1.2.4 remains unchanged and View acceptance is still FAIL. No claim of
fixed installed behavior before operator upgrade plus fresh actual-data test.
Actual MSI/package/live acceptance for this new source is pending. Available C:
space near20.17GiB does not provide room to publish a complete new candidate while
maintaining the20GiB guard. No package build started and no limit lowered.
Main installed database integrity, service-context PDH and helper first-use audit
are separate open checks. Keep Shadow/readOnly; no pilot proposed.
