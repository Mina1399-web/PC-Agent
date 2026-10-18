# Host Agent V1 – nezávislý funkční, technický a bezpečnostní audit

Datum: 2026-10-12. Audit proběhl výhradně ve schválené Windows DEV VM `AGENTPC` (VirtualBox, výrobce innotek GmbH), účet `agentpc\agentpcdevtest`, checkout `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`. `VBoxGuest` i `VBoxService` byly ve stavu Running. Větev před auditem: `codex/diagnostic-self-service`; HEAD `94ce71b2374ecfe9bb78ca302df129056ee6bfae`; pracovní strom byl čistý. Použit byl lokální SDK 10.0.401 v `%LOCALAPPDATA%\HostAgent-DEV-tools\dotnet-10.0.401`. Výstupy buildů zůstaly v ignorovaných `bin/obj`. Změněn nebyl zdrojový kód ani bezpečnostní nastavení; nic nebylo commitnuto.

## Hodnocení připravenosti

**V1 je funkčně pokročilý a v DEV VM dobře testovaný, ale jako produkt není plně připravený na produkční nasazení ani autonomní opravy.** Čerstvý Release build a všech 226 testů prošly. Nainstalovaná VM verze 1.0.4 odpověděla na read-only příkazy; obě služby běží. Reálný provoz ale odhalil stav `degraded: true`, jehož příčinu CLI neumí vyjmenovat. Žádný z 20 vrácených incidentů nenabízel automatickou opravu a aplikace neukázala bugcheck diagnózu, přestože systémový Event Log měl tři WER 1001 události v posledních 24 hodinách. To samo o sobě nedokazuje jejich ztrátu: seznam CLI je omezen na 20 nejnovějších incidentů a stav checkpointu nebyl měněn ani forenzně rekonstruován.

VM kandidát je výslovně DEV-only a nepodepsaný. Živé Pushover/Healthchecks doručení, servisní DPAPI pod skutečnou virtuální identitou, SCM obnovu po reálném pádu, produkční podpis a příčinu konkrétního restartu tento audit neověřil. Automatické opravy proto nelze považovat za ověřené ani bezpečné k produkčnímu povolení.

## Čerstvé ověřovací běhy

Příkazy byly spuštěny ve výše uvedeném VM checkoutu. `dotnet` nebyl v PATH, proto byl použit přímo soubor SDK:

```powershell
$sdk = 'C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-tools\dotnet-10.0.401'
$env:DOTNET_ROOT = $sdk
$env:PATH = $sdk + ';' + $env:PATH
& (Join-Path $sdk 'dotnet.exe') build HostAgent.sln --configuration Release
```

Výsledek: exit 0, 0 upozornění, 0 chyb.

```powershell
& (Join-Path $sdk 'dotnet.exe') test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=minimal'
```

Výsledek: Core 50/50, Infrastructure 176/176; dohromady **226 passed, 0 failed, 0 skipped**, exit 0.

Focused běhy:

```powershell
& (Join-Path $sdk 'dotnet.exe') test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~BaselineEngineTests|FullyQualifiedName~DiagnosticEngineTests|FullyQualifiedName~IncidentEngineTests|FullyQualifiedName~PolicyEngineTests' --logger 'console;verbosity=minimal'
```

Výsledek: 41/41, exit 0.

```powershell
& (Join-Path $sdk 'dotnet.exe') test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build -m:1 --filter 'FullyQualifiedName~WindowsCollectorIntegrationTests|FullyQualifiedName~WindowsEventLogCollectorTests|FullyQualifiedName~SqliteHostAgentStoreTests|FullyQualifiedName~Task2PersistenceTests|FullyQualifiedName~CollectionCoordinatorTests|FullyQualifiedName~NamedPipeSecurityTests|FullyQualifiedName~HelperProtocolTests|FullyQualifiedName~RemediationGuardTests|FullyQualifiedName~ProtectedSecretStoreTests|FullyQualifiedName~OperatingModeTests|FullyQualifiedName~OutboundTransportTests|FullyQualifiedName~ServiceLifecycleTests|FullyQualifiedName~HelperServiceHostingTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~CliTests|FullyQualifiedName~InstallerStagingTests' --logger 'console;verbosity=minimal'
```

Výsledek: 116/116, exit 0.

```powershell
& (Join-Path $sdk 'dotnet.exe') list HostAgent.sln package --vulnerable --include-transitive
```

