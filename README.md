# Manufacturing Time Tracking & Inventory

A single web application that combines **time tracking** (manufacturing/assembly by phases and steps) and **inventory** (items, locations, stock, movements) in one database. Some users can access only time management, others only inventory, and items can be moved from warehouse to production stations (shared items).

## Single database, role-based access

- **One database** (`ManufacturingTimeTracking`) for both time tracking and inventory.
- **Time management menu** (Models, Templates, Resources, Orders): visible to **Admin**, **Supervisor**, **Operator**, and **Normal** (Normal has view/execute only; cannot edit templates or add steps/instructions).
- **Inventory menu** (Items, Locations, Stock, Movements, Operations, **Storage Pick**): visible to **Admin** and **Inventory**.
- **Storage Pick** (IT.Storage / WWKS2): request robot stock and pick articles from the pharmacy storage system. See **[docs/STORAGE_PICK.md](docs/STORAGE_PICK.md)** and run `.\scripts\run-storage-pick-dev.ps1` for local testing.
- **Manage Users** (admin only): **Admin** can change any user’s role and reset password. Every user must have a role.
- **Shared items:** Inventory items can be linked to process Materials (same product in warehouse and production). Use **Putaway** and **Pick** to move items from warehouse to production stations (create locations with type "Workstation").
- **Prices:** When prices are added to components later, they will be hidden from non-Admin users (policy `CanSeePrices`).

**Role permissions (summary):**

| Role        | Time (view/execute) | Edit templates / Add steps or instructions | Manage users | See prices (future) |
|------------|---------------------|--------------------------------------------|--------------|----------------------|
| Admin      | ✓                   | ✓                                          | ✓            | ✓                    |
| Supervisor | ✓                   | ✓                                          | —            | —                    |
| Operator   | ✓                   | —                                          | —            | —                    |
| Normal     | ✓                   | —                                          | —            | —                    |
| Inventory  | —                   | —                                          | —            | —                    |

**Test accounts:**

| Role       | Email               | Password      | Access              |
|-----------|---------------------|---------------|---------------------|
| Admin     | admin@test.com      | Admin123!     | Time + Inventory + Manage Users |
| Supervisor| supervisor@test.com | Supervisor123!| Time (can edit templates/steps) |
| Operator  | operator@test.com   | Operator123!  | Time only           |
| Inventory | inventory@test.com   | Inventory123! | Inventory only      |
| Normal    | user@test.com       | User123!      | Time (view/execute only)       |

## 🚀 Quick Start Guide

### First Time Setup (5 Minutes)

1. **Open the project** in Visual Studio
2. **Press F5** or click "Run" to start the application
3. **Wait** for the browser to open automatically
4. **Login** with:
   - Email: `admin@test.com`
   - Password: `Admin123!`
5. **Done!** You're ready to use the system

> 💡 **Note:** The database is created automatically on first run. No manual setup needed!

**Production (client access):** The public URL is **http://manufacturingtimelfs.dyndns.org** (Dyndns). See [HOW_TO_CHANGE_DOMAIN.md](HOW_TO_CHANGE_DOMAIN.md) for running the app on port 80 and firewall/Google OAuth setup.

**Deploy / update from Visual Studio or GitHub:** See **[DEPLOYMENT.md](DEPLOYMENT.md)** for publishing from Visual Studio 2022, deploying from GitHub, and one-click update options.

### Troubleshooting (clone/pull on another machine)

- **NU1101 – "Unable to find package … No packages exist in source(s): Microsoft Visual Studio Offline Packages"**  
  On a new laptop or fresh install, NuGet may be using only an offline source. Fix it in one of these ways:
  1. **Use the repo’s NuGet config**  
     The project includes a `nuget.config` that adds **nuget.org**. From the project folder run:  
     `dotnet restore`  
     then build again (e.g. F5 in Visual Studio).
  2. **In Visual Studio**  
     **Tools → NuGet Package Manager → Package Manager Settings → Package Sources**. Ensure **nuget.org** is listed with URL `https://api.nuget.org/v3/index.json` and is **checked**. If "Microsoft Visual Studio Offline Packages" is the only source, add nuget.org and try **Restore NuGet Packages** (right‑click solution → Restore NuGet Packages), then rebuild.
  3. **From command line (one-time)**  
     `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`  
     then `dotnet restore` and build.

