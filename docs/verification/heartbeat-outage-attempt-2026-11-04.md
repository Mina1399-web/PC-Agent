# Healthchecks heartbeat — pokus o výpadek a návrat (2026-11-04)

## Provedené ověření ve schválené DEV VM

- VM: HostAgent-DEV-Rebuild. Před testem běžely služby HostAgent (PID 3736)
  a HostAgent.Helper (PID 3400).
- Aktivní NAT adaptér VM 1 byl na více než pět minut odpojen. Uvnitř VM byl
  Ethernet ve stavu Disconnected; obě služby po celou dobu zůstaly spuštěné.
- Adaptér 1 byl obnoven. Ethernet přešel do stavu Up, test TCP spojení na
  hc-ping.com:443 uspěl a obě služby zůstaly spuštěné se stejnými PID.
- Měnila se pouze dočasná konektivita DEV VM. Nastavení hlavního PC ani
  bezpečnostní konfigurace aplikace se neměnily.

## Co tento test potvrzuje a co ne

Test potvrzuje, že po síťovém výpadku zůstala služba běžet a síť VM se obnovila.
Úspěšný TCP test portu 443 ale nepotvrzuje, že HostAgent odeslal ping a že ho
Healthchecks přijal. Aktuální stránka historie Healthchecks nebyla z tohoto
prostředí dostupná a běžný účet vmdev neměl oprávnění přečíst chráněnou
konfiguraci služby, takže aktuální hodnotu EnableHeartbeat se nepodařilo znovu
ověřit.

Dřívější snímek historie ukazoval neúspěšné pingy #676–#678 a úspěšný ping #679.
Detail #679 však uváděl Android 10 / Chrome jako uživatelského agenta. Tento ping
proto nelze připsat službě HostAgent a nepovažuje se za důkaz jejího zotavení.
Snímek obsahující endpoint ani jeho tajná data nejsou součástí repozitáře.

## Zbývající ruční kontrola

V účtu Healthchecks otevřít kontrolu HostAgent DEV VM a obnovit historii. Pro
dokončení testu je potřeba vidět selhání nebo dobu nedostupnosti během tohoto
výpadku a následný nový úspěšný ping po obnovení adaptéru, jehož zdroj odpovídá
DEV VM, nikoli telefonu. Celý tajný ping URL, token ani přístupové údaje
nesdílet. Pokud nový ping chybí nebo jeho původ není jasný, heartbeat zůstává
neověřený a je potřeba zkontrolovat stav služby z jejího zvýšeného, pouze
čtecího provozního rozhraní.