Výsledek: exit 0; NuGet `https://api.nuget.org/v3/index.json` neohlásil zranitelné balíčky v žádném z osmi projektů. Jde o výsledek aktuálního zdroje NuGet, ne o audit všech nativních/runtime komponent ani budoucích verzí.

Tracked soubory pod `src`, `tests` a `docs` byly prohledány heuristikou běžných přiřazení API klíčů, hesel a tokenů. Výsledek: 0 odpovídajících souborů. Heuristika nezachytí všechny formy tajemství; nejde o důkaz jejich absolutní absence.

## Funkční mapa a výsledek

| Oblast | Stav | Čerstvý důkaz a hranice |
|---|---|---|
| Windows system collector | **Částečně ověřeno** | `WindowsCollectorIntegrationTests` v čerstvém focused/full běhu čte skutečné údaje VM o Windows, buildu, uptime a lifecycle události. Neověřuje dlouhý provoz ani restart hostitele. |
| CPU/RAM a top procesy | **Částečně ověřeno** | Read-only integrační test ověřuje rozsahy paměti, CPU, limity a procesní hodnoty na VM. Neověřuje přesnost dlouhodobého monitoringu ani dopad minutového zatížení. |
| Disky a I/O | **Částečně ověřeno** | Integrační test skutečně čte systémový svazek, kapacitu, volné místo a dostupnost I/O čítačů. SMART se nečte; kolektor vždy přidá `smart_health_unavailable` a vrací `Partial`. |
| Procesy a služby | **Částečně ověřeno** | Windows collector test ověřuje čtecí enumeraci procesů a služeb. Nativní ukončení procesu ani změna služby nebyly provedeny; jejich executor používá fake testy. |
| Síť | **Částečně ověřeno** | `WindowsCollectorIntegrationTests` a `OutboundTransportTests` pokrývají adaptér a fake transport. Instalovaná konfigurace má aktivní sondy vypnuté; ICMP na gateway/1.1.1.1 a DNS `example.com` proto nebyly živě ověřeny a nic se neposílalo na externí endpoint. |
| Event Log | **Částečně ověřeno** | Fresh Windows Event Log collector testy a bugcheck parser testy prošly. Read-only dotaz na System log nalezl 3 WER 1001 události za 24 h. Instalovaná aplikace vrátila 20 incidentů a úspěšně zpřístupnila detail všech 20; žádný z těchto 20 neměl bugcheck root-cause kód. Nelze z toho určit, zda byly nalezené WER události zpracovány, protože `incidents` vrací jen posledních 20 a checkpoint nebyl diagnosticky rozebrán. |
| SQLite, migrace a retention | **Částečně ověřeno** | Čerstvé `SqliteHostAgentStoreTests` a `Task2PersistenceTests` v 116 focused běhu plus kompletní suite pokrývají schéma, transakce, snapshoty, retention a obnovu. Instalovaná služba přes administrátorský pipe načetla incidenty z trvalé DB. Neprovedl se upgrade staré cizí DB ani obnova ze zálohy. |
| Baselines, diagnózy, confidence | **Ověřeno v testovaném rozsahu** | 41 Core focused testů a plná suite pokrývají robustní baseline, learning režimy, známé diagnostické korelace, confidence assertions a hranice časových oken. Živý detail incidentu vrátil confidence a root-cause kódy. Model nebyl ověřen proti označenému datasetu skutečných poruch. |
| Incident lifecycle, deduplikace, snapshots | **Ověřeno v testovaném rozsahu** | Lifecycle graf, opakované události, durable incident/snapshot transakce a deduplikace jsou v Core/SQLite/Task2 testech. Instalovaná CLI načetla 20 trvalých incidentů a detailů; nedošlo k simulovanému novému incidentu ani k testu zotavení po restartu procesu. |
| Policy, Shadow a approval | **Částečně ověřeno** | Policy/OperatingMode/AdminDispatcher/Cli testy passed. Živá CLI `status` vrací `Shadow`, `ReadOnly=true`; detail incidentu ve všech 20 případech uvádí `automaticRepair.available=false`. Živá approval ani nativní operace se záměrně neprovedla. |
| Named Pipes a identita | **Ověřeno pro současnou VM instalaci, širší deployment částečný** | Elevovaný CLI proces úspěšně provedl `status`, `incidents` a 20 požadavků `incident <N-guid>` přes instalované lokální administrační rozhraní; všechny vrátily exit 0. Pipe/protokol/ACL testy passed. Jiný uživatelský účet nebo produkční SID/ACL kombinace nebyla testována. |
| Helper allowlist, audit, limity a kill switch | **Částečně ověřeno** | `HelperProtocolTests`, `NamedPipeSecurityTests`, `RemediationGuardTests`, audit/policy suite passed; konfigurace instalace má prázdné allowlisty. Reálná operace, auditní výsledek z nativního cíle ani fyzický kill-switch test nebyly provedeny. SQLite append-only triggery chrání před běžnou aplikací, ne před administrátorem schopným nahradit DB. |
| Pushover | **Částečně ověřeno** | Fake transport/outbox testy prošly. Nainstalovaná konfigurace hlásí `EnablePushover=false`; skutečné credentials ani externí doručení se nepoužily. |
| Healthchecks kompatibilní heartbeat | **Částečně ověřeno** | Fake HTTPS/timeout/cancellation testy prošly. Instalovaná konfigurace hlásí `EnableHeartbeat=false`; živá URL ani skutečný ping/ack nebyly použity. |
| Tajemství | **Částečně ověřeno** | Protected-secret tests ověřují syntetický CurrentUser DPAPI a hranice souborů; tajemství v argumentech se neukládala. Nebyl proveden provisioning přesně pod skutečnou identitou `NT SERVICE\HostAgent` ani restart/čtení takto zašifrovaného tajemství. |
| Windows služby a ukončení/obnova | **Částečně ověřeno** | Čerstvý `Get-Service` ukázal HostAgent i HostAgent.Helper Running/Automatic. `sc.exe qc`: monitor běží jako `NT SERVICE\HostAgent`, helper jako LocalSystem. `sc.exe qfailure`: oba mají dva restart pokusy po 10 s, reset failure count za 86400 s. Graceful stop a recovery testy v sadě jsou hostované/in-process; skutečný stop/pád a SCM restart nebyly vyvolány. |
| CLI a lokální instalace | **Částečně ověřeno** | Nainstalovaný CLI `--help` vrátil exit 0; status/incidents/20 detailů přes UAC/pipe exit 0. Instalace verze 1.0.4 proběhla dříve ve stejné DEV VM; v tomto auditu byly čerstvě ověřeny služby a CLI chování. MSI repair/upgrade/uninstall nebyly znovu spuštěny. |

