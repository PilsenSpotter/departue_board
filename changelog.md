# Changelog

## Unreleased
- Pridan rezim zobrazovac (fullscreen + always on top, skryje horni panel, vetsi pismo; ESC ukonci).
- Novy PID-like rezim tabule: LED panel s oran‘ovym textem, fullscreen, zobrazuje top 4 odjezdy, cas, stanoviste a pristupnost.
- Pridano prepinani ukladani nastaveni (zastavky, filtry, motiv); pri vypnuti se smazou ulozene hodnoty.
- Obarveni odjezdu podle zpozdeni (jen v normalnim seznamu, ne v rezimu tabule).
- Fix: barva zpozdeni ma prednost pred alternovanim radku, aby byla videt vzdy.
- Pridan filtr „Jen včas“ pro skrytí zpožděných spojů.
- Pridany filtry linek podle prave nactenych odjezdu (stejny styl jako stanoviště).
- Sety zastavek/filtru: ulozitelne pod nazvem, nacitani/mazani ulozeno v %AppData%.

## 0.0.1.6.5
- Pridan tmavy/svetly motiv s prepinacem v zahlavi okna.
- Pridano offline cache pro rychlejsi nacitani zastavek

## 0.0.1.6
- Offline cache: GTFS stops a posledni odjezdy se uchovavaji a zobrazi pri vypadku site/API.
- Jemne doladeni barev (Win11-like) a redesign tabulky odjezdu (rounded karty, novy hover/alternace).
- Scrollovani primo v seznamu vybranych zastavek i ve vysledcich vyhledavani koleckem/touchpadem.
- Vylepsene zobrazeni typu vozidel s emoji ikonou podle druhu linky.

## 0.0.1.5
- Pridan prepinac vlaku a okamzity refresh pri zmene filtru dopravy.
- Filtrovani podle stanovist MHD (platforms) nactenych z odjezdu a dostupny vyber v UI.
- Integrace vehicle positions: zobrazeni typu vozidla a presnejsi pristupnosti (wc) podle RT dat.
- Novy filtr pristupnosti (vse / bezbarierove / vysokopodlazni).
- UI rozsirovano o kolonku "Typ vozidla", sekci stanovist a checkbox "Vlak".

## 0.0.1
- Pocatecni verze: zakladni odjezdova tabule, filtrovani dopravnich modu, vyhledavani a vyber zastavek.
