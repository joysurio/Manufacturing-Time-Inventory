<# 
  tools.ps1

  Combined utility script for the Manufacturing Time Tracking & Inventory project.
  This replaces:
    - view-database.ps1
    - open-firewall-ports.ps1
    - fix-navicat-connection.ps1

  Run this script in PowerShell. For best results, start PowerShell as Administrator
  when using firewall-related actions.
#>

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Manufacturing Time Tracking – Tools" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Show-MainMenu {
    Write-Host "Select an option:" -ForegroundColor Yellow
    Write-Host "1. View LocalDB database (tables & sample queries)"
    Write-Host "2. Fix LocalDB / Navicat connection"
    Write-Host "3. Open firewall ports for dev HTTP/HTTPS"
    Write-Host "4. Storage Pick / IT.Storage dev test (robot + smoke + MTI)"
    Write-Host "Q. Quit"
    Write-Host ""
}

function Invoke-DbViewer {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "ManufacturingTimeTracking Database Viewer" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    # Check if LocalDB is running
    Write-Host "Checking LocalDB status..." -ForegroundColor Yellow
    $null = sqllocaldb info MSSQLLocalDB 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "LocalDB is not running. Starting LocalDB..." -ForegroundColor Yellow
        sqllocaldb start MSSQLLocalDB
        Start-Sleep -Seconds 2
    }

    Write-Host "LocalDB is running!" -ForegroundColor Green
    Write-Host ""

    # Connection string pieces
    $server = "(localdb)\mssqllocaldb"
    $database = "ManufacturingTimeTracking"

    Write-Host "Connecting to: $server" -ForegroundColor Cyan
    Write-Host "Database: $database" -ForegroundColor Cyan
    Write-Host ""

    function Run-Query {
        param([string]$query)
        sqlcmd -S $server -d $database -E -Q $query -W -s "," -h -1
    }

    Write-Host "Select an option:" -ForegroundColor Yellow
    Write-Host "1. View all StepTemplates"
    Write-Host "2. View all ProcessTemplates"
    Write-Host "3. View all PhaseTemplates"
    Write-Host "4. View all Materials"
    Write-Host "5. View all Tools"
    Write-Host "6. View Steps with their Phase and Process"
    Write-Host "7. View Steps with Tools"
    Write-Host "8. View Steps with Materials"
    Write-Host "9. Custom SQL Query"
    Write-Host "0. Back to main menu"
    Write-Host ""

    $choice = Read-Host "Enter your choice (0-9)"

    switch ($choice) {
        "1" {
            Write-Host "`nStepTemplates:" -ForegroundColor Green
            Run-Query "SELECT * FROM StepTemplates ORDER BY Id"
        }
        "2" {
            Write-Host "`nProcessTemplates:" -ForegroundColor Green
            Run-Query "SELECT * FROM ProcessTemplates ORDER BY Id"
        }
        "3" {
            Write-Host "`nPhaseTemplates:" -ForegroundColor Green
            Run-Query "SELECT * FROM PhaseTemplates ORDER BY ProcessTemplateId, SortOrder"
        }
        "4" {
            Write-Host "`nMaterials:" -ForegroundColor Green
            Run-Query "SELECT * FROM Materials ORDER BY Name"
        }
        "5" {
            Write-Host "`nTools:" -ForegroundColor Green
            Run-Query "SELECT * FROM Tools ORDER BY Name"
        }
        "6" {
            Write-Host "`nSteps with Phase and Process:" -ForegroundColor Green
            $query = @"
SELECT 
    st.Id,
    st.Title,
    st.Instructions,
    st.AllowSkip,
    pt.Name AS PhaseName,
    prt.Name AS ProcessTemplateName
FROM StepTemplates st
INNER JOIN PhaseTemplates pt ON st.PhaseTemplateId = pt.Id
INNER JOIN ProcessTemplates prt ON pt.ProcessTemplateId = prt.Id
ORDER BY prt.Name, pt.SortOrder, st.SortOrder
"@
            Run-Query $query
        }
        "7" {
            Write-Host "`nSteps with Tools:" -ForegroundColor Green
            $query = @"
SELECT 
    st.Id AS StepId,
    st.Title AS StepTitle,
    t.Name AS ToolName
FROM StepTemplates st
INNER JOIN StepTemplateTools stt ON st.Id = stt.StepTemplateId
INNER JOIN Tools t ON stt.ToolId = t.Id
ORDER BY st.Id
"@
            Run-Query $query
        }
        "8" {
            Write-Host "`nSteps with Materials:" -ForegroundColor Green
            $query = @"
SELECT 
    st.Id AS StepId,
    st.Title AS StepTitle,
    m.Name AS MaterialName,
    stm.Qty AS Quantity
FROM StepTemplates st
INNER JOIN StepTemplateMaterials stm ON st.Id = stm.StepTemplateId
INNER JOIN Materials m ON stm.MaterialId = m.Id
ORDER BY st.Id
"@
            Run-Query $query
        }
        "9" {
            Write-Host "`nEnter your SQL query" -ForegroundColor Yellow
            Write-Host "Example: SELECT * FROM StepTemplates WHERE Id = 1" -ForegroundColor Gray
            $customQuery = Read-Host "Query"
            Run-Query $customQuery
        }
        default {
            Write-Host "`nReturning to main menu..." -ForegroundColor Yellow
        }
    }
}

