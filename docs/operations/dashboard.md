# Místní přehled Host Agent

Na úvodním přehledu je souhrn hlášených omezení komponent a chybějících
aktuálních měření CPU a paměti. Prázdný seznam komponent není potvrzení
zdravého stavu. Zastaralý přehled nebo ztráta spojení zneplatní předchozí
souhrn; technické podrobnosti jsou v části **Komponenty**.

Spusťte **Host Agent** z nabídky Start, případně
`C:\Program Files\HostAgent\Desktop\HostAgent.Desktop.exe`. Přehled běží pod
běžným přihlášeným účtem, bez požadavku na správce. Vyžaduje monitor s novým
rozhraním `HostAgent.View.v1`; starší služba zobrazí nedostupnost, nikoli
vymyšlené hodnoty. X ukončí přehled; tlačítko Skrýt do lišty jej nechá běžet.
Ukončení přehledu nezastaví monitorovací služby.

V nové vývojové verzi lze tlačítkem **Vybrat složku pro údržbu** otevřít také
vlastní místní složku. V tomto výslovně vybraném rozsahu není podmínka stáří,
limit jednoho souboru je 1 GiB a limit karantény 10 GiB. Kontrolují se pouze
soubory přímo ve složce; systémové cesty, odkazy, nepovolená oprávnění a
zamčené soubory zůstávají vyloučené. Výchozí náhled Temp má původní limity.
Podmínky a nevratné mazání popisuje [rozšířená údržba](expanded-maintenance.md).
Tato funkce vyžaduje nově sestavený dashboard; změna režimu služby ji sama
nepřidá do staršího MSI.
Přehled obnovuje data po 15 sekundách. Aktuální metriky starší než tři minuty
nejsou aktuální stav. Diskové kapacity, deset největších spotřebitelů RAM,
počet aktivních adaptérů a health kódy neodvozují úspěšné připojení k internetu
ani dostupnost SMART. Historie je 24 hodin s průměry po 15 minutách; mezery se
nespojují. Incidenty obsahují dvacet posledních záznamů, detail nejvýše šestnáct
omezených důkazů. Confidence u bugchecku klasifikuje stop kód, nikoli viníka.

V části **Komponenty agenta** se vedle technického health kódu zobrazují
česká vysvětlení omezení měření procesů, CPU, paměti a služeb. Původní důvody
jsou dostupné po najetí na vysvětlení. Neznámé důvody zůstávají zobrazené
svým kódem. Chybějící důvody nejsou potvrzením zdravého stavu; chybějící
údaj o CPU procesu také sám o sobě neznamená chybějící celkové využití CPU.

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
po spuštění. Samostatné **Dočasné soubory** nabízí read-only náhled a po
výslovném potvrzení přesune všechny způsobilé soubory z tohoto náhledu do
karantény aktuálního
uživatele. Jde pouze o běžné soubory přímo v `%LOCALAPPDATA%\Temp`, nejméně
sedm dní staré, maximálně 16 MiB každý; náhled prochází nejvýše 500 položek a
navrhne nejvýše 100. Karanténa má limit 128 MiB a 256 záznamů, používá přesný
jednorázový identifikátor náhledu, který platí deset minut, a před každým
přesunem znovu ověřuje čerstvý stav služby. Obnova má vlastní přesný
identifikátor a nikdy nepřepisuje soubor, který mezitím vznikl na původním
místě. Nedostupné, změněné, zamčené nebo nepodporované položky se přeskočí.

Karanténa soubory **nesmaže a neuvolní místo**; data zůstávají obnovitelná.
Automatická likvidace není zapnutá. Nová ruční funkce dovoluje trvale smazat jeden vybraný soubor z karantény po zadání DELETE, mezery a přesného ID. Tuto operaci nelze vrátit. Ručně kontrolujte obsah a
obnovujte jen položky, kterým rozumíte. Přehled ani nástroj nemaže jiné
soubory, neprochází podadresáře a nepřijímá zadané cesty. Režim Shadow,
read-only, kill switch, zastaralý nebo nedostupný stav a zvýšený token vždy
zablokují změnu. Nové vysvětlení zdravotního stavu používá pouze omezené
stabilní kódy komponent; citlivý text výjimky se nezobrazuje.

OS-managed výběr, instalaci balíčků a případné nevratné smazání musí
zkontrolovat operátor. Funkce údržby byla ověřena automatizovanými testy nad
syntetickými soubory ve VM; její provedení na uživatelském Temp ani uvolnění
místa nejsou součástí provozního ověření.

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
## Jazyk rozhraní

Vývojová verze následující po `v1.2.8-preview.1` nabízí v záhlaví češtinu,
angličtinu a němčinu. Volba se projeví ihned a ukládá se pro příští spuštění.
Podrobnosti a odlišení od již vydaného balíčku uvádí
[výběr jazyka desktopu](desktop-languages.md). Nastavení mění pouze rozhraní;
monitorování a pravidla schvalování zůstávají stejné.
