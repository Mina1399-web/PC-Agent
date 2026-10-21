# Configuration and secrets

Runtime configuration is a protected startup snapshot at %ProgramData%/HostAgent/service.json. Command-line arguments, environment configuration providers and appsettings do not bind remediation options or credentials. Missing, unreadable, oversized, duplicate/unknown-field or invalid configuration falls back to Shadow, read-only, outbound adapters disabled, configuration unavailable for remediation. Changes require restart.

The JSON shape is below. enableNetworkProbes is optional for backward
compatibility; its absence defaults to false. Duplicate/unknown fields and
non-boolean values are still rejected. The local administrator can use
`network-probes on` or `network-probes off`; the CLI announces the gateway,
1.1.1.1 and example.com targets before asking for APPLY. This does not enable
notifications or remediation. See [privacy](privacy.md).

```json
{
  "mode": "Shadow",
  "readOnly": true,
  "killSwitch": false,
  "collectionIntervalSeconds": 60,
  "heartbeatIntervalSeconds": 60,
  "requestTimeoutSeconds": 10,
  "enablePushover": false,
  "enableHeartbeat": false,
  "enableNetworkProbes": false
}
```

Only Shadow and Confirmation are accepted. Collection interval is 5–3600 seconds, heartbeat interval 30–3600 seconds and transport timeout 1–30 seconds. Defaults are 60/60/10. LimitedAutonomous is unsupported. Shadow writes recommendations/proposals and local history. Confirmation additionally requires readOnly=false, valid configuration, a durable actionable incident, confidence >=80 and exact fresh approval. Helper must independently have its matching operation enabled, readOnly=false/shadow=false and trusted helper-side approval. Merely changing this file cannot grant approval.

Writable Confirmation also checks the persisted diagnosis observation time.
It must not be in the future and must be no older than the greater of three
minutes or twice the configured collection interval (inclusive). A fresh
approval or snapshot cannot renew an old diagnosis. Older correlation evidence
may legitimately support a newly observed diagnosis, so evidence items are not
all forced into a three-minute window. Historical incidents remain readable in
Shadow/read-only. An unresolved lifecycle state does not establish that a
historical service fault is still present now.

The Task 3 helper configuration remains %ProgramData%/HostAgent.Helper/helper.json: exact fields readOnly, shadow, killSwitch, executionTimeoutSeconds, services, processes and approverSids. Defaults deny execution with empty registrations. The protected helper disable-remediation marker is checked before each invocation. See [Task 3 review](../security/task-3-review.md) for the exact trust boundary.

Secrets have closed names PushoverToken, PushoverUser and HeartbeatUrl. ProtectedSecretStore writes only CurrentUser DPAPI ciphertext, with application/name-specific entropy and UI forbidden. It validates the current identity, protected directory, file owner/ACL, no file reparse point, and bounded input. Plaintext or corrupt/foreign-access ciphertext is unavailable. No environment-variable, JSON, command-line or plaintext fallback exists.

Production uses %ProgramData%/HostAgent/secrets-production; the standard Development hosting environment selects the separate secrets-development directory. Each contains only the fixed *.dpapi filenames. Development must use a separate service identity/VM and synthetic values; do not copy production ciphertext into DEV. Environment selection does not change Shadow/read-only policy.

Provisioning is through ProtectedSecretStore.WriteAsync in a trusted local process running as the actual NT SERVICE/HostAgent identity with its profile loaded. An interactive administrator's CurrentUser ciphertext will not decrypt as the service. The API accepts secrets in memory; never put values in source, scripts, arguments, logs or transcripts. A production provisioning UI/CLI was not implemented or exercised. Until identity-correct provisioning is performed, keep both adapters disabled and use durable local history.

Recovery/rotation uses the same trusted identity API, which writes a temporary protected ciphertext file, flushes and atomically replaces the destination. Back up DPAPI profile/key material through an approved OS recovery process; a DB/source backup alone does not restore the identity's keys. On identity/profile/machine changes, reprovision from the trusted credential source; do not weaken ACLs or switch to machine-wide/plaintext storage.

Pushover uses verified HTTPS with redirects/cookies disabled and fixed message endpoint. INFO remains local, WARNING priority 0, HIGH priority 1, CRITICAL priority 2 with retry=60 and expire=3600. Server retry is capped by Pushover; receipt polling observes acknowledgement/expiry without resending accepted emergency messages. Receipt lookup requires the token in the HTTPS query as specified by the API. HTTP request/URI/payload logging and tracing must remain disabled; adapters never log these values.

Healthchecks-compatible URLs are loaded only from protected storage, require absolute HTTPS and reject credentials in userinfo, queries and fragments. Self-hosted HTTPS endpoints are supported as trusted operator configuration. There is no management API, auto-provisioning or inbound listener. Outgoing heartbeats have an empty body; unhealthy or stopping agent sends /fail, healthy agent sends the base URL. TLS validation uses the OS defaults without callbacks/bypasses.

Protocol references: [Pushover API](https://pushover.net/api) and [Healthchecks pinging API](https://healthchecks.io/docs/http_api/). Live delivery and account-scoped DPAPI provisioning remain unverified.

The authenticated read-only View `overview` can include `DiskIoDiagnostics`
from the same current disk snapshot. `available` means diagnostic metadata is
recorded, not that disk I/O is healthy. `not_recorded` covers older/empty
metadata, `invalid` rejects malformed metadata while preserving readable
volumes, and `unavailable` means no fresh eligible disk sample. The inclusive
freshness limit is three minutes; response generation never refreshes a sample.
At most ten fixed native call records expose stage/counter IDs, unsigned API
status, meaningful counter CStatus and an optional raw-value finiteness flag.
They contain no counter values, paths or exception text. Numeric unknown errors
are retained without guessing their cause. No extra native probe or IPC command
is invoked by this projection. Neither this metadata nor an interactive/token
comparison replaces acceptance of the actual installed collector under SCM.