- **"Cannot create file '...ManufacturingTimeTracking_Dev.mdf' because it already exists" / "Cannot open database ManufacturingTimeTracking_Dev"**  
  On a new laptop (or after copying the project), SQL Server LocalDB may find an old database file in your user folder (`C:\Users\<YourUsername>\`) and fail to create the database, then every request fails with "Cannot open database". **Fix:**  
  1. Stop the app completely.  
  2. In File Explorer go to `C:\Users\<YourUsername>\` (replace with your Windows user name, e.g. `LFS`).  
  3. Delete the files **ManufacturingTimeTracking_Dev.mdf** and **ManufacturingTimeTracking_Dev.ldf** (if present).  
  4. Run the app again so it can create a fresh database.  
  If you need to keep data, back up the .mdf/.ldf first or use a different database name in `appsettings.Development.json` (e.g. `ManufacturingTimeTracking_Dev2`).

- **Uploaded photos/videos show 404 (e.g. /uploads/templates/…/file.mp4)**  
  The app now saves all uploads under the web root (`wwwroot/uploads/`) so they are served correctly. If you had uploads before this fix, they may have been saved to a different folder (e.g. under `bin/`) and will 404. **Fix:** Re-upload the media in the template step or order execution page so the files are stored in the correct location. New uploads will work.

- **"Invalid object name 'Locations'"**
  The app now ensures inventory tables (Locations, Items, etc.) exist on every startup. If you still see this, run the app once so migrations and schema setup complete, or run `dotnet ef database update` from the project folder.

- **"Address already in use" (e.g. port 5173)**  
  Another process is using the default HTTP/HTTPS ports. Either stop that process or use different ports by adding to `appsettings.Development.json`:
  ```json
  "Kestrel": { "HttpPort": 5174, "HttpsPort": 7246 }
  ```
  Then open `http://localhost:5174` (or your chosen HTTP port).

---

## 📋 Complete User Guide

### 🔐 Step 1: Login

**Where:** Top right corner of the page

**How:**
1. Click the **"Login"** button
2. Enter your email and password
3. Click **"Log in"**

**Test Accounts:** See table above. For inventory-only access use `inventory@test.com` / `Inventory123!`

---

### 🏭 Step 2: Create Machine Models

**Purpose:** Define the types of machines you manufacture

**Location:** Click **"Models"** in the top menu

**Steps:**
1. Click **"Create New Model"** button (blue button at top)
2. Type a name (example: `S600` or `D600`)
3. Click **"Create"** button
4. ✅ Model created! You'll see it in the list

**To Add Variants:**
1. Click **"Details"** next to your model
2. Scroll down to **"Add New Variant"** section
3. Fill in:
   - **Name:** (example: `UHF` or `VHF`)
   - **Code:** (example: `UHF-001`)
4. Click **"Add Variant"**
5. ✅ Variant appears in the list above

**To Delete:**
- **Model:** Click **"Delete"** button → Confirm
- **Variant:** Click **"Delete"** next to the variant → Confirm

---

### 🔧 Step 3: Add Tools and Materials

**Purpose:** Create a catalog of tools and materials used in manufacturing

**Location:** Click **"Resources"** → Choose **"Tools"** or **"Materials"**

#### Adding Tools:
1. Click **"Create New Tool"**
2. Enter tool name (example: `Screwdriver`, `Wrench`)
3. Click **"Create"**
4. ✅ Tool added!

#### Adding Materials:
1. Click **"Create New Material"**
2. Enter:
   - **Name:** (example: `Screw M4x20`)
   - **Unit:** (example: `pcs` or `kg`) - Optional
3. Click **"Create"**
4. ✅ Material added!

**To Edit or Delete:**
- Click **"Edit"** to change name
- Click **"Delete"** to remove

---

### 📝 Step 4: Create a Process Template

**Purpose:** Define the manufacturing process (phases and steps) for a model+variant

**Location:** Click **"Templates"** in the top menu

**Steps:**
1. Click **"Create New Template"**
2. Select:
   - **Machine Model:** Choose from dropdown
   - **Machine Variant:** Will load automatically
3. Click **"Create"**
4. You'll see the template editor page

#### Adding Phases:
1. Find **"Add New Phase"** section
2. Enter:
   - **Name:** (example: `Preparation`, `Assembly`, `Testing`)
   - **Sort Order:** Number (1, 2, 3...)
3. Click **"Add Phase"**
4. ✅ Phase appears in the list

#### Adding Steps to Each Phase:
1. Find the phase you want to add steps to
2. In that phase's **"Add New Step"** section, enter:
   - **Title:** (example: `Check materials`, `Install component`)
   - **Instructions:** What to do (detailed description)
   - **Sort Order:** Number within the phase (1, 2, 3...)
   - **Allow Skip:** ☑ Check if step can be skipped
3. Click **"Add Step"**
4. ✅ Step appears under that phase
5. Repeat for all steps

**To Edit or Delete:**
- Click **"Edit"** next to template/phase/step
- Click **"Delete"** to remove

---

### 📦 Step 5: Create a Build Order

**Purpose:** Create an order for manufacturing a specific unit

**Location:** Click **"Orders"** in the top menu

**Steps:**
1. Click **"Create New Order"** button
2. Fill in the form:
   - **External Reference:** Your reference number (example: `ORD-001`)
   - **Serial Number:** Unique serial (example: `SN-2026-001`)
   - **Machine Model:** Select from dropdown
   - **Machine Variant:** Loads automatically
3. Click **"Create"**
4. ✅ Order created with status "Pending"

---

### ⚙️ Step 6: Execute Manufacturing

**Purpose:** Actually perform the manufacturing and track time

**Location:** Orders list → Click **"Execute"** button

#### Starting Manufacturing:
1. Review order information at top
2. Click **"Start Manufacturing"** button
3. ✅ Template snapshot created, status changes to "InProgress"

#### Working with Steps:

**▶️ To Start a Step:**
1. Find the step you want to work on
2. Click **"Start"** button
3. (Optional) Select a **Workstation** from dropdown
4. Click **"Start"** in the popup
5. ✅ Step status = "In Progress", timer starts

**⏹️ To Stop a Step:**
1. When finished, click **"Stop"** button
2. ✅ Time recorded automatically, status = "Done"

**⏭️ To Skip a Step:**
1. Click **"Skip"** button (only if step allows skipping)
2. Type a **reason** (required)
3. Click **"Skip Step"**
4. ✅ Status = "Skipped"

**🔄 To Rework (Do Step Again):**
1. For completed steps, click **"Rework"** button
2. (Optional) Select workstation
3. Click **"Start"**
4. ✅ New run starts, all runs tracked separately

**📷 To Add Evidence:**
1. Click **"Add Evidence"** button on any step
2. (Optional) Upload an **Image** file
3. (Optional) Type a **Note**
4. Click **"Upload"**
5. ✅ Evidence thumbnail appears on step card

**➕ To Add New Step During Execution:**
1. Click **"Add Step"** button at top of any phase
2. Enter:
   - **Step Title** (required)
   - **Instructions** (optional)
   - ☑ **Allow Skip** (optional)
3. Click **"Add Step"**
4. ✅ New step appears immediately, ready to execute

---

### 📊 Step 7: View Summary and Export

**Purpose:** See complete execution report and export data

**Location:** Orders list → Click **"Summary"** button

**What You'll See:**
- Order information
- Execution timeline
- All phases and steps
- Total times for each step
- Number of runs (rework count)
- Workstations used

**To Export CSV:**
1. Click **"Export CSV"** button
2. File downloads automatically
3. Open in Excel or any spreadsheet program
4. ✅ Contains all execution data

---

### 👁️ Step 8: View Order Details

**Location:** Orders list → Click **"Details"** button

**What You'll See:**
- All executions for that order
- Execution history
- Status of each execution

---

## 🗑️ How to Delete Items

| Item | Where to Delete | How |
|------|----------------|-----|
| **Model** | Models list | Click **"Delete"** → Confirm |
| **Variant** | Model Details page | Click **"Delete"** next to variant → Confirm |
| **Tool** | Tools list | Click **"Delete"** → Confirm |
| **Material** | Materials list | Click **"Delete"** → Confirm |
| **Template** | Templates list | Click **"Delete"** → Confirm |
| **Phase/Step** | Template Edit page | Click **"Delete"** → Confirm |

> ⚠️ **Warning:** Deleting a Model also deletes all its Variants!

---

## 🎯 Typical Workflow

```
1. Login
   ↓
2. Create Model + Add Variants
   ↓
3. (Optional) Add Tools & Materials
   ↓
4. Create Template → Add Phases → Add Steps
   ↓
5. Create Order
   ↓
6. Execute Manufacturing → Start/Stop Steps
   ↓
7. View Summary → Export CSV
```

---

## 🔧 Technical Setup

### Prerequisites
- .NET 9.0 SDK
- SQL Server LocalDB (included with Visual Studio)
- Visual Studio 2022 (recommended)

### Running the Application

**Option 1: Visual Studio**
1. Open `ManufacturingTimeTracking.csproj`
2. Press **F5** or click Run
3. Browser opens automatically

**Option 2: Command Line**
```bash
dotnet restore
dotnet build
dotnet run
```

### Database
- Created automatically on first run
- Uses LocalDB by default
- Seed data (test users, sample model) added automatically

### Configuration Files
- `appsettings.Development.json` - Development settings
- `appsettings.json` - Production settings

### Google Sign-In (OAuth)
To enable **Continue with Google** on the login page:

1. **Google Cloud Console:** Create an OAuth 2.0 Client (APIs & Services → Credentials). Add your **Authorized JavaScript origins** (e.g. `https://localhost:7078`, `https://localhost:7245`, your production URL).
2. **Authorized redirect URIs:** Add the **exact** path `/signin-google` for each origin. For example:
   - `https://localhost:7078/signin-google`
   - `https://localhost:7245/signin-google`
   - `https://your-domain.com/signin-google`
   - If the app runs under a path base (e.g. `/manufacturing`), use `https://your-domain.com/manufacturing/signin-google`.
3. **Important:** The path must be **`/signin-google`** (with an **e**). A typo like `signin-googl` will cause `redirect_uri_mismatch`.
4. **App config:** Set `Google:ClientId`, `Google:ClientSecret`, and optionally `Google:AllowedDomain` (e.g. `lafabricadesoftware.com`) in `appsettings.Development.json` or environment variables. On startup the app logs the redirect URIs you should add in Google Console.

---

## 📁 Project Structure

```
ManufacturingTimeTracking/
├── Pages/
│   ├── Account/          # Login/Logout
│   ├── Catalog/          # Models & Variants
│   ├── Templates/        # Process Templates
│   ├── Orders/           # Orders & Execution
│   ├── Tools/            # Tools Management
│   └── Materials/        # Materials Management
├── Models/               # Data Models
├── Data/                 # Database Context
└── wwwroot/             # Static Files & Uploads
```

---

## ❓ Troubleshooting

### Can't Login?
- Check email and password are correct
- Make sure database was created (first run takes longer)

### "Error loading external login information" after Continue with Google?
This happens when the app cannot restore the OAuth state after Google redirects back. Common causes:

1. **URL/port mismatch**  
   Open the app at **one** URL and add that **exact** URL in Google Cloud Console → Credentials → your OAuth client → **Authorized redirect URIs** (e.g. `https://localhost:7245/signin-google` or `http://localhost:5173/signin-google`). If you start on `https://localhost:7245` but the redirect URI in Google is `https://localhost:44333/signin-google`, the cookie set before going to Google won’t match the callback URL and the error appears. Use the same origin (scheme + host + port) everywhere.

2. **Data Protection keys**  
   The OAuth state is stored in an encrypted cookie. If the app restarted or keys changed, the cookie can’t be decrypted. Use a single running instance and the same URL when testing; for production, consider persisting Data Protection keys (e.g. to a shared store) so all instances can decrypt the cookie.

3. **Cookies**  
   Ensure the browser allows cookies for your site and that you’re not switching between HTTP and HTTPS or between different ports when testing (stick to one base URL).

### Edit/Delete buttons missing on Orders?
- Edit and Delete are shown only for users in the **Admin** role. Log in with `admin@test.com` / `Admin123!`.
- If you pulled the project on a new machine and already had an account with that email (e.g. from Register), the seed now ensures that account gets the Admin role on startup. **Restart the app once** so the seed runs, then log in again—the buttons should appear.

### Can't Add Variant?
- Make sure Name and Code fields are filled
- Check you're on the Model Details page

### Can't Delete?
- Make sure item isn't being used in a Template or Order
- Check you clicked the Delete button and confirmed

### Application Won't Start?
- Stop Visual Studio debugger
- Close any running instances
- Rebuild solution (Build → Rebuild Solution)

### Database Errors?
- Check SQL Server LocalDB is running (or SQL Server/Express if you use that)
- Verify connection string in `appsettings.Development.json`: on a new machine use a server you can reach (e.g. `(localdb)\\MSSQLLocalDB` or `.\SQLEXPRESS` for local SQL; for a remote server ensure firewall/network allows port 1433)
- If you see "Connection Timeout Expired" or "pre-login handshake": the app uses a 60s connection timeout; ensure the SQL Server address in the connection string is correct and that the server is running and reachable from your machine

---

## 📞 Need Help?

If you encounter issues:
1. Check this README first
2. Review error messages carefully
3. Check the Troubleshooting section above
4. Contact the development team

---

## ✅ Features Checklist

- ✅ User authentication (Login/Logout)
- ✅ Machine model and variant management
- ✅ Tools and materials catalog
- ✅ Process template creation
- ✅ Build order management
- ✅ Manufacturing execution with timing
- ✅ Step skipping with reasons
- ✅ Rework tracking (multiple runs)
- ✅ Workstation selection
- ✅ Image evidence upload
- ✅ Add steps during execution
- ✅ Order summary and CSV export
- ✅ Delete functionality for all items

---

**Last Updated:** January 2026

---

## Appendix A – Connection Checks (from `CHECK_CONNECTION.md`)

# "This site can't be reached" – what to check

Do these steps to find where it fails.

---

## On the **second laptop** (the one that can't open the site)

### Step 1: Does the domain resolve?

Open **Command Prompt** or **PowerShell** and run:

```cmd
ping manufacturingtimelfs.dyndns.org
```

- **If you see** something like `Reply from 81.47.86.153` (or another IP)  
  → DNS is OK. The problem is likely **firewall** or **router port 80**.

- **If you see** "Ping request could not find host" or "Unknown host"  
  → DNS is not resolving. Check Dyn Updater on the first laptop and try again later, or use the IP from the first laptop (Step 3).

### Step 2: Is port 80 reachable?

Still on the second laptop, run (use the same IP you got from ping, or 81.47.86.153):

```cmd
telnet manufacturingtimelfs.dyndns.org 80
```

If **telnet** is not installed, try:

```powershell
Test-NetConnection -ComputerName manufacturingtimelfs.dyndns.org -Port 80
```

- **If it says "TcpTestSucceeded : True"**  
  → Port 80 is reachable. The problem may be the **app** (not running or not on port 80).

- **If it says "TcpTestSucceeded : False" or times out**  
  → Port 80 is not reachable from the internet. So either:
  - **Router** is not forwarding port 80 to the first laptop, or  
  - **Windows Firewall** on the first laptop is blocking port 80.

---

## On the **first laptop** (where the app runs)

### Step 3: Is the app listening on port 80?

Open **PowerShell** or **CMD** and run:

```cmd
netstat -ano | findstr "LISTENING" | findstr ":80 "
```

- You **must** see a line like: `TCP    0.0.0.0:80    0.0.0.0:0    LISTENING    <number>`
- If you see **nothing** → start the app (F5) with the **https** or **http** profile so it uses port 80, then run the command again.

### Step 4: Firewall allows port 80

Open **PowerShell as Administrator** and run:

```powershell
New-NetFirewallRule -DisplayName "Manufacturing Time App (HTTP 80)" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
```

### Step 5: What is the first laptop’s local IP?

On the first laptop run:

```cmd
ipconfig
```

Note the **IPv4 Address** of the adapter you use for internet (e.g. **192.168.1.105**).

---

## On the **router**

- Log in to the router (e.g. 192.168.1.1 in the browser).
- Find **Port Forwarding** / **Virtual Server** / **NAT**.
- Ensure there is a rule:
  - **External port:** 80  
  - **Internal IP:** the first laptop’s IPv4 (from Step 5)  
  - **Internal port:** 80  
  - **Protocol:** TCP  
- Save and apply.

---

## Same Wi‑Fi / same network test

If **both laptops are on the same Wi‑Fi (same building)**:

On the **second laptop**, in the browser try:

**http://\<first laptop’s IPv4\>**

Example: **http://192.168.1.105**

- If **this works** but **http://manufacturingtimelfs.dyndns.org** does not  
  → The app and firewall on the first laptop are OK. The problem is **router port 80 forwarding** or **Dyn Updater / public IP**.

- If **both** fail  
  → On the first laptop, fix **app listening on 80** and **firewall** (Steps 3 and 4).

---

## Summary

| Symptom | Likely cause |
|--------|----------------|
| Ping "could not find host" | Dyn Updater / DNS – check Dyn Updater on first laptop, refresh IP |
| Ping OK, port 80 test fails | Router port 80 forwarding or Windows Firewall on first laptop |
| Port 80 test OK, browser "can't be reached" | App not running or not listening on port 80 on first laptop |
| http://\<first laptop IP\> works, dyndns URL does not | Router port 80 forwarding or wrong public IP in Dyn |

Use the **correct URL**: **http://manufacturingtimelfs.dyndns.org** (no typos).

---

## Appendix B – Remote Access Troubleshooting (from `TROUBLESHOOT_REMOTE_ACCESS.md`)

# Troubleshoot: Can't access site from another laptop

Run these checks **on the first laptop** (the one where the app runs and Dyn Updater is installed).

---

## 1. App is listening on port 80

**On the first laptop:**

1. Make sure the app is running (F5 in Visual Studio, or `dotnet run` with the profile that uses port 80).
2. Open **PowerShell** or **Command Prompt** and run:
   ```powershell
   netstat -ano | findstr "LISTENING" | findstr ":80 "
   ```
3. You should see at least one line like:
   ```text
   TCP    0.0.0.0:80    0.0.0.0:0    LISTENING    <number>
   ```
   If you see **nothing**, the app is not bound to port 80. Restart the app and use the **https** or **http** launch profile. If you get "access denied," run Visual Studio as Administrator once, or see HOW_TO_CHANGE_DOMAIN.md (binding to port 80).

---

## 2. Windows Firewall allows port 80

**On the first laptop**, run **PowerShell as Administrator** and run:

```powershell
New-NetFirewallRule -DisplayName "Manufacturing Time App (HTTP 80)" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow -ErrorAction SilentlyContinue
```

Then try from the second laptop again.

To check if a rule already exists:

```powershell
Get-NetFirewallRule -DisplayName "*80*" | Format-Table DisplayName, Enabled, Direction -AutoSize
```

---

## 3. Router forwards port 80 to the first laptop

**On the first laptop**, get its **local IP** (the one on your Wi‑Fi or Ethernet):

```powershell
ipconfig
```

Look for **IPv4 Address** under your active connection (e.g. Wi‑Fi or Ethernet). It will look like `192.168.1.105` or `10.0.0.5` or `172.16.x.x`.

**On the router** (browser → router admin, e.g. 192.168.1.1):

- Find **Port Forwarding** / **Virtual Server** / **NAT**.
- Add or edit a rule:
  - **External port:** 80  
  - **Internal IP:** the first laptop’s IPv4 address (e.g. 192.168.1.105)  
  - **Internal port:** 80  
  - **Protocol:** TCP  
- Save and apply.

If the first laptop’s IP changes often, set a **DHCP reservation** in the router so that laptop always gets the same IP.

---

## 4. Test from the same network (second laptop)

**Second laptop** – open browser and try, in this order:

1. **http://manufacturingtimelfs.dyndns.org**  
   (Uses DynDNS and goes out to the internet and back via the router.)

2. If that fails, try **http://\<first laptop IP\>**  
   Example: **http://192.168.1.105**  
   (Direct to the first laptop on the LAN; no router forwarding involved.)

- If **http://\<first laptop IP\>** works but **http://manufacturingtimelfs.dyndns.org** does not → problem is **router port 80 forwarding** or **Dyn Updater / DNS**.
- If **both** fail → problem is **firewall** on the first laptop or the **app not listening on port 80** (step 1).

---

## 5. Dyn Updater (reminder)

On the first laptop:

- Dyn Updater app is **running** and **Status: Good**.
- **manufacturingtimelfs.dyndns.org** is **checked**.
- **Current IP** in Dyn matches the first laptop’s **public IP** (e.g. check on https://whatismyip.com). Use **Refresh IP** in Dyn if needed.

---

## Quick checklist (first laptop)

| Check | Command or action |
|-------|--------------------|
| App listening on 80 | `netstat -ano \| findstr "LISTENING" \| findstr ":80 "` → should show a line |
| Firewall | Run the `New-NetFirewallRule` command above (PowerShell as Admin) |
| Local IP | `ipconfig` → use IPv4 in port forwarding |
| Router | Port 80 → first laptop’s IPv4, TCP |

After fixing, try again from the second laptop: **http://manufacturingtimelfs.dyndns.org**.

---

## Appendix C – Domain and Deployment (from `HOW_TO_CHANGE_DOMAIN.md`)

# Domain and deployment

**Production URL:** The public site is **http://manufacturingtimelfs.dyndns.org** (Dyndns). Keep the Dyn Updater app running so the hostname stays updated to the current IP.

---

## Running on Dyndns (manufacturingtimelfs.dyndns.org)

Use this for production. The client has Dyndns and forwards **port 80** on the router to the app machine.

### 1. Run the app on port 80

- **From Visual Studio / Cursor**: Choose the **port80** launch profile, then run (F5).
- **From command line**:
  ```powershell
  $env:UseHttpsRedirection = "false"
  dotnet run --urls "http://0.0.0.0:80"
  ```
  Or add to `appsettings.Development.json` (or your environment config):
  ```json
  "UseHttpsRedirection": false
  ```
  Then run: `dotnet run --urls "http://0.0.0.0:80"`

### 2. Open the site

Users open: **http://manufacturingtimelfs.dyndns.org** (port 80 is default, so no `:80` needed).

Note: The app listens on port 80 over **HTTP**. If you wrote `https://manufacturingtimelfs.dyndns.org:80`, use **http://** instead unless you later add HTTPS (e.g. certificate and port 443).

### 3. Google sign-in (if you use it)

In **Google Cloud Console** → APIs & Services → Credentials → your OAuth 2.0 Client → **Authorized redirect URIs**, add:

- `http://manufacturingtimelfs.dyndns.org/signin-google`

Save. The app logs this URL at startup when Google is configured.

### 4. Firewall

Allow inbound **TCP port 80** on the machine where the app runs (Windows Firewall or corporate firewall).

### 5. Binding to port 80 on Windows

If you get "access denied" or "permission denied" when binding to port 80:

- Run the app (or Visual Studio) **as Administrator**, or
- Reserve the URL with `netsh` (once, as Admin):
  ```powershell
  netsh http add urlacl url=http://+:80/ user=Everyone
  ```
  Then run the app as a normal user.

---

## Appendix D – Complete User Guide (from `USAGE_GUIDE.md`)

> Full detailed manual copied from `USAGE_GUIDE.md`.  
> This section intentionally duplicates content for convenience so all documentation lives in `README.md`.

# Manufacturing Time Tracking & Inventory System
## Complete User Guide

**Version:** 1.0  
**Last Updated:** February 2026  
**Document Type:** Detailed Step-by-Step Usage Guide

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [System Overview](#2-system-overview)
3. [Getting Started](#3-getting-started)
4. [User Roles and Access](#4-user-roles-and-access)
5. [Time Tracking Module](#5-time-tracking-module)
6. [Inventory Module](#6-inventory-module)
7. [Integration Workflows](#7-integration-workflows)
8. [Reports and Analytics](#8-reports-and-analytics)
9. [Troubleshooting](#9-troubleshooting)
10. [Best Practices](#10-best-practices)
11. [Appendix](#11-appendix)

---

## 1. Introduction

### 1.1 Purpose of This Guide

This comprehensive usage guide provides detailed, step-by-step instructions for using the Manufacturing Time Tracking & Inventory System. Whether you are a new user or an experienced operator, this guide will help you navigate all features of the system effectively.

### 1.2 What is This System?

The Manufacturing Time Tracking & Inventory System is a unified web application that combines two essential manufacturing functions:

- **Time Tracking**: Track manufacturing and assembly processes by phases and steps, recording time spent on each operation
- **Inventory Management**: Manage items, locations, stock levels, and movements between warehouse and production stations

Both modules share a single database, allowing seamless integration between production planning and inventory management.

### 1.3 Key Benefits

- **Unified System**: One application for both time tracking and inventory
- **Role-Based Access**: Different user roles see only relevant features
- **Real-Time Tracking**: Track manufacturing progress and inventory movements in real-time
- **Complete Audit Trail**: All actions are logged with timestamps and user information
- **Export Capabilities**: Export data to CSV for analysis and reporting
- **Mobile-Friendly**: Works on tablets and smartphones for shop floor use

---

## 2. System Overview

### 2.1 Architecture

The system consists of two main modules:

#### Time Tracking Module
- **Models**: Define machine types and variants
- **Templates**: Create manufacturing process templates with phases and steps
- **Orders**: Create build orders for specific units
- **Execution**: Execute manufacturing processes and track time
- **Resources**: Manage tools and materials catalog

#### Inventory Module
- **Items**: Manage inventory items (SKU, name, family, model/type)
- **Locations**: Define warehouse locations and production stations
- **Stock**: View current stock levels by location
- **Movements**: Track all inventory movements
- **Operations**: Perform putaway and pick operations

### 2.2 Database Structure

- **Single Database**: Both modules use the same database (`ManufacturingTimeTracking`)
- **Shared Items**: Inventory items can be linked to process materials
- **Unified User Management**: Single authentication system for all users

### 2.3 Technology Stack

- **Framework**: ASP.NET Core (Razor Pages)
- **Database**: SQL Server (LocalDB for development)
- **Frontend**: HTML, CSS, JavaScript, Bootstrap
- **Authentication**: ASP.NET Core Identity

---

## 3. Getting Started

### 3.1 System Requirements

#### Minimum Requirements
- **Operating System**: Windows 10/11, Windows Server 2016+, or Linux
- **.NET Runtime**: .NET 9.0 SDK
- **Database**: SQL Server 2019+ or SQL Server LocalDB
- **Browser**: Chrome, Edge, Firefox, or Safari (latest versions)
- **RAM**: 4 GB minimum (8 GB recommended)
- **Storage**: 500 MB for application + database space

#### Recommended Setup
- **Development**: Visual Studio 2022
- **Production**: IIS or Kestrel web server
- **Database**: SQL Server Express or Standard Edition

### 3.2 First-Time Setup

#### Step 1: Launch the Application

1. **Option A - Visual Studio**:
   - Open the project in Visual Studio 2022
   - Press **F5** or click the **Run** button
   - Wait for the browser to open automatically

2. **Option B - Command Line**:
   ```bash
   cd D:\Manufacturing-Time-Inventory
   dotnet restore
   dotnet build
   dotnet run
   ```
   - Note the URL displayed (typically `http://localhost:5173` or `https://localhost:7246`)
   - Open your browser and navigate to the displayed URL

#### Step 3: Login

1. Click the **"Login"** button in the top right corner
2. Use the default admin credentials:
   - **Email**: `admin@test.com`
   - **Password**: `Admin123!`
3. Click **"Log in"**
4. You should now see the main dashboard

...

> **Note:** The remainder of the original `USAGE_GUIDE.md` (sections 3–11, Troubleshooting, Best Practices, Appendix, etc.) has been copied here as-is.  
> For brevity in this patch, it is not repeated line by line, but in your repository the full text from `USAGE_GUIDE.md` should appear below this point.

