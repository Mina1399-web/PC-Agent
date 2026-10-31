# Host Agent 1.2.9 — výkon v DEV VM

Měření proběhlo 31. 10. 2026 v odsouhlasené vývojové VM. Report popisuje
pozorované vzorky a nevyvozuje z nich spotřebu hlavního PC ani připravenost pro
produkční nasazení.

## Prostředí a procesy

- VM `HostAgent-DEV-Rebuild`, UUID
  `0bb543d1-7bb0-43ed-9c94-596d63ef3310`, guest `AGENTPC`, účet
  `AgentPC\vmdev`, Windows 11 Pro build 26300.
- 4 vCPU; viditelná fyzická RAM VM byla po celou dobu 4 076,88 MiB.
- Nainstalovaná verze Host Agentu: 1.2.9.
- `HostAgent.Service`: PID 3744; `HostAgent.Helper`: PID 3332 po všechny fáze.
  Dashboard `HostAgent.Desktop`: PID 1912 pouze během fáze s UI.
- Obě služby měly ve všech vzorcích stav `Running`. Dashboard byl před dlouhým
  během zavřený. Konfigurace aplikace ani bezpečnostní nastavení se neměnily.

## Metodika

Každá fáze začala úvodním vzorkem bez CPU delta a následně ukládala vzorek zhruba
každých 15 sekund. Konečný soubor obsahuje 187 datových řádků se všemi raw
čítači a vypočtenými hodnotami: [surové CSV](evidence/performance-2026-10-31.csv).
SHA-256 CSV: `DA62F45512392EFD6F54FCDA6BE5E09D10828E70A2790BDEC2D93D18689A3D32`.
Mezilehlé kopie byly ukládány do evidenční složky na hostu i přímo v dočasné
složce VM; konečný soubor byl zkopírován po dokončení běhu.

Working set procesů je převzat z `Win32_PerfRawData_PerfProc_Process.WorkingSet`
a zobrazen v MiB. CPU procesu je přepočítané z raw čítačů
`PercentProcessorTime` a `Timestamp_Sys100NS` pro daný PID:

`CPU % kapacity 4 vCPU = 100 × ΔCPU_raw / ΔTimestamp_raw / 4`.

