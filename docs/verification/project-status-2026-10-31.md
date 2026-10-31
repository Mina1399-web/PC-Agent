# Host Agent — stav projektu a produktu

**Stav ověřen k 31. 10. 2026.** Report porovnává místní zdrojový checkout,
GitHub, vydání a ověřovací záznamy z běžící DEV VM. Funkčnost v DEV VM
neznamená připravenost pro běžné nasazení ani podpis produktu.

## Rychlý přehled

- V DEV VM běží **HostAgent 1.2.9**. Instalace, základní dashboard/IPC,
  síťové sondy a potvrzený test automatického incidentního upozornění byly
  ověřeny. Pushover doručil syntetické varování do telefonu.
- Poslední GitHub release je **nepodepsaný předběžný Host Agent 1.2.8**.
  Vydaná větev neobsahuje říjnové změny údržby, SMART ani aktuální nastavení
  Pushover/Healthchecks z DEV VM.
- Kód 1.2.9 a ověřovací reporty jsou commitnuté a pushnuté v otevřeném PR #4
  proti `codex/signpath-submission-prep`. Windows CI pro hlavu `d39cbd8`
  prošlo; PR ještě není sloučený. Instalační balíček 1.2.9 zůstává místním
  DEV artefaktem.
- Ověřovací záznam upozornění i tento report jsou součástí PR #4; pracovní
  strom je čistý.
- Chybí řízený test zotavení heartbeat po výpadku a ověření SMART na skutečném
  podporovaném fyzickém disku. Výkon Host Agentu už má srovnávací měření v DEV
  VM; podrobnosti a limity jsou v [samostatném reportu](performance-2026-10-31.md).

## Stav funkcí produktu

| Oblast | Stav | Co bylo prokázáno / co zbývá |
|---|---|---|
| Windows služba a pomocná služba | Funkční v DEV VM | Po instalaci 1.2.9 obě služby běžely a po následném čerstvém testu stále běžely. Produkční instalace není ověřena. |
| Dashboard a lokální IPC | Základ ověřen | Nainstalovaný autentizovaný View pipe vrátil aktuální overview. To nepotvrzuje ruční průchod všemi obrazovkami a všemi jazykovými kombinacemi. |
| Čeština, angličtina, němčina | Implementováno a GitHub CI prošlo | Preference jazyka se ukládá pro uživatele; UI texty a telemetrická vysvětlení mají překlady. PR #2 je sloučen. |
| Sběr CPU/RAM a procesů | Částečně funkční | Systémová telemetrie a overview fungovaly. Windows odmítl přístup k části procesních CPU metrik a cest; tyto sběrače proto hlásí částečný stav. |
| Zdravotní stav disků / SMART | Omezené | Tlačítko používá pouze čtecí `IOCTL_STORAGE_PREDICT_FAILURE`; testovací UI zná i stavy „nedostupné“, „selhání předpovězeno“ a „bez předpovězeného selhání“. Ve VM nebyla predikce dostupná; skutečný fyzický disk ověřen nebyl. Teplota, opotřebení NVMe, vendor atributy a samočinné testy nejsou součástí této funkce. |
| Aktivní síťové sondy | Zapnuté a otestované v DEV VM | Proběhla i výslovně povolená omezená živá síťová testovací cesta. Výsledek nepokrývá každou síť ani všechny typy výpadků. |
| Návrhy a opravy | Dostupné v režimu Confirmation | Režim je nastavený na Confirmation; oprava stále vyžaduje konkrétní podporovanou operaci a schválení. Automatické opravy bez obsluhy nejsou ověřené ani povolené. |
| Údržba běžných dočasných souborů | Implementováno a testováno | Náhled, karanténa a obnova s kontrolou cíle a integrity. Mutující testy používaly jen syntetické soubory. |
| Ručně vybraná složka | Implementováno, instalováno v 1.2.9 | Prohledává přímé soubory ve vybrané uživatelské složce; není to rekurzivní úklid systému. Zachovává kontrolu náhledu, identity, vlastníka, karantény a omezení rozsahu. |
| Trvalé smazání z karantény | Implementováno, testováno na syntetickém souboru | Vyžaduje přesné `DELETE <ID>`, kontroluje identitu/hash a zapisuje trvalý audit. Neexistuje automatické promazávání. Smazaný obsah aplikace neobnoví. |
| Automatické Pushover upozornění | Ověřeno end-to-end v DEV VM | Nainstalovaná služba sama zpracovala syntetický varovný incident: `push_sent`, jeden pokus, bez opakování během dalšího intervalu. Uživatel doložil doručení do telefonu. Jde o testovací incident, ne o simulaci detekce reálné poruchy. |
| Healthchecks heartbeat | Zapnutý; základní odeslání prošlo | Při nastavování služba úspěšně zavolala uživatelův endpoint. Chování při výpadku a následném zotavení ještě otestováno není. Stav může být červený kvůli částečným procesním metrikám. |
| Spotřeba paměti a výkon | Změřeno v DEV VM | Čtyři srovnávací fáze: nečinnost bez UI, běžné sběrné cykly, otevřený dashboard a 31minutový běh. Průměrný working set Service/Helper: 213,38/43,40 MiB při nečinnosti; 239,42/43,33 MiB s dashboardem. Celková VM RAM se vykazuje zvlášť. Raw data, CPU metodika a omezení jsou v [reportu výkonu](performance-2026-10-31.md). Nejde o měření celého hlavního PC ani o důkaz absence paměťového úniku. |
| Podpis a běžné produkční nasazení | Nedokončeno | Balíček 1.2.9 je nepodepsaný DEV build. Není potvrzená důvěryhodná podpisová cesta, instalace na hlavní počítač ani produkční přijetí. |