## Potvrzené chyby a omezení

### P2 – zdravotní stav se může trvale hlásit jako degraded kvůli očekávané částečné telemetrii

`WindowsDiskCollector` přidá `smart_health_unavailable` při každém cyklu i když jsou kapacita a I/O v pořádku; vrátí proto `CollectorStatus.Partial` (`src/HostAgent.Infrastructure/Collectors/WindowsDiskCollector.cs`). `CollectorCycleRunner` zachová stav jako `CollectorCycleStatus.Partial`. `CollectionCoordinator` nastaví `collectors` degraded při libovolném výsledku, který není `Succeeded` (`src/HostAgent.Service/CollectionCoordinator.cs`). `AdminDispatcher` poskytuje CLI pouze boolean `degraded`, bez komponenty nebo důvodu (`src/HostAgent.Service/AdminDispatcher.cs`). Na nainstalované VM `status` skutečně vrátil `degraded: true`.

To znamená, že na systému, kde je SMART očekávaně nedostupný, může monitor hlásit degraded nepřetržitě i při zdravém sběru všech povinných metrik. Aktuální živý výstup nedokazuje, že právě SMART je jediným aktivním důvodem; trvalé degraded při každém takovém cyklu ale plyne z implementačního propojení. Dopad: operátor nepozná, zda došlo k reálné chybě, a heartbeat/monitoring může být zbytečně klasifikován jako unhealthy.

Reprodukce bez změny konfigurace: v DEV VM spusťte několik sběrných cyklů na VM, kde SMART není podporováno; ověřte partial warning `smart_health_unavailable`, poté `HostAgent.Cli.exe status`. Přítomnost jediného partial výsledku nastaví boolean degraded. Ověřovací test `Disk_collector_reports_capacity_and_treats_optional_io_as_partial` potvrzuje partial výsledek; propojení health flagu je v uvedeném zdroji.

### P2 – první Event Log scan nepokrývá deklarované 24hodinové bugcheck okno

