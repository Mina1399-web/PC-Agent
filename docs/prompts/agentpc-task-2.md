# Pokračovací prompt pro Codex v AgentPCtestdev

Pokračuj v projektu Host Agent V1 a dokonči **Task 2**. Pracuj autonomně podle aktuálního stavu repozitáře a dokumentace. **Task 3 nezačínej.**

## Nejdřív ověř prostředí a aktuální stav

1. Ověř skutečný počítač, přihlášený účet, pracovní adresář a umístění checkoutu. Potvrď, že běh Codexu a všechny příkazy budou v samostatné Windows VM `HostAgent-DEV` / účtu DEV na virtuálním disku. Název připojeného zařízení ani vzdálená obrazovka samy o sobě nejsou důkaz. Pokud VM prostředí nelze spolehlivě potvrdit, nespouštěj build/test a popiš blocker.
2. Zachovej snapshot `clean-win-base`; nikdy ho neupravuj ani nepřepisuj. Zjisti skutečný aktuální snapshot VM, checkout, větev, Git stav a všechny necommitnuté změny. Nic nepřepisuj naslepo.
3. Přečti aktuální verze specifikace, vývojové/testovací dokumentace, implementačního plánu a Task 2 verification record:
   - `docs/specs/host-agent-v1.md`
   - `docs/development-and-testing.md`
   - `docs/plans/2026-10-06-host-agent-v1.md`
   - `docs/verification/task-2.md`
4. Ověř dostupnost připnutého .NET SDK a testovacího hostu v guestu. Instaluj jen nástroje nutné k buildu a testování, pouze do DEV prostředí. Neměň bezpečnostní zásady hostitele ani VM.

## Test-first postup

Nejdřív bez změny produkčního kódu spusť a zaznamenej focused Release testy pro `BaselineEngine` a `DiagnosticEngine`. Používej skutečný aktuální kód a stav testů. Pokud se očekávané regresní testy nenacházejí v repozitáři, doplň je jako první, potvrď jejich očekávané selhání a teprve potom oprav implementaci.

Vyřeš všechny tři otevřené review nálezy:

1. Service Control Manager crash events (včetně 7031/7034) musí být omezené na aktuální 24hodinové correlation window.
2. Service-crash diagnosis musí určit jednoznačnou službu jako affected subject a používat stabilní, jednoznačný correlation key. Události s chybějícím `RecordId` nesmějí slučovat různé služby nebo nesouvisející události. Přidej regresní testy pro shodné i chybějící RecordId.
3. Disk evidence bez timestampu se nesmí považovat za recent nebo contemporaneous. Doplň regresní test s přesným numerickým confidence assertion a ověř, že chybějící čas nevytváří sekundární korelační symptom.

Potom dokonči celý zbývající rozsah Task 2:

- `IncidentEngine` a podporovaný incident lifecycle;
- deduplikaci incidentů a update existujícího incidentu během opakovaných sběrných cyklů;
- trvalé incident snapshots;
- obnovení a persistenci baseline;
- wiring přes `CollectionCoordinator`;
- chybějící unit a integration testy, včetně přesných confidence assertions;
- storage snapshot a retention integration včetně ověření zachování/odstranění očekávaných záznamů.

Po každé změně spusť příslušné focused testy v Release konfiguraci. Nakonec spusť celý Release build a kompletní test suite:

- `dotnet build HostAgent.sln --configuration Release`
- focused Core testy pro `BaselineEngine`, `DiagnosticEngine` a `IncidentEngine`;
- `dotnet test HostAgent.sln --configuration Release`

Zaznamenej přesné příkazy, prostředí, datum/čas, exit codes, počty testů a skutečné chyby. Build úspěch sám o sobě není důkaz úspěšných testů.

## Přenos a závěrečný report

Task 2 verification record aktualizuj jen podle nových testů, které jsi v této VM opravdu spustil. Zachovej historické výsledky jako historické a neoznačuj je za nové.

Změny vrať do produkčního host projektu až po úspěšném Release buildu i celé test suite v guestu. Před přenosem zachovej ověřitelnou rollback kopii. Přenes jen zkontrolované source/test/docs soubory; nevracej build artefakty, secrets, VM konfiguraci ani DEV cache. Bezpečnostní nastavení hostitele neměň. Pokud některý gate selže, Task 2 ponech neúplný a nepřenášej změny.

Na konci stručně uveď změněné soubory, skutečně ověřené testy a výsledky, přenesené soubory, otevřené problémy a přesný stav Task 2. Výslovně potvrď, že Task 3 nezačal.