function Invoke-NavicatFix {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Navicat / LocalDB Connection Fix" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    # Step 1: Check LocalDB status
    Write-Host "Step 1: Checking LocalDB status..." -ForegroundColor Yellow
    $null = sqllocaldb info MSSQLLocalDB 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "LocalDB instance not found. Creating..." -ForegroundColor Yellow
        sqllocaldb create MSSQLLocalDB
        Start-Sleep -Seconds 2
    }

    Write-Host "Starting LocalDB..." -ForegroundColor Yellow
    sqllocaldb start MSSQLLocalDB
    Start-Sleep -Seconds 3

    $status = sqllocaldb info MSSQLLocalDB
    if ($status -match "State: Running") {
        Write-Host "✓ LocalDB is running" -ForegroundColor Green
    } else {
        Write-Host "✗ LocalDB failed to start" -ForegroundColor Red
        Write-Host "Status: $status" -ForegroundColor Red
        return
    }

    Write-Host ""

    # Step 2: Check if database exists
    Write-Host "Step 2: Checking if database exists..." -ForegroundColor Yellow

    $query = "SELECT name FROM sys.databases WHERE name = 'ManufacturingTimeTracking'"
    $result = sqlcmd -S "(localdb)\mssqllocaldb" -E -Q $query -h -1 2>&1

    if ($result -match "ManufacturingTimeTracking") {
        Write-Host "✓ Database 'ManufacturingTimeTracking' exists" -ForegroundColor Green
    } else {
        Write-Host "✗ Database 'ManufacturingTimeTracking' does NOT exist" -ForegroundColor Red
        Write-Host ""
        Write-Host "Creating database..." -ForegroundColor Yellow

        $createDbQuery = "CREATE DATABASE ManufacturingTimeTracking;"
        sqlcmd -S "(localdb)\mssqllocaldb" -E -Q $createDbQuery 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Database created successfully" -ForegroundColor Green
        } else {
            Write-Host "✗ Failed to create database" -ForegroundColor Red
            Write-Host "You may need to run your application first to create the database." -ForegroundColor Yellow
        }
    }

    Write-Host ""

    # Step 3: Test connection
    Write-Host "Step 3: Testing connection..." -ForegroundColor Yellow

    $testQuery = "SELECT @@VERSION"
    $testResult = sqlcmd -S "(localdb)\mssqllocaldb" -d "ManufacturingTimeTracking" -E -Q $testQuery -h -1 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Connection test successful!" -ForegroundColor Green
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "Connection Settings for Navicat:" -ForegroundColor Cyan
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Host: (localdb)\mssqllocaldb" -ForegroundColor White
        Write-Host "Initial Database: ManufacturingTimeTracking" -ForegroundColor White
        Write-Host "Authentication: Windows Authentication" -ForegroundColor White
        Write-Host "Username: (leave blank)" -ForegroundColor White
        Write-Host "Password: (leave blank)" -ForegroundColor White
        Write-Host ""
    } else {
        Write-Host "✗ Connection test failed" -ForegroundColor Red
        Write-Host "Error: $testResult" -ForegroundColor Red
        Write-Host ""
        Write-Host "Try these solutions:" -ForegroundColor Yellow
        Write-Host "1. Run your application first to create the database" -ForegroundColor White
        Write-Host "2. Check if SQL Server services are running" -ForegroundColor White
        Write-Host "3. Try connecting to 'master' database first" -ForegroundColor White
    }
}

function Invoke-OpenFirewallPorts {
    Write-Host "Opening firewall ports for Manufacturing Time Tracking application..." -ForegroundColor Green

    # Open HTTP port 5173
    New-NetFirewallRule -DisplayName "Manufacturing Time Tracking HTTP" -Direction Inbound -LocalPort 5173 -Protocol TCP -Action Allow -ErrorAction SilentlyContinue
    Write-Host "HTTP port 5173 opened" -ForegroundColor Yellow

    # Open HTTPS port 7245
    New-NetFirewallRule -DisplayName "Manufacturing Time Tracking HTTPS" -Direction Inbound -LocalPort 7245 -Protocol TCP -Action Allow -ErrorAction SilentlyContinue
    Write-Host "HTTPS port 7245 opened" -ForegroundColor Yellow

    Write-Host "`nFirewall ports configured successfully!" -ForegroundColor Green
    Write-Host "You can now access the application from other devices on your network." -ForegroundColor Green
    Write-Host "`nTo find your IP address, run: ipconfig" -ForegroundColor Cyan
    Write-Host "Then access the app using: http://[YOUR_IP]:5173 or https://[YOUR_IP]:7245" -ForegroundColor Cyan
}

do {
    Show-MainMenu
    $mainChoice = Read-Host "Enter your choice (1-4 or Q)"

    switch ($mainChoice.ToUpper()) {
        "1" { Invoke-DbViewer }
        "2" { Invoke-NavicatFix }
        "3" { Invoke-OpenFirewallPorts }
        "4" {
            $script = Join-Path $PSScriptRoot "scripts\run-storage-pick-dev.ps1"
            if (Test-Path $script) {
                & $script
            } else {
                Write-Host "Script not found: $script" -ForegroundColor Red
            }
        }
        "Q" { Write-Host "`nExiting tools..." -ForegroundColor Yellow }
        default { Write-Host "Invalid choice. Please try again." -ForegroundColor Red }
    }

    if ($mainChoice.ToUpper() -ne "Q") {
        Write-Host "`nPress any key to return to main menu..." -ForegroundColor DarkGray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        Write-Host ""
    }
} while ($mainChoice.ToUpper() -ne "Q")