## Co je na GitHubu

Repozitář: [Mina1399-web/PC-Agent](https://github.com/Mina1399-web/PC-Agent).
GitHub API při kontrole uvádělo výchozí větev
`codex/signpath-submission-prep`, aktuální vzdálenou hlavu
`b5226143f05c55e2adb2d06f2c1627b61a9d2edd`.

- **PR #1 — sloučen:** souhrn zdraví dashboardu, vysvětlení telemetrie a
  aktualizace kontrol DEV VM. Jeho review workflow proběhlo úspěšně.
- **PR #2 — sloučen:** trvalý výběr jazyka čeština/angličtina/němčina.
  Windows review build na výsledném commitu `b5226143` skončil úspěšně
  30. 10. 2026.
- **PR #3 — uzavřený jako zastaralý, nesloučený:** cílová větev už obsahuje
  společnou funkci `Get-SubmissionOwnedSigningCandidates`, která ze seznamu
  pro podpis vynechává `*.resources.dll` a satelitní soubory ponechává
  v balíčku. PR #3 zkoušel vložit duplicitní starší filtr do dvou skriptů;
  GitHub u něj vracel `mergeable: false` a nebyl k dispozici Windows check.
  Přidal jsem vysvětlující komentář a PR zavřel bez mergování.
- **PR #4 — otevřený, CI zelené:** [Expand confirmed file maintenance and
  disk failure prediction](https://github.com/Mina1399-web/PC-Agent/pull/4)
  posílá zachované lokální commity 1.2.9 a reporty proti aktuální větvi
  `codex/signpath-submission-prep`; GitHub hlásí `mergeable: true`. PR nebyl
  sloučen.
- **Poslední release:** [1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1),
  označený jako prerelease. Obsahuje MSI, ZIP, manifest a SHA256SUMS.
  Je výslovně nepodepsaný a určený pouze pro DEV VM; nejde o produkční
  vydání.
- Windows review workflow #19 pro PR #4, commit
  `d39cbd8215c349e3035dadff397097832af56743`, skončil úspěšně 31. 10. 2026:
  release build 0 warningů/chyb; Core 62/62, Infrastructure 455/455,
  Desktop 47/47 (564 testů); také submission regression checks prošly.
  Úspěch tohoto workflow se týká PR #4; PR #3 zůstává zavřený bez merge.
- GitHub tag/release 1.2.9 ani publikovaný balíček nových funkcí nebyly
  nalezeny.

## Co je commitnuté lokálně a co čeká

Místní checkout je větev `codex/expanded-local-maintenance`, remote
`origin` míří na výše uvedený repozitář.

| Commit / změna | Místní stav | GitHub stav |
|---|---|---|
| `1515ec1` — rozšířená potvrzovaná údržba a predikce selhání disku | Commitnutý; zdroj 1.2.9, instalován v DEV VM | Součást PR #4; nesloučeno |
| `dc32681` — záznam instalace a readbacku 1.2.9 | Commitnutý; lokální důkazy a dokumentace | Součást PR #4; nesloučeno |
| `ab12a5b` — důkaz automatického upozornění a stavový report | Commitnuté ověřovací dokumenty | Součást PR #4; nesloučeno |
| `d39cbd8` — prodloužení čekání v časově citlivém CI testu | Commitnutá testovací stabilizace | Součást hlavy PR #4; Windows CI prošlo |

Po zachování původních SHA jsem vytvořil merge commit `e096117`, který
připojuje aktuální vzdálenou větev `b5226143` k místním commitům. Graf
zachovává `1515ec1`, `dc32681` a `ab12a5b`; následuje commit `d39cbd8`.
Větev `codex/expanded-local-maintenance` je pushnutá a otevřená jako PR #4
proti `codex/signpath-submission-prep`, nikoli proti `main`.

Build 1.2.9, MSI, manifest a ZIP existují jako ověřené místní DEV artefakty
v evidenční složce pracovní kopie. Nejsou GitHub release assety a nebyly
spuštěny na hlavním PC.

## Co zbývá a doporučené pořadí

1. **GitHub PR:** PR #4 a Windows CI jsou dokončené; zbývá lidská revize a
   případné sloučení do cílové vývojové větve. PR zatím nebyl sloučen.
   Balíček 1.2.9 nevydávat jako hotový produkční release.
2. **Provozní testy:** provést řízený test selhání a zotavení heartbeat a
   ověřit doručení i stav v Healthchecks.
3. **Výkon — dokončeno v DEV VM:** proběhly srovnávací fáze nečinnosti, běžného
   sběru, otevřeného dashboardu a 31minutového běhu. [Naměřené výsledky,
   surové vzorky a validační kontroly](performance-2026-10-31.md).
4. **SMART:** vyzkoušet čtecí předpověď na skutečném podporovaném fyzickém
   disku; pokud hardware/API údaj neposkytne, zobrazit tuto schopnost jako
   nepodporovanou, nikoli jako potvrzené zdraví.
5. **Podpis/nasazení:** počkat na odpověď SignPath podle dosavadní informace
   majitele, dokončit podpisovou cestu a až potom posuzovat přesun na hlavní
   počítač.

## Ověřovací podklady a limity

- [Rozšířené funkce, testy a instalace 1.2.9](expanded-features-2026-10-30.md)
  uvádějí build bez varování a 685/685 testů v DEV VM.
- [Automatické incidentní upozornění](automatic-notification-2026-10-30.md)
  zaznamenává jeden automatický pokus, žádné opakování v dalším cyklu,
  uzavřený syntetický incident a doložené doručení do telefonu.
- [Rozšířená údržba](../operations/expanded-maintenance.md) popisuje
  přesné limity karantény, mazání a SMART rozhraní.
- README nyní zaznamenává majitelem nahlášené podání SignPath 27. 10. a čekání
  na odpověď. To nepotvrzuje přijetí do programu ani podpis.

Testy, hardware a CI se zde nepovažují za ověřené nad rámec uvedených
podkladů. Tento report je součástí otevřeného PR #4; není to release, podpis
ani produkční schválení. GitHub byl ověřen 31. 10. 2026 po úspěšném Windows
review workflow #19.
