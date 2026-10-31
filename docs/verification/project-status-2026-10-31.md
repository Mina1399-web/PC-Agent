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
- Nový kód 1.2.9 je lokálně commitnutý v pracovní větvi. **Na GitHub pushnutý
  není.** Instalační balíček 1.2.9 zůstává místním DEV artefaktem.
- Pracovní strom obsahuje necommitnutý ověřovací záznam upozornění a tento
  report. Nic z toho zatím nebylo odesláno na GitHub.
- Chybí ověřený běh zotavení po výpadku heartbeat, měření dlouhodobé spotřeby
  RAM/CPU a ověření SMART na skutečném fyzickém disku.

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
| Spotřeba paměti a výkon | Nedostatečně změřeno | Jeden starší okamžitý vzorek služeb: Service 195,3 MiB working set / 166,6 MiB private; Helper 41,5 / 9,4 MiB. Bez desktopu a bez srovnávacího či dlouhodobého měření. Nelze z něj vyvodit běžnou celkovou spotřebu PC. |
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
- **Poslední release:** [1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1),
  označený jako prerelease. Obsahuje MSI, ZIP, manifest a SHA256SUMS.
  Je výslovně nepodepsaný a určený pouze pro DEV VM; nejde o produkční
  vydání.
- Poslední načtený hlavní review workflow na `b5226143` byl zelený. Historie
  obsahuje také starší neúspěšné běhy; neznamenají, že selhal poslední běh.
  Pro PR #3 zde úspěšný běh potvrzen nebyl.
- GitHub tag/release 1.2.9 ani publikovaný balíček nových funkcí nebyly
  nalezeny.

## Co je commitnuté lokálně a co čeká

Místní checkout je větev `codex/expanded-local-maintenance`, remote
`origin` míří na výše uvedený repozitář.

| Commit / změna | Místní stav | GitHub stav |
|---|---|---|
| `1515ec1` — rozšířená potvrzovaná údržba a predikce selhání disku | Commitnutý; zdroj 1.2.9, instalován v DEV VM | Na GitHub neposlaný |
| `dc32681` — záznam instalace a readbacku 1.2.9 | Commitnutý; lokální důkazy a dokumentace | Na GitHub neposlaný |
| `ab12a5b` — důkaz automatického upozornění a tento stavový report | Commitnuté ověřovací dokumenty | Na GitHub zatím neposlané |

Po zachování původních SHA jsem vytvořil merge commit `e096117`, který
připojuje aktuální vzdálenou větev `b5226143` k místním commitům. Graf
zachovává `1515ec1`, `dc32681` a `ab12a5b`. Větev
`codex/expanded-local-maintenance` zatím nebyla pushnuta; PR pro 1.2.9
má cílit na `codex/signpath-submission-prep`, nikoli na `main`.

Build 1.2.9, MSI, manifest a ZIP existují jako ověřené místní DEV artefakty
v evidenční složce pracovní kopie. Nejsou GitHub release assety a nebyly
spuštěny na hlavním PC.

## Co zbývá a doporučené pořadí

1. **GitHub PR:** pushnout zachovanou větev a otevřít PR proti
   `codex/signpath-submission-prep`; Windows CI pro tento rozsah zatím neběží.
   Balíček 1.2.9 nevydávat jako hotový produkční release.
2. **Provozní testy:** provést řízený test selhání a zotavení heartbeat a
   ověřit doručení i stav v Healthchecks.
3. **Výkon:** zaznamenat reprezentativní RAM/CPU při nečinnosti, sběru,
   otevřeném dashboardu a delším běhu; odlišit working set jednotlivých
   procesů od celkové fyzické RAM.
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
- README ještě chybně uvádí, že žádost SignPath nebyla podána. Majitel
  uvedl, že ji odeslal 27. 10. a čeká na odpověď. To nepotvrzuje přijetí
  do programu ani podpis; veřejný text je potřeba opravit v tomto PR.

Testy, hardware a CI se zde nepovažují za ověřené nad rámec uvedených
podkladů. Tento report je lokální pracovní dokument, nikoli GitHub commit,
release, podpis ani produkční schválení. GitHub byl naposledy načten
31. 10. 2026 před otevřením PR pro tyto změny.
