# PID Departure Board (WPF, .NET 8)
Autor: Oliver Bocko

Jednoducha desktopova tabule odjezdu pro PID postavena nad Golemio API.

## Aktualni verze
0.0.1.6.5  https://github.com/PilsenSpotter/departue_board/releases/tag/0.0.1.6.5

## Funkce
- Svetly/tmavy motiv s prepinacem v zahlavi okna.
- Offline cache GTFS stops i poslednich odjezdu pro pripad, ze API/sit vypadne.
- Vyhledavani zastavek (GTFS stop_id) a vyber vice zastavek najednou.
- Filtry dopravy (bus, tram, metro, vlak, trolejbus) a platformy, pokud je API vrati.
- Filtr pristupnosti (vse / bezbarierove / vysokopodlazni) a zobrazeni pristupnosti v tabulce.
- Zobrazeni odjezdu z Golemio `departureboards` vcetne zpozdeni, platformy, typu vozidla a odpoctu.
- Automaticka obnova v nastavovanem intervalu a nastaveni minut dopredu.

## Pozadavky
- .NET 8 SDK
- Golemio API token s pristupem k `/v2/pid/departureboards`

## Konfigurace API klice
V souboru `DepartureBoard/Services/GolemioClient.cs` nahrad retezec v `EmbeddedApiKey` svym tokenem:
```csharp
private const string EmbeddedApiKey = "TVUJ_TOKEN";
```
Alternativa: nastav promennou prostredi `GOLEMIO_API_KEY` a nech `EmbeddedApiKey` prazdny.

## Spusteni
```bash
dotnet run --project DepartureBoard/DepartureBoard.csproj
```

## Pouziti
1) Do pole "Vyhledat zastavku" napis nazev, vyber konkretni polozku (platformove stop_id).
2) Pridej dalsi zastavky dle potreby, nastav filtry a interval.
3) Klikni "Nacist odjezdy" - data se pak obnovuji automaticky.

## Poznamky
- `departureboards` vraci pristupnost jen pokud je v datech (trip/departure/vehicle). Pokud API neposila `wheelchair_accessible/low_floor`, sloupec zustane prazdny.
- Vyhledavani pouziva aktualni GTFS z `https://data.pid.cz/PID_GTFS.zip` a filtruje pouze skutecne zastavkove body (ne parent stanice).
