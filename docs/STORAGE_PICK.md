# Storage Pick — MTI ↔ IT.Storage (WWKS2)

MTI connects to the pharmacy storage robot over **WWKS 2.0 TCP** (port **6050**) using the shared library in [Test-WWKS/StorageSystem](../../Test-WWKS/StorageSystem) (GitHub: `La-Fabrica-de-Software/Test-WWKS-PROTOCOL`, clone as sibling folder `Test-WWKS`).

## What it does

- **Inventory → Storage Pick** (`/StoragePick`)
- **Load from IT.Storage** — `StockInfoRequest` / stock list
- Select articles → **Request pick** — `OutputRequest`, quantity **1** per article

Only articles **physically stored on robot shelves** appear in MTI. Excluded from the list:

- **Pending load** (`EnCarga` / no shelf assigned yet)
- Articles on **load** or **dispense** points (not storage shelves)

When MTI requests a pick, `RobotDeFarmacia` enqueues an internal **Dispensar** order via the shared `MtiPickQueue` SQLite table.

Requires role **Inventory** or **Admin**.

## Configuration

In `appsettings.Development.json` (or `appsettings.json`):

```json
"StorageSystem": {
  "Host": "127.0.0.1",
  "Port": 6050,
  "SubscriberId": 100,
  "OutputDestination": 1,
  "OutputPoint": 0,
  "IncludePacks": true,
  "IncludeArticleDetails": false
}
```

## Prerequisites

1. **One robot** listening on port **6050** (not two at once).
2. Robot must have **stock** (empty DB returns 0 articles).
3. MTI user with **Inventory** or **Admin** role.

Test login (see main [README.md](../README.md)):

| Role      | Email              | Password        |
|-----------|--------------------|-----------------|
| Inventory | inventory@test.com | Inventory123!   |
| Admin     | admin@test.com     | Admin123!       |

---

## Quick test (one script)

From the MTI project folder in PowerShell:

```powershell
.\scripts\run-storage-pick-dev.ps1
```

Choose:

1. **Start robot (seeded)** — dev robot with sample article `87654321`
2. **Smoke test only** — WWKS connect + stock + pick (no browser)
3. **Start MTI** — web app
4. **Full dev flow** — robot → smoke test → open MTI URL

---

## Manual step-by-step

### Terminal 1 — Robot with stock

**Recommended for development:**

```powershell
dotnet run --project "D:\La-Fabrica-de-Software\Test-WWKS\tools\SeedRobotHost\SeedRobotHost.csproj"
```

Expected: `Seeded article 87654321 with 1 pack(s)` and `Robot listening on port 6050`.

**Alternatives:**

```powershell
# Production-style host (load stock via ItSystem.Simulator or input)
dotnet run --project "D:\La-Fabrica-de-Software\Test-WWKS\PharmacyRobot.Host" -- --port 6050 --subscriber 999
```

**Physical robot (RobotDeFarmacia on branch `3Capas`):**

1. Start **TestFarmacia** and connect the MODBUS robot (same SQLite path as below).
2. Start WWKS host in physical mode (only one listener on 6050):

```powershell
dotnet run --project "D:\La-Fabrica-de-Software\Test-WWKS\PharmacyRobot.Host" -- --hardware physical --farmacia-db "C:\La Fabrica de Software\Farmacia\farmacia.db"
```

Stock is synced from `Ubicaciones` in `farmacia.db`; picks run via shared `MtiPickQueue`. See [RobotDeFarmacia/INTEGRATION.md](../../RobotDeFarmacia/INTEGRATION.md) and [Test-WWKS/docs/PHYSICAL_ROBOT_INTEGRATION.md](../../Test-WWKS/docs/PHYSICAL_ROBOT_INTEGRATION.md).

For production MTI, set `StorageSystem:Host` to the robot PC IP address (not `127.0.0.1`).

### Terminal 2 — Automated smoke test (no UI)

```powershell
dotnet run --project "D:\La-Fabrica-de-Software\1 - Manufacturing-Time-Inventory\tools\StoragePickSmoke\StoragePickSmoke.csproj"
```

Success:

```
GetStock: 1 article(s).
  87654321 | MTI Smoke Test | qty=1
Succeeded: 87654321
Smoke test OK.
```

### Terminal 3 — MTI web UI

```powershell
cd "D:\La-Fabrica-de-Software\1 - Manufacturing-Time-Inventory"
dotnet build
dotnet run
```

Open **http://localhost:80/StoragePick** (or the URL shown in the console).

1. Log in as Inventory or Admin.
2. **Load from IT.Storage**
3. Select rows → **Request pick**

If port 80 fails (permissions), use:

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5080"
dotnet run
```

Then open **http://localhost:5080/StoragePick**.

### Optional — WWKS tests in Test-WWKS

```powershell
dotnet test "D:\La-Fabrica-de-Software\Test-WWKS\PharmacyRobot.Tests\PharmacyRobot.Tests.csproj" --filter "FullyQualifiedName~StockConsistency"
```

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|--------|-----|
| Cannot connect to 127.0.0.1:6050 | Robot not running | Start `SeedRobotHost` or `PharmacyRobot.Host` |
| Connected, 0 articles | Empty stock | Use `SeedRobotHost` or load stock in simulator |
| Port 6050 in use | Two robots | `netstat -ano \| findstr :6050` then stop the other process |
| Page redirects to login | Not signed in / wrong role | Use `inventory@test.com` or Admin |
| Pick queued but OutputMessage timeout in logs | Normal; dispense is async | Check robot trace; order may still be queued |

---

## Architecture (short)

```
MTI StoragePickController
  → IStorageSystemClient (StorageSystemClient)
    → RowaStorageSystem (WWKS2 TCP :6050)
      → PharmacyRobot.Host / SeedRobotHost / Simulator
```

Key code:

- `Services/StorageSystem/StorageSystemClient.cs`
- `Controllers/StoragePickController.cs`
- `Views/StoragePick/Index.cshtml`
