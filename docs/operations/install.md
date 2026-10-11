# Installation readiness

This file records the historical Task 4 installation procedure. The later V1 completion provides an MSI and local CLI; follow the current [operator guide](quickstart.md) and [verification record](../verification/v1-release.md) instead of manually registering services.

No service installation, account creation, deployment or production host modification was performed during Task 4. The examples below are a future administrator-controlled deployment procedure, not an executed verification record.

Build and test only in the approved Windows DEV VM. Before any production deployment, provide a trusted signing/release strategy accepted by the production host's active security policies; never disable protections to run unsigned builds. Install released binaries under an administrator/SYSTEM-writable directory, with service accounts granted read/execute only. Do not run from the source checkout or a user-writable directory.

Use Windows SCM automatic startup without interactive login. HostAgent uses the non-privileged virtual account NT SERVICE/HostAgent. HostAgent.Helper is a separate LocalSystem service, with no generic launcher or remote control. Example SCM configuration after approved release packaging:

```powershell
sc.exe create HostAgent binPath= '"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"' start= delayed-auto obj= 'NT SERVICE\HostAgent'
sc.exe sidtype HostAgent unrestricted
sc.exe create HostAgent.Helper binPath= '"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"' start= delayed-auto obj= LocalSystem
sc.exe sidtype HostAgent.Helper unrestricted
```

Provision %ProgramData%/HostAgent with a protected DACL granting full inherited file/directory control only to NT SERVICE/HostAgent, SYSTEM and Administrators, with a trusted owner. HostAgentDataDirectory creates this ACL on a new directory and rejects an unsafe existing directory. The helper directory is SYSTEM/Administrators-only, excluding HostAgent; validate existing helper config/journal/sidecar owners and ACLs. No automatic broad ACL repair exists.

Provision protected service/helper configuration with safe defaults first. Create Event Log sources for each service during approved installation so the non-privileged runtime need not create them. Configure recovery as documented separately. Start monitoring in Shadow with adapters disabled; then provision synthetic secrets in the service identity in an isolated deployment test before enabling outbound adapters. Keep helper registrations empty until each capability and approval path has been separately accepted.

An installation acceptance run must verify actual User/service SIDs, protected paths, DPAPI profile access, collector permissions, pipe authentication across the two accounts, startup without login, stop deadlines, Event Log codes and SCM crash recovery. These are open deployment checks; in-process host tests are not a substitute.
