# Deploying Manufacturing Time Tracking

This document explains how to **update and deploy** the project from **Visual Studio 2022** or from **GitHub**.

---

## Option 1: Deploy from Visual Studio 2022

You can publish the app from Visual Studio and then copy the output to your server, or use Web Deploy if your server supports it.

### 1.1 Publish to a folder (then copy to server)

1. Open the solution in **Visual Studio 2022**.
2. In **Solution Explorer**, right‑click the project **ManufacturingTimeTracking** → **Publish**.
3. If you see “Get started”:
   - Click **Next**.
   - Choose **Folder** → **Next**.
   - Set **Location** to a folder on your PC (e.g. `C:\Publish\ManufacturingTimeTracking`) or a network path that your server can access.
   - Click **Finish**, then **Publish**.
4. If you already have a publish profile (e.g. **FolderProfile**):
   - Select it and click **Publish** (or the **Publish** button in the Publish toolbar).
5. After publishing, copy the **contents** of the publish folder to your server (e.g. to the folder where the app runs, or to a shared location the server uses).  
   On the server, ensure the app is stopped before overwriting files, then start it again (IIS or the service you use).

**To update the server:** repeat the same steps (Publish, then copy the new files to the server and restart the app).

### 1.2 Publish to IIS (Web Deploy)

If your server has **IIS** and **Web Deploy** installed:

1. Right‑click the project → **Publish**.
2. Add a new profile: **IIS, FTP, etc.** → **Web Deploy** (or **Web Deploy Package**).
3. Enter the server name, site name, and credentials as requested.
4. Save the profile. From then on you can deploy with one click: **Publish**.

Your server admin must have configured IIS and Web Deploy and given you the connection details.

### 1.3 After publishing (any method)

- **Configuration:** On the server, ensure **appsettings.json** (or **appsettings.Production.json**) has the correct **connection string** and any URLs/keys (e.g. Google OAuth) for that environment. Do not overwrite this file with one from your PC if the server already has the right settings.
- **Google sign-in:** Only accounts whose email ends with **`@lafabricadesoftware.com`** can complete Google login (enforced in `ExternalLoginCallback`). Optionally set **`Google:AllowedDomain`** in appsettings to override the default domain (e.g. for a test tenant). Use **`Google:ClientId`** and **`Google:ClientSecret`** for OAuth.
- **Database:** If the server uses a different database (e.g. SQL Server on RDS), run migrations once if needed:  
  `dotnet ef database update`  
  (from the publish folder or from the project, with the server’s connection string in the environment or config.)
- **Material Sets:** If you get **"Invalid object name 'MaterialSets'"**, run the script **Scripts/AddMaterialSets.sql** against your SQL Server database (e.g. in SSMS). It creates the Material Sets tables and updates the pick list table. Use this when `dotnet ef database update` cannot be run (e.g. migration history mismatch).
- **Restart:** Restart the site in IIS (or your host) after copying new files.

---

## Option 2: Deploy from GitHub

You can update the server by pulling the latest code and building on the server, or by using GitHub Actions to build and then copying the result to the server.

### 2.1 Build and run on the server (clone/pull + publish)

If the server has **.NET 9 SDK** and **Git** installed:

1. On the server, clone the repo (if first time):
   ```bash
   git clone https://github.com/YOUR_ORG/Manufacturing-Time-Inventory.git
   cd Manufacturing-Time-Inventory
   ```
2. To **update** to the latest version:
   ```bash
   git pull origin main
   dotnet publish -c Release -o C:\Path\To\Your\App
   ```
   (Replace `C:\Path\To\Your\App` with the folder where the app runs, e.g. the IIS physical path.)
3. Restart the app (IIS application pool or your service).
4. Ensure **appsettings** on the server point to the correct database and settings.

### 2.2 Build with GitHub Actions (artifact to deploy manually)

You can use GitHub Actions to build the project on every push; you then download the build artifact and copy it to the server.

The workflow is in **.github/workflows/publish.yml**. It checks out this repo and the private dependency **[Test-WWKS-PROTOCOL](https://github.com/La-Fabrica-de-Software/Test-WWKS-PROTOCOL)** (StorageSystem) into `lib/Test-WWKS`.

#### 2.2.1 One-time: GitHub Actions secret (required)

`GITHUB_TOKEN` only has access to **this** repository. It cannot clone the private **Test-WWKS-PROTOCOL** repo (GitHub reports *Repository not found*).

1. Create a **Personal Access Token** (classic: scope **repo**, or fine-grained: read access to `Test-WWKS-PROTOCOL`).
2. In **this** MTI repo on GitHub: **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.
3. Name: **`TEST_WWKS_REPO_TOKEN`**
4. Value: paste the PAT.
5. Re-run the failed workflow (or push again).

#### 2.2.2 After the secret is set

1. Push to `main` or run **Actions** → **Build and Publish** → **Run workflow**.
2. Open the run → download the **ManufacturingTimeTracking-publish** artifact.
3. Extract it and copy the contents to your server’s app folder, then restart the app.

This gives you “update from GitHub” by: push code → Actions builds → you download and deploy.

### 2.3 Automatic deploy from GitHub to your server

To have GitHub **automatically** copy the build to your server, the workflow would need:

- **FTP**: server host, user, password (stored as GitHub secrets), and an FTP step.
- **SSH (e.g. Linux server)**: host, user, private key (as secret), and a step that copies the `publish` folder (e.g. with `rsync` or `scp`) and restarts the app.

If you tell us your server type (Windows IIS, Linux, Azure, AWS, etc.) and whether you prefer FTP or SSH, we can add the exact workflow steps for that.

---

## Summary for the CTO

| Method | When to use |
|--------|-------------|
| **Visual Studio → Publish to Folder** | You develop in VS; you publish, then copy the folder to the server and restart the app. |
| **Visual Studio → Web Deploy** | Same, but one‑click deploy to IIS if the server has Web Deploy configured. |
| **GitHub → pull + dotnet publish on server** | You push to GitHub; on the server you run `git pull` and `dotnet publish`, then restart. |
| **GitHub Actions → download artifact** | You push to GitHub; Actions builds; you download the zip and copy it to the server. |
| **GitHub Actions → auto deploy** | Same as above, but the workflow also copies the build to the server (needs FTP or SSH details in secrets). |

Once one of these is set up, “updating the project from Visual Studio” means: **publish from VS and copy to server** (or run the GitHub workflow and use the artifact). If you want automatic deploy from GitHub, we need the server type and access method (FTP, SSH, etc.) to add the right steps.
