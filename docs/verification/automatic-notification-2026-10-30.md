# Automatic incident notification — 2026-10-30

The installed DEV service automatically published and sent one synthetic
Warning incident through its ordinary notification worker. This verifies
the installed incident-to-outbox-to-Pushover path, including access to the
service's protected credentials. The test runner made no HTTP requests and
did not call the service's notification sender or publisher in the live run.

Execution was confined to the approved rebuilt VM: `AGENTPC`, VirtualBox
UUID `0bb543d1-7bb0-43ed-9c94-596d63ef3310`, installed HostAgent 1.2.9.
The operator confirmed ordinary Windows UAC for the bounded test script.
No configuration, service lifecycle, repair, maintenance or security-policy
changes were part of this test.

## Procedure and fresh evidence

The non-elevated preflight used private scratch databases and the installed
`IncidentPublisher` and storage assemblies. Publishing the same incident
twice left exactly one queued notification with zero delivery attempts.
The synthetic incident was closed successfully. No network call was made.

The live run inserted one clearly labeled synthetic incident through
`SqliteHostAgentStore.SaveIncidentBundleAsync`. It used the unique correlation
key `synthetic.notification.selftest.d670b25f80584ba0953ec62326d65a79`,
Warning severity and a `no_action` recommendation. The installed background
worker then read and published the incident without a manual send call.

- Incident ID: `d670b25f80584ba0953ec62326d65a79`.
- Inserted: `2026-10-30T19:04:27.4348431Z`.
- Service accepted delivery: `Sent`, code `push_sent`, attempts `1`.
- Delivery journal contains one claim and one successful send.
- Observed receipt of that result: `2026-10-30T19:04:47.3593782Z`.
- After another configured notification interval plus ten seconds, the
  notification remained `Sent` with exactly one attempt.
- Test completed: `2026-10-30T19:05:59.2331844Z`.
- The test incident was retained as `Resolved`, severity `Information`,
  with a `TEST COMPLETED` explanation. A separate non-elevated read through
  the installed authenticated View pipe confirmed the closed state.
- Both `HostAgent` and `HostAgent.Helper` remained `Running` on final readback.
- The one-time interactive consent-launch task was removed after completion.

Sanitized JSON receipts are retained in the host work area's
`expanded-feature-evidence/automatic-incident-live-result.json` and
`automatic-incident-preflight-result.json`. The bounded test script is
retained outside the repository as `Test-AutomaticIncident-20261030.ps1`;
its verified host/guest SHA-256 is
`584A7FF2736C2ABC9B3474F2CD2A8128F696A0311CC52F417A1D3E6358E234AB`.

## Scope

This was a synthetic persisted incident, not an induced real PC failure.
It verifies automatic publication, delivery and the observed absence of
duplicates for that incident. It does not verify the detection of every
real fault, future delivery under outages, emergency acknowledgement,
phone display, heartbeat outage/recovery, or production acceptance.
Phone receipt of the earlier direct test was already confirmed by the
operator's screenshot; that is separate from this automatic-incident test.
The setup-time Healthchecks acceptance is recorded separately in
[Heartbeat setup result](heartbeat-setup-2026-10-30.md); it does not establish
outage/recovery behavior.
