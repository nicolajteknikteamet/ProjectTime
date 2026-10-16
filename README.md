# ProjectTime

Självhostad, automatisk tidsspårning i stil med [Memtime](https://www.memtime.com/) – men där **all data stannar på din egen server**.
Skrivet helt i .NET 10.

En liten agent på varje dator registrerar vilket program och fönster som är aktivt, och när du är inaktiv.
Allt skickas till en server du själv kör, där ett webbgränssnitt visar dagen timme för timme, summerar per projekt och program,
låter dig tilldela aktiviteter till projekt (manuellt eller med regler) och bokföra tid.

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ Kontorsdator │   │   MacBook    │   │  Linuxburk   │      agenter (projecttime-agent)
│   agent      │   │   agent      │   │   agent      │      • läser aktivt fönster varannan sekund
└──────┬───────┘   └──────┬───────┘   └──────┬───────┘      • upptäcker inaktivitet
       │  HTTPS, API-nyckel per enhet, buffras lokalt vid avbrott
       ▼                  ▼                  ▼
┌──────────────────────────────────────────────────────┐
│  ProjectTime Server (ASP.NET Core + Blazor, SQLite)  │    din server / NAS / Docker
│  Översikt · Aktiviteter · Tidsposter · Rapporter     │
│  Projekt · Regler · Enheter                          │
└──────────────────────────────────────────────────────┘
```

## Vad som spåras

| | |
|---|---|
| **Program** | processnamn, t.ex. `devenv`, `chrome`, `Code` |
| **Fönstertitel** | t.ex. `Kundportal – Visual Studio`, `Ärende #1182 – Zoho Desk` (kan stängas av) |
| **Sökväg** | programfilens sökväg när den går att läsa |
| **Inaktivitet** | tid utan tangentbord/mus (standard: efter 3 minuter), retroaktivt från sista aktivitet |
| **Enhet** | vilken dator aktiviteten kom från |

Inget tangentbordsinnehåll, inga skärmbilder, inga URL:er utöver vad som står i fönstertiteln.
Program kan uteslutas helt (t.ex. lösenordshanterare) via `ExcludedApps`.

## Webbgränssnittet

- **Översikt** – aktiv/inaktiv tid för dagen, dygnsremsa per enhet, staplar per projekt och program, dagen timme för timme.
- **Aktiviteter** – alla fönster grupperade per program med tid och klockslag. Välj projekt per fönster eller för hela programmet; en regel skapas automatiskt så att framtida (och tidigare otilldelade) aktiviteter tilldelas.
- **Tidsposter** – bokför tid på projekt. Förslag skapas från dagens aktiviteter (luckor slås ihop, redan bokförd tid räknas bort) eller läggs in manuellt. Veckovy och CSV-export.
- **Rapporter** – valfri period: aktiv tid per dag staplad per projekt, spårad kontra bokförd tid per projekt, per program, per enhet. CSV-export.
- **Projekt** – namn, kund, färg, timpris, arkivering.
- **Regler** – `program/titel/sökväg` + `innehåller/börjar med/är/regex` → projekt, med prioritet.
- **Enheter** – registrera datorer, skapa/rotera API-nycklar, se när de senast rapporterade, radera gammal data.

När flera datorer är aktiva samtidigt räknas tiden inte dubbelt i summeringarna.

## Komma igång

### 1. Servern

**Med Docker (rekommenderat):**

```bash
cd deploy
# Sätt lösenord och tidszon i docker-compose.yml
docker compose up -d --build
```

**Utan Docker:**

```bash
dotnet publish src/ProjectTime.Server -c Release -o /opt/projecttime
cd /opt/projecttime
PROJECTTIME_ProjectTime__Password='ett-starkt-lösenord' \
PROJECTTIME_ProjectTime__TimeZone='Europe/Stockholm' \
dotnet ProjectTime.Server.dll
```

Servern lyssnar på `http://0.0.0.0:5080`. Databas och nycklar hamnar i katalogen `data/` (eller `/data` i Docker).
Lägg den bakom en reverse proxy med HTTPS (Caddy, nginx, Traefik) om agenterna når den över internet.

Inställningar (`appsettings.json` under `ProjectTime`, eller miljövariabler `PROJECTTIME_ProjectTime__<Namn>`):

| Inställning | Standard | Beskrivning |
|---|---|---|
| `Password` | *(tomt)* | Lösenord till webbgränssnittet. Tomt = ingen inloggning – använd bara på ett skyddat nätverk. |
| `TimeZone` | `Europe/Stockholm` | Tidszon som dagar och klockslag visas i. |
| `DataDirectory` | `data` | Var SQLite-databasen lagras. |
| `MinVisibleSeconds` | `5` | Aktiviteter kortare än detta döljs i detaljlistor (räknas ändå). |
| `BookingMergeGapMinutes` | `5` | Luckor kortare än detta slås ihop när tidsposter föreslås. |

### 2. Registrera datorn

Öppna webbgränssnittet → **Enheter** → ange ett namn → **Skapa och visa nyckel**.
Nyckeln visas en gång (servern sparar bara en hash). Sidan visar färdig konfiguration att klistra in.

### 3. Agenten på varje dator

Publicera agenten för rätt plattform (eller hämta artefakten från CI):

```bash
dotnet publish src/ProjectTime.Agent -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o out/agent-win
dotnet publish src/ProjectTime.Agent -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o out/agent-mac
dotnet publish src/ProjectTime.Agent -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o out/agent-linux
```

Konfigurera via `appsettings.json` bredvid programmet, miljövariabler (`PROJECTTIME_Agent__ApiKey=...`) eller kommandorad (`--Agent:ApiKey=...`):

```json
{
  "Agent": {
    "ServerUrl": "https://tid.example.com",
    "ApiKey": "pt_...",
    "DeviceName": "Kontorsdator",
    "SampleIntervalSeconds": 2,
    "IdleThresholdSeconds": 180,
    "SyncIntervalSeconds": 30,
    "TrackWindowTitles": true,
    "ExcludedApps": [ "1Password", "KeePassXC", "Bitwarden" ]
  }
}
```

**Windows** – kör `deploy/agent/install-windows.ps1` i PowerShell som administratör från katalogen med den publicerade agenten:

```powershell
.\install-windows.ps1 -ServerUrl "https://tid.example.com" -ApiKey "pt_..." -DeviceName "Kontorsdator"
```

Agenten registreras som en schemalagd uppgift som startar vid inloggning (en Windows-tjänst kan inte se användarens fönster).

**macOS** – lägg filerna i `~/Library/Application Support/ProjectTime/bin/`, anpassa sökvägen i
`deploy/agent/nu.projecttime.agent.plist`, kopiera den till `~/Library/LaunchAgents/` och kör
`launchctl load -w ~/Library/LaunchAgents/nu.projecttime.agent.plist`.
Ge programmet behörighet under *Systeminställningar → Integritet och säkerhet → Hjälpmedel* samt *Automation* när macOS frågar.

**Linux (X11)** – installera `xdotool` och `xprintidle`, lägg filerna i `~/.local/share/ProjectTime/bin/`,
kopiera `deploy/agent/projecttime-agent.service` till `~/.config/systemd/user/` och kör
`systemctl --user enable --now projecttime-agent`. Wayland saknar ett generellt API för aktivt fönster och stöds inte.

### Offline och flera datorer

Agenten skriver allt till en lokal SQLite-buffert först och skickar i batchar till servern.
Är servern onåbar (tåg, VPN nere, bärbar dator utan nät) fortsätter spårningen och allt synkas när kontakten är tillbaka.
Varje segment har ett klient-id så att omsändningar aldrig ger dubbletter.
Sömn/viloläge upptäcks och tiden datorn sov registreras inte som arbete.

## Struktur

```
src/ProjectTime.Shared   DTO:er för API:t mellan agent och server
src/ProjectTime.Agent    Spårningsagent (Windows/macOS/Linux), lokal buffert, synk
src/ProjectTime.Server   ASP.NET Core + Blazor Server, EF Core/SQLite, API, rapporter
tests/ProjectTime.Tests  xUnit: segmentering, regler, intervallaritmetik, integrationstester mot SQLite
deploy/                  Dockerfile, docker-compose, tjänstfiler för agenten
```

Bygg och testa:

```bash
dotnet build
dotnet test
```

## Säkerhet och integritet

- Agenter autentiserar med en API-nyckel per enhet (`X-Api-Key`); servern lagrar bara SHA-256-hashen.
- Webbgränssnittet skyddas av lösenord och cookie-session; CSV-export kräver inloggning.
- Ingen telemetri, inga externa anrop, inga tredjepartstjänster.
- Radera gammal spårningsdata när som helst under **Enheter → Dataminimering**; tidsposter bevaras.