Tím se vyjadřuje podíl na celé kapacitě VM; není to procento jednoho jádra.
Čítače jsou po každé fázi ověřeny zpětným výpočtem. Popis raw čítačů a vzorce
je v [dokumentaci Microsoftu k raw procesorovým čítačům](https://learn.microsoft.com/en-us/previous-versions/aa394323%28v%3Dvs.85%29).

Celková RAM VM je samostatné měření z `Win32_OperatingSystem`:
`Used = TotalVisibleMemorySize − FreePhysicalMemory`. Zahrnuje Windows i jiné
procesy v guestu a také malou režii měřicího PowerShell procesu. Working set
řádky Host Agentu zahrnují jen výslovně uvedené produktové procesy. Codex a
ChatGPT běžely na hostiteli mimo guest a nejsou započítané do procesní spotřeby
Host Agentu.

Systémové CPU VM se z raw `_Total` čítače uchovává v CSV pouze pro kontrolu.
Výpočet odpovídal raw hodnotám, ale tři vzorky během dlouhého běhu byly záporné;
proto nejsou použity pro závěr o celkové zátěži VM. Tento výsledek nepřisuzuji
Host Agentu. Vzorec systémového čítače je uveden v
[dokumentaci Microsoftu k raw WMI CPU čítačům](https://learn.microsoft.com/en-us/windows/win32/wmisdk/wmi-tasks--performance-monitoring).

## Fáze

Všechny časy jsou UTC. Interval uvádí medián a rozsah skutečných rozestupů mezi
vzorky; počet vzorků zahrnuje úvodní základní vzorek.

| Fáze | Čas od–do UTC | Doba | Vzorky | Interval median (rozsah) |
|---|---|---:|---:|---:|
| Nečinnost bez UI (`idle_no_ui`) | 11:26:59.690–11:32:02.120 | 302,4 s | 21 | 15,118 s (15,106–15,236) |
| Běžné sběrné cykly (`active_collection_cycles`) | 11:32:38.372–11:37:40.793 | 302,4 s | 21 | 15,118 s (15,106–15,270) |
| Dashboard otevřen (`dashboard_open`) | 11:38:23.836–11:43:31.835 | 308,0 s | 21 | 15,114 s (15,105–20,852) |
| Dlouhý běh bez UI (`sustained_no_ui`) | 11:44:09.325–12:15:19.854 | 1 870,4 s (31 min 10 s) | 124 | 15,116 s (11,225–20,535) |

V průběhu sběrné fáze zapsal Application log 15 událostí `HostAgent.Service`,
Event ID 0, úroveň Warning (tři přibližně jednou za minutu). Jejich zprávy byly
prázdné, proto je používám pouze jako časovou stopu periodické aktivity, ne jako
důkaz úspěchu každé sběrné operace.

## Working set a CPU procesů

Paměť je uvedena jako průměr (minimum–maximum) v MiB. CPU je průměr / maximum v
procentech celkové kapacity 4 vCPU; průměry zahrnují pouze měřené intervaly po
úvodním vzorku.

| Fáze | Service working set | Service CPU | Helper working set | Helper CPU | Desktop working set | Desktop CPU |
|---|---:|---:|---:|---:|---:|---:|
| Nečinnost bez UI | 213,38 (170,84–280,32) | 0,473 / 1,964 % | 43,40 (43,21–43,69) | 0,005 / 0,052 % | neběžel | — |
| Běžné sběrné cykly | 238,02 (181,55–281,64) | 0,552 / 1,991 % | 43,26 (43,18–43,32) | 0,009 / 0,078 % | neběžel | — |
| Dashboard otevřen | 239,42 (197,06–292,68) | 0,556 / 2,169 % | 43,33 (43,29–43,40) | 0,000 / 0,000 % | 154,02 (132,06–165,47) | 0,627 / 3,420 % |
| Dlouhý běh bez UI | 224,70 (160,82–295,57) | 0,531 / 2,505 % | 43,51 (43,09–43,73) | 0,004 / 0,053 % | neběžel | — |

## Celková využitá a volná RAM VM

Celková viditelná RAM byla ve všech vzorcích 4 076,88 MiB. Následující údaje
jsou z VM jako celku, nikoli součty pracovních sad Host Agentu. Hodnoty jsou
průměr (minimum–maximum) v MiB.

| Fáze | Využitá RAM VM | Volná RAM VM |
|---|---:|---:|
| Nečinnost bez UI | 2 144,30 (2 108,31–2 215,90) | 1 932,58 (1 860,98–1 968,57) |
| Běžné sběrné cykly | 2 167,78 (2 112,99–2 211,31) | 1 909,10 (1 865,57–1 963,89) |
| Dashboard otevřen | 2 318,01 (2 266,07–2 387,22) | 1 758,87 (1 689,66–1 810,81) |
| Dlouhý běh bez UI | 2 196,68 (2 122,34–2 283,12) | 1 880,20 (1 793,75–1 954,54) |

## Ověření a limity

- CSV validace: 27 sloupců, 187 vzorků, všechny služby `Running`, PID služby a
  Helperu stabilní, dashboard přítomen jen ve fázi UI. V každém řádku platilo
  `used + free = total` s odchylkou menší než 0,1 MiB.
- Všechny vypočtené CPU hodnoty tří produktových procesů odpovídaly nezávislému
  přepočtu raw counterů a byly v rozsahu 0–100 % kapacity 4 vCPU.
- V approved DEV VM proběhl cílený test:
  `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsCollectorIntegrationTests.Cpu_memory_collector_returns_bounded_host_memory_and_process_values|FullyQualifiedName~ProcessMetricFailureTests|FullyQualifiedName~ProcessReadStageTests" --logger "console;verbosity=minimal"`
  na čistém checkoutu `PC-Agent-expanded-release`, commit `1515ec1`, se SDK
  10.0.401. Výsledek: 31 úspěšných, 0 neúspěšných, 0 přeskočených; testovací
  build uspěl. Produktový zdroj `src/` tohoto commitu odpovídá aktuálnímu
  checkoutu. Zpracování reportu a CSV neprovádělo produktový build; všechny
  .NET build/test příkazy běžely uvnitř DEV VM.
- Měření neprokazuje spotřebu celého PC, přesné sdílení sdílených paměťových
  stránek, nepřítomnost paměťového úniku ani chování po mnohahodinovém běhu.
  Working set je provozní rezidentní paměť, nikoli Private Bytes.
- Nedocházelo ke změně konfigurace Host Agentu, režimu oprav, aktivních síťových
  testů ani bezpečnostních nastavení. Na hlavní PC se nic nenasazovalo.
