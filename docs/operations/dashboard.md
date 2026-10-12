# Místní přehled Host Agent

Spusťte **Host Agent** z nabídky Start, případně
`C:\Program Files\HostAgent\Desktop\HostAgent.Desktop.exe`. Přehled běží pod
běžným přihlášeným účtem, bez požadavku na správce. Vyžaduje monitor s novým
rozhraním `HostAgent.View.v1`; starší služba zobrazí nedostupnost, nikoli
vymyšlené hodnoty. X ukončí přehled; tlačítko Skrýt do lišty jej nechá běžet.
Ukončení přehledu nezastaví monitorovací služby.

Přehled obnovuje data po 15 sekundách. Aktuální metriky starší než tři minuty
nejsou aktuální stav. Diskové kapacity, deset největších spotřebitelů RAM,
počet aktivních adaptérů a health kódy neodvozují úspěšné připojení k internetu
ani dostupnost SMART. Historie je 24 hodin s průměry po 15 minutách; mezery se
nespojují. Incidenty obsahují dvacet posledních záznamů, detail nejvýše šestnáct
omezených důkazů. Confidence u bugchecku klasifikuje stop kód, nikoli viníka.

**Prověřit problém** čte posledních nejvýše 256 uložených Event Log vzorků
za 24 hodin a ukáže nejvýše šestnáct WER 1001 / 41 / 6008 událostí. Nejde o
nový kompletní scan Windows Event Logu. Chybějící uložený záznam nedokazuje
ztrátu události. Checkpointy se nemění, dumpy se neodesílají ani neanalyzují.

**Doporučení** vycházejí z důkazů: CPU/RAM alespoň 90 % po nejméně čtyři
minuty, volné místo do 10 %, nebo čerstvý nevyřešený bugcheck. Pro doporučení
se používají konzervativní minutová minima; krátká špička nestačí. Každá karta
uvádí přínos, dopad a limity návratu. Instalace náhodného „optimalizátoru“,
automatická změna ovladačů ani mazání osobních dat se nenavrhují.

Údržba otevře konkrétní nastavení Windows: úložiště, aplikace nebo programy
po spuštění. Host Agent zde zatím sám nemaže soubory a neinstaluje balíčky.
OS-managed výběr a případné nevratné smazání musí zkontrolovat operátor.

Pro **ruční zásah** nejdřív vyberte incident a zadejte již registrovaný alias
helperu. Pro proces vyberte řádek v přehledu, který doplní PID a přesný čas
spuštění; pokud čas chybí, akce se odmítne. Dostupné jsou jen start/stop/restart
služby a ukončení konkrétního procesu. Uložte práci před přerušením aplikace.
Tlačítko otevře pouze ověřené instalované CLI přes UAC. CLI ukáže přesný cíl
a digest a vyžádá opsání GUID. UAC samo není schválením operace. Shadow,
read-only, kill switch, neznámý alias, změna identity nebo neplatné schválení
provádění blokují. Dashboard nekonfiguruje allowlist ani nezapíná Confirmation.

**Zablokovat všechny opravy** otevře `kill-switch on` v CLI, které vyžádá
APPLY a restartuje pouze produktové služby. Zachová režim i nastavení
Pushover/heartbeat. Zapnutí musí potvrdit nový stav služby. Opětovné povolení
je samostatná administrátorská konfigurace (`kill-switch off`), nikoli
automatický návrat. Existující marker `disable-remediation` má nadále účinek.

Po zásahu počkejte na měření novější než úspěšné dokončení CLI. Porovnání
ukazuje pozorovanou změnu a neprohlašuje příčinnou souvislost či opravu.
Odmítnutý nebo nejistý výsledek se nezmění na úspěch jen poklesem CPU.

Export detailu vytváří lokální JSON s kódy, confidence, časy a číselnými
důkazy. Neobsahuje celé snapshots, raw text událostí, cesty ani tajemství.
Před sdílením jej přesto zkontrolujte. Dashboard nemá veřejný port a
neprovádí žádné automatické externí doručení.