`DiagnosticEngine` přijímá bugcheck pouze v 24 hodinách (`src/HostAgent.Core/Diagnostics/DiagnosticEngine.cs`, `EventCorrelationWindow`). `WindowsEventLogCollector` však bez uloženého checkpointu spustí `InitialEventQuery` jen za posledních 10 minut (`src/HostAgent.Infrastructure/Collectors/WindowsEventLogCollector.cs`, `InitialScanWindow`). Událost 1001, která vznikla při pádu před startem služby a je při prvním sběru starší než 10 minut, ale mladší než 24 hodin, se do této první evidence nedostane. Potom již může být mimo uložený checkpoint a diagnóza se nemusí vytvořit.

Jde o potvrzenou mezeru mezi dvěma implementovanými časovými okny; na skutečně nalezených třech WER událostech nebyl zjištěn jejich checkpoint ani okamžik prvního sběru, proto nelze tvrdit, že právě tyto záznamy aplikace ztratila.

Reprodukce podle současného filtru: začněte bez Event Log checkpointu; vložte do fake reader WER 1001 s časem `now - 11 min` a úrovní Error; spusťte první sběr. `InitialEventQuery` takovou událost výslovně vyloučí, přestože `DiagnosticEngine` by ji při dodání zahrnul do 24hodinového okna. Test-first oprava by měla pokrýt pouze relevantní bugcheck metadata v 24 h, ne hromadně ukládat všechny systémové události.

### Podezření – recentní WER události zatím nejsou vidět mezi nahlášenými bugcheck incidenty

Read-only `Get-WinEvent` dotaz našel tři události provideru `Microsoft-Windows-WER-SystemErrorReporting`, ID 1001, za posledních 24 hodin. Elevated `incidents` vrátil 20 incidentů; read-only detail všech 20 proběhl úspěšně, ale žádný z jejich root-cause kódů nebyl bugcheck. Protože CLI stránkuje pouze 20 nejnovějších a nebyl měněn checkpoint ani proveden read-only export odpovídajících Event Log metadat vedle incident historie, příčina není potvrzena. Možnosti zahrnují 10minutový první scan, starší checkpoint, pořadí více než 20 novějších incidentů nebo událost bez validních strukturovaných polí.

Bezpečný další diagnostický krok: porovnat pouze čas, EventRecordID a parser výsledek WER záznamů se sběrnými checkpointy a uloženými diagnózami. Nesmazávat logy ani checkpointy a nespouštět simulovaný pád.

## Bezpečnostní zjištění

- Čerstvé testy a read-only instalovaný pipe potvrzují uzavřený lokální administrační/protokolový tok v této VM. Služba monitoru běží pod virtual service account; helper je LocalSystem. Nebyl nalezen generic shell/process execution capability; helper testy používají explicitní typed allowlist.
- Shadow/read-only jsou v instalaci aktivní. Detail každého incidentu explicitně odmítá unattended opravu. Žádná operace helperu, změna služby, procesní terminace, secret provisioning, externí síťové doručení ani host zásah nebyly provedeny.
- Závislostní audit nenašel známý zranitelný NuGet balíček. Heuristický tracked-file secret scan našel 0 kandidátů. To nenahrazuje fuzzing parserů, statický bezpečnostní scanner, analýzu nativních artefaktů ani reálný secrets review podepsaného release.
- Reálné rozdílné účty, produkční service SID/DPAPI, cross-account pipe ACL, SYSTEM bootstrap a nativní helper mutace nejsou ověřeny. Známé bezpečnostní review z Task 2–4 a V1 jsou historické source reviews; nenahrazují tento audit spuštěného produkčního deploymentu.
- Recovery konfigurace je dvakrát restart po 10 sekundách s resetem počítadla po 24 hodinách; reálný pád služby a následný SCM recovery nebyly testovány. Třetí selhání tedy nelze prohlásit za bezpečně obnovené podle živého testu.
- VM release je nesignovaný `-DevVm` kandidát. Produkční Smart App Control/Code Integrity kompatibilita, podpis a instalace nejsou ověřeny. Žádný host policy bypass nebyl použit.

## Prioritizované další kroky

