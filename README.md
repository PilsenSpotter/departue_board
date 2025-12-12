# PID Departure Board (WPF, .NET 8)

Jednoduchá desktopová tabule odjezdů pro PID (Golemio API).

## Funkce
- Vyhledávání zastávky (GTFS stop_id) s fltrováním z GTFS stops.txt.
- Načtení odjezdů přes Golemio `departureboards` (PID) včetně zpoždění a informací o přístupnosti (pokud je API poskytne).
- Automatická obnova v nastavitelném intervalu.

## Požadavky
- .NET 8 SDK
- Platný Golemio API token s přístupem k `/v2/pid/departureboards`

## Konfigurace API klíče
V souboru `DepartureBoard/Services/GolemioClient.cs` nahraď řetězec v `EmbeddedApiKey` svým tokenem:
```csharp
private const string EmbeddedApiKey = "TVUJ_TOKEN";
```
Alternativa: nastav proměnnou prostředí `GOLEMIO_API_KEY` a nechej `EmbeddedApiKey` prázdný.

## Spuštění
```bash
dotnet run --project DepartureBoard/DepartureBoard.csproj
```

## Použití
1) Do pole „Vyhledat zastávku“ napiš název, vyber konkrétní položku (platformové stop_id).  
2) Klikni „Načíst odjezdy“. Odjezdy se pak obnovují podle intervalu.  
3) Sloupce: Linka, Směr, Stanoviště, Odjezd, Za, Zpoždění, Acc (♿ pokud API vrátí přístupnost).

## Poznámky
- `departureboards` vrací přístupnost jen pokud je v datech (trip/departure/vehicle). Pokud API neposílá `wheelchair_accessible/low_floor`, sloupec zůstane prázdný.  
- Vyhledávání používá aktuální GTFS z `https://data.pid.cz/PID_GTFS.zip` a filtruje pouze skutečné zastávkové body (ne parent stanice).