1. **Opravit a test-first ověřit bugcheck startup lookback** tak, aby první start služby zachytil relevantní timestampované WER 1001 v deklarovaném 24h okně bez ukládání raw XML nebo bulk logů. Přidat reprodukční test pro event starý 11 minut a přesné hraniční testy.
2. **Opravit health model**: oddělit očekávanou nepovinnou telemetrii (SMART) od selhání povinného collectoru; zveřejnit komponentu a pevný health code místo neprůhledného booleanu. Přidat test, že normální SMART unavailable nezanechá systém trvale degraded.
3. **Dokončit read-only diagnostiku konkrétního restartu**: bezpečně korelovat tři již pozorované WER 1001 časy/record IDs s checkpointy a durable incidenty; pokud existuje odpovídající dump, analyzovat ho WinDbg `!analyze -v`. Neprovádět změnu/rollback ovladače bez atribučního důkazu a schváleného cíle.
4. **Ověřit dlouhodobý sběr a zotavení** v DEV VM: několikahodinový soak, restart služby řízeným stop/start, následný boot a kontrola, že intervaly, checkpointy, SQLite a incident deduplikace pokračují. Reálný crash SCM recovery testovat pouze izolovaně ve VM a bez host policy změn.
5. **Ověřit provozní identitu a tajemství** pomocí syntetických testovacích hodnot: provision přes admin CLI, ověřit DPAPI read pod `NT SERVICE\HostAgent`, restart služby, ověřit načtení bez zobrazení hodnoty; potom testovací tajemství odstranit podporovanou cestou. Produkční tajemství ani živé endpointy nepoužívat.
6. **Ověřit helper fail-closed cestu** na neškodném vlastním testovacím targetu nebo pouze fake executorem: policy DENIED/kill switch, stale approval, mismatched digest, timeouts a audit. V tomto auditu žádná privilegovaná operace nebyla provedena.
7. **Před produkčním použitím** vytvořit trusted code-signing release, ověřit manifest a podpis na cílové politice bez jejího oslabování, ověřit instalaci/upgrade/rollback a připravit zálohu/obnovu ProgramData. Do té doby zůstat u DEV VM unsigned balíčků.
8. Teprve po těchto gatech zvážit živé Pushover/Healthchecks endpointy s jednorázovými testovacími credentials, timeouts, odvoláním secretů a potvrzením, že payloady neobsahují osobní údaje.

## Doporučená rozšíření

| Možnost | Přínos | Náročnost | Dopad / opatření |
|---|---|---|---|
| `status --verbose` s pevnými health codes pro jednotlivé workery/collectory | Operátor vidí, proč je stav degraded a co opravit | Nízká až střední | Povolené kódy bez výjimek, payloadů, cest či tajemství; zachovat limit velikosti pipe |
| Diagnostický export incidentu s redakcí a manifestem | Snadnější nezávislý rozbor supportem | Střední | Opt-in, pouze vybraná metadata, žádné raw event XML, uživatelská jména, secret hodnoty ani plné procesní command line; hash manifestu |
| Bezpečné ověření minidumpu a návodu WinDbg | Rychlejší přechod od stop code ke skutečnému ovladači | Střední až vysoká | Neodesílat dump do cloudu; lokální read-only inspekce, jasně oddělit fakt od heuristiky; bez automatické změny driveru |
| Reálná monitorovací dashboard/UI pouze lokálně | Přístupnější incidenty a schválení | Vysoká | Nový attack surface; preferovat authenticated local pipe/UI, žádný public listener; mimo aktuální V1 specifikaci |
| Podporovaný SMART provider za feature capability | Zdraví disku s doloženým hardware API | Střední až vysoká | Vendor/model variabilita, privilegia a falešně negativní výsledky; unavailable nesmí znamenat selhání povinného sběru |
| Alert na ztrátu heartbeat a lokální stav doručení | Upozornění na vypnutí hostu nebo ztrátu internetu | Střední | Vyžaduje testovací účet; metadata minimální, URL jako secret, jasně vymezit co externí watchdog skutečně pozná |
| Verifikované backup/restore a export auditů | Obnova po poškození disku a lepší forenzní kontinuita | Střední | Konzistentní SQLite backup, immutable audit export, přístup pouze správci, žádné automatické mazání auditů |
| Kalibrace confidence na označených scénářích | Lepší interpretace skóre uživatelem | Střední | Datový soubor bez citlivých logů, report precision/false positive; confidence neprezentovat jako pravděpodobnost příčiny bez kalibrace |

## Co tento audit neudělal

Nezměnil zdrojové soubory, konfiguraci služeb, secret store, databáze ani checkpointy; nespustil proposal/approval/execution; nepoužil skutečné tajemství nebo externí doručení; nevyvolal pád, restart služby ani hostitele; neupravoval ani nepřenášel host projekt. Report je jediný zamýšlený nový tracked soubor. Žádný commit nebyl vytvořen.
