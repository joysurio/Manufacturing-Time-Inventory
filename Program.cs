using System.Net;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using QuestPDF;
using QuestPDF.Infrastructure;

// Validate appsettings JSON files early to provide a clearer error message when malformed
try
{
    var basePath = Directory.GetCurrentDirectory();
    var filesToCheck = new[] { Path.Combine(basePath, "appsettings.json"), Path.Combine(basePath, $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json") };
    foreach (var f in filesToCheck)
    {
        if (File.Exists(f))
        {
            var txt = File.ReadAllText(f);
            // Attempt to parse
            using var _ = JsonDocument.Parse(txt);
        }
    }
}
catch (JsonException jex)
{
    Console.Error.WriteLine("Configuration JSON parse error: " + jex.Message);
    throw new InvalidDataException("Invalid JSON in configuration file. See inner exception for details.", jex);
}

var builder = WebApplication.CreateBuilder(args);

// Configure QuestPDF license mode (Community) to avoid runtime license exception when generating PDFs.
QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// In Development use a short timeout (10s) so startup fails fast when the remote DB is unreachable.
// In Production use a longer timeout (60s) for slow or cross-region SQL Server connections.
var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
var targetTimeout = builder.Environment.IsDevelopment() ? 10 : 60;
if (connectionBuilder.ConnectTimeout != targetTimeout)
    connectionBuilder.ConnectTimeout = targetTimeout;
connectionString = connectionBuilder.ConnectionString;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServerOptions =>
        sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

builder.Services.AddMemoryCache();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => 
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    
    // User settings
    options.User.RequireUniqueEmail = true;
    
    // Sign in settings
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Google external login (ClientId/ClientSecret in appsettings; domain restriction in ExternalLoginCallback, default @lafabricadesoftware.com)
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
}

// Allow auth cookies to work over plain HTTP (production uses http://manufacturingtimelfs.dyndns.org).
// Without this, SameSite=None cookies require Secure and browsers drop them over HTTP → "Correlation failed".
builder.Services.ConfigureApplicationCookie(options =>
{
    // If not authenticated (or session expired), show a dedicated page that asks the user to log in.
    options.LoginPath = "/Account/SessionRequired";
    options.AccessDeniedPath = "/Account/SessionRequired";

    // Session settings (2-hour login session, sliding)
    options.ExpireTimeSpan = TimeSpan.FromMinutes(120);
    options.SlidingExpiration = true;

    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.ConfigureExternalCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization(options =>
{
    // Inventory menu: only users with Inventory or Admin role
    options.AddPolicy("CanUseInventory", p => p.RequireRole("Inventory", "Admin"));
    // Time management menu: Operator, Supervisor, Admin, Normal (Normal has restricted actions)
    options.AddPolicy("CanUseTimeManagement", p => p.RequireRole("Operator", "Supervisor", "Admin", "Normal"));
    // Admin-only: manage users and roles
    options.AddPolicy("CanManageUsers", p => p.RequireRole("Admin"));
    // Who can add/edit steps, templates, and add instructions/evidence (Admin, Supervisor, Operator; not Normal)
    options.AddPolicy("CanEditSteps", p => p.RequireRole("Admin", "Supervisor", "Operator"));
    // Who can see prices (when added to components later)
    options.AddPolicy("CanSeePrices", p => p.RequireRole("Admin"));
});

builder.Services.AddRazorPages(options =>
{
    // Razor Pages: require login for the entire app by default.
    options.Conventions.AuthorizeFolder("/");
    // Allow account pages (Login/Register/ExternalLogin/Callbacks/Logout) to be accessed without being logged in.
    options.Conventions.AllowAnonymousToFolder("/Account");
});
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ManufacturingTimeTracking.Services.IBuildOrderNumberService, ManufacturingTimeTracking.Services.BuildOrderNumberService>();
builder.Services.AddScoped<ManufacturingTimeTracking.Services.InventoryService>();
builder.Services.AddScoped<ManufacturingTimeTracking.Services.OpenAIVisionService>();
builder.Services.AddScoped<ManufacturingTimeTracking.Services.GptReportService>();
builder.Services.Configure<ManufacturingTimeTracking.Services.StorageSystem.StorageSystemOptions>(
    builder.Configuration.GetSection(ManufacturingTimeTracking.Services.StorageSystem.StorageSystemOptions.SectionName));
builder.Services.AddScoped<ManufacturingTimeTracking.Services.StorageSystem.IStorageSystemClient,
    ManufacturingTimeTracking.Services.StorageSystem.StorageSystemClient>();
builder.Services.AddHttpClient();
// Email service
builder.Services.AddTransient<ManufacturingTimeTracking.Services.IEmailService, ManufacturingTimeTracking.Services.SmtpEmailService>();

// Add forwarded headers support for reverse proxies (e.g. Caddy gateway).
// Required so redirects and links use the public URL, not localhost.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    options.ForwardLimit = 1;
});

// Configure Kestrel ports.
// In Production the app binds to port 80 (via the ASPNETCORE_URLS env var set on the server).
// In Development we let launchSettings.json / ASPNETCORE_URLS control the ports so VS can
// use safe non-privileged ports (7150/7151) without requiring admin rights.
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(5173); // HTTP (gateway profile)
        options.ListenAnyIP(80);   // HTTP port 80 (production: manufacturingtimelfs.dyndns.org)
    });
}

var app = builder.Build();

// Log Google OAuth redirect URIs for Google Cloud Console (APIs & Services > Credentials > OAuth 2.0 Client).
// Use exactly /signin-google (not signin-googl). Add each URL you use (localhost, production).
if (!string.IsNullOrEmpty(builder.Configuration["Google:ClientId"]))
{
    var pathBaseForGoogle = (builder.Configuration["PathBase"] ?? builder.Configuration["ASPNETCORE_PATHBASE"] ?? "").Trim().TrimEnd('/');
    var pathSuffix = string.IsNullOrEmpty(pathBaseForGoogle) ? "" : "/" + pathBaseForGoogle.TrimStart('/');
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Google OAuth: Add these Authorized redirect URIs in Google Cloud Console (exact path: /signin-google):");
    logger.LogInformation("  http://manufacturingtimelfs.dyndns.org{PathBase}/signin-google", pathSuffix);
    logger.LogInformation("  http://localhost{PathBase}/signin-google", pathSuffix);
    logger.LogInformation("  http://localhost:5173{PathBase}/signin-google", pathSuffix);
    if (!string.IsNullOrEmpty(pathSuffix))
        logger.LogInformation("  (When using PathBase, also add your production base URL + {PathBase}/signin-google", pathSuffix);
}

// Forwarded headers MUST run first so the app sees the real host/scheme when behind a proxy.
// Otherwise you get 307 redirects to localhost and "can't access with domain".
app.UseForwardedHeaders();

// When behind LFSInventario gateway, app is served at /manufacturing (path base from config)
var pathBase = builder.Configuration["PathBase"] ?? builder.Configuration["ASPNETCORE_PATHBASE"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    pathBase = pathBase.Trim().TrimEnd('/');
    if (!string.IsNullOrEmpty(pathBase))
        app.UsePathBase("/" + pathBase.TrimStart('/'));
}

// Development-only debugging middleware was removed.

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// HTTPS redirect: skip when behind a proxy (PathBase set) or when using HTTP-only (e.g. dyndns on port 80).
var useHttpsRedirection = builder.Configuration.GetValue("UseHttpsRedirection", true);
if (string.IsNullOrWhiteSpace(pathBase) && useHttpsRedirection)
    app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();

// Ensure database is created and seeded
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        
        // Try to apply migrations, or create database if no migrations exist
        var migrationFailedDueToExistingTables = false;
        try
        {
            context.Database.Migrate();
        }
        catch (InvalidOperationException)
        {
            context.Database.EnsureCreated();
        }
        catch (SqlException ex) when (ex.Number == 5170)
        {
            // File already exists: orphaned MDF/LDF from another machine or previous run. See README "Database file already exists".
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError("Database creation failed: the data file already exists (e.g. at C:\\Users\\<You>\\ManufacturingTimeTracking_Dev.mdf). " +
                "Delete the .mdf and .ldf files for this database from that folder (close the app first), then run the app again.");
            throw;
        }
        catch (SqlException ex) when (ex.Number == 2714 || ex.Number == 2715) // Object already exists / Duplicate object
        {
            migrationFailedDueToExistingTables = true;
        }
        catch (SqlException)
        {
            // Any other SQL error during Migrate (e.g. column already exists, invalid column). Still run ensure-SQL below to fix schema.
            migrationFailedDueToExistingTables = true;
        }

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            if (migrationFailedDueToExistingTables)
            {
                // Database already had tables (e.g. from EnsureCreated or previous deploy).
                // Mark known migrations as applied so EF stops trying to re-run them on every startup.
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309212429_InitialCreate')
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260309212429_InitialCreate', N'9.0.0');
                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260209161308_AddInventory')
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260209161308_AddInventory', N'9.0.0');";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Ensure newly added migration entries exist so EF won't try to re-run migrations
                using (var cmd2 = connection.CreateCommand())
                {
                    cmd2.CommandText = @"
                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260425121000_AddHiddenToStepTemplate')
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260425121000_AddHiddenToStepTemplate', N'9.0.0');
                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260425122000_AddHiddenToStepExec')
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260425122000_AddHiddenToStepExec', N'9.0.0');";
                    await cmd2.ExecuteNonQueryAsync();
                }
            }

            // Always ensure inventory tables (Locations, Items, etc.) so the app works after clone/pull on another machine.
            await DatabaseStartup.EnsureInventorySchemaAsync(connection, services.GetRequiredService<ILogger<Program>>());

            // Add ImageUrl to Materials and MaterialId to Items if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'ImageUrl')
                    BEGIN
                        ALTER TABLE [Materials] ADD [ImageUrl] NVARCHAR(MAX) NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure Hidden column exists on StepTemplates and StepExecs (in case migrations were not applied)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'StepTemplates')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepTemplates]') AND name = 'Hidden')
                    BEGIN
                        ALTER TABLE [dbo].[StepTemplates] ADD [Hidden] bit NOT NULL DEFAULT 0;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'StepExecs')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepExecs]') AND name = 'Hidden')
                    BEGIN
                        ALTER TABLE [dbo].[StepExecs] ADD [Hidden] bit NOT NULL DEFAULT 0;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure StepTemplateId column exists on StepExecs (nullable foreign key to templates)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'StepExecs')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepExecs]') AND name = 'StepTemplateId')
                    BEGIN
                        ALTER TABLE [dbo].[StepExecs] ADD [StepTemplateId] int NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Items')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND name = 'MaterialId')
                    BEGIN
                        ALTER TABLE [dbo].[Items] ADD [MaterialId] INT NULL;
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Items_Materials_MaterialId')
                            ALTER TABLE [dbo].[Items] ADD CONSTRAINT [FK_Items_Materials_MaterialId]
                                FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE SET NULL;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Items_MaterialId' AND object_id = OBJECT_ID(N'[dbo].[Items]'))
                            CREATE NONCLUSTERED INDEX [IX_Items_MaterialId] ON [dbo].[Items] ([MaterialId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Items')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND name = 'ModelOrType')
                    BEGIN
                        ALTER TABLE [dbo].[Items] ADD [ModelOrType] NVARCHAR(64) NOT NULL DEFAULT '';
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure OrderPickLines exists (pick list for orders)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[OrderPickLines]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[OrderPickLines] (
                            [Id] int NOT NULL IDENTITY,
                            [BuildOrderId] int NOT NULL,
                            [ItemId] int NOT NULL,
                            [QuantityRequired] int NOT NULL,
                            [QuantityPicked] int NOT NULL DEFAULT 0,
                            CONSTRAINT [PK_OrderPickLines] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_OrderPickLines_BuildOrders_BuildOrderId] FOREIGN KEY ([BuildOrderId]) REFERENCES [dbo].[BuildOrders] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_OrderPickLines_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE NO ACTION
                        );
                        CREATE INDEX [IX_OrderPickLines_BuildOrderId] ON [dbo].[OrderPickLines] ([BuildOrderId]);
                        CREATE UNIQUE INDEX [IX_OrderPickLines_BuildOrderId_ItemId] ON [dbo].[OrderPickLines] ([BuildOrderId], [ItemId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure OrderMaterialPickLines exists (manual material pick tracking)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[OrderMaterialPickLines]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[OrderMaterialPickLines] (
                            [Id] int NOT NULL IDENTITY,
                            [BuildOrderId] int NOT NULL,
                            [ComponentId] int NOT NULL,
                            [MaterialId] int NOT NULL,
                            [QuantityRequired] int NOT NULL,
                            [QuantityPicked] int NOT NULL DEFAULT 0,
                            CONSTRAINT [PK_OrderMaterialPickLines] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_OrderMaterialPickLines_BuildOrders_BuildOrderId] FOREIGN KEY ([BuildOrderId]) REFERENCES [dbo].[BuildOrders] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_OrderMaterialPickLines_BuildOrderId] ON [dbo].[OrderMaterialPickLines] ([BuildOrderId]);
                        CREATE UNIQUE INDEX [IX_OrderMaterialPickLines_BuildOrderId_ComponentId] ON [dbo].[OrderMaterialPickLines] ([BuildOrderId], [ComponentId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure BlockedReasons table and BuildOrder blocked columns (migration AddBlockedStateToBuildOrder)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[BlockedReasons]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[BlockedReasons] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [Name] nvarchar(max) NOT NULL,
                            [Active] bit NOT NULL DEFAULT 1,
                            CONSTRAINT [PK_BlockedReasons] PRIMARY KEY ([Id])
                        );
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Time tracking: BreakReasons and TimeTrackingEntries
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[BreakReasons]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[BreakReasons] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [Name] nvarchar(max) NOT NULL,
                            [Description] nvarchar(max) NULL,
                            [Active] bit NOT NULL DEFAULT 1,
                            CONSTRAINT [PK_BreakReasons] PRIMARY KEY ([Id])
                        );
                        INSERT INTO [dbo].[BreakReasons] ([Name], [Description], [Active]) VALUES
                        (N'Lunch', N'Lunch break', 1),
                        (N'Coffee break', N'Short coffee or rest break', 1),
                        (N'Personal', N'Personal matters', 1),
                        (N'Technical problem', N'Technical issue or equipment', 1),
                        (N'Meeting', N'Meeting or coordination', 1),
                        (N'Training', N'Training or learning', 1),
                        (N'Other', N'Other reason', 1);
                    END
                    IF OBJECT_ID(N'[dbo].[BreakReasons]', N'U') IS NOT NULL AND (SELECT COUNT(*) FROM [dbo].[BreakReasons]) = 0
                    BEGIN
                        INSERT INTO [dbo].[BreakReasons] ([Name], [Description], [Active]) VALUES
                        (N'Lunch', N'Lunch break', 1),
                        (N'Coffee break', N'Short coffee or rest break', 1),
                        (N'Personal', N'Personal matters', 1),
                        (N'Technical problem', N'Technical issue or equipment', 1),
                        (N'Meeting', N'Meeting or coordination', 1),
                        (N'Training', N'Training or learning', 1),
                        (N'Other', N'Other reason', 1);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[TimeTrackingEntries]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[TimeTrackingEntries] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [UserId] nvarchar(450) NOT NULL,
                            [ActionType] nvarchar(max) NOT NULL,
                            [BreakReasonId] int NULL,
                            [TimestampUtc] datetime2 NOT NULL,
                            [SessionId] uniqueidentifier NULL,
                            CONSTRAINT [PK_TimeTrackingEntries] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_TimeTrackingEntries_BreakReasons_BreakReasonId] FOREIGN KEY ([BreakReasonId]) REFERENCES [dbo].[BreakReasons] ([Id]) ON DELETE SET NULL
                        );
                        CREATE INDEX [IX_TimeTrackingEntries_BreakReasonId] ON [dbo].[TimeTrackingEntries] ([BreakReasonId]);
                        CREATE INDEX [IX_TimeTrackingEntries_SessionId] ON [dbo].[TimeTrackingEntries] ([SessionId]);
                        CREATE INDEX [IX_TimeTrackingEntries_UserId_TimestampUtc] ON [dbo].[TimeTrackingEntries] ([UserId], [TimestampUtc]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[BuildOrders]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BuildOrders]') AND name = 'IsBlocked')
                    BEGIN
                        ALTER TABLE [dbo].[BuildOrders] ADD [IsBlocked] bit NOT NULL DEFAULT 0;
                        ALTER TABLE [dbo].[BuildOrders] ADD [BlockedReasonId] int NULL;
                        ALTER TABLE [dbo].[BuildOrders] ADD [BlockedNotes] nvarchar(max) NULL;
                        ALTER TABLE [dbo].[BuildOrders] ADD [BlockedAt] datetime2 NULL;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BuildOrders_BlockedReasonId' AND object_id = OBJECT_ID(N'[dbo].[BuildOrders]'))
                            CREATE INDEX [IX_BuildOrders_BlockedReasonId] ON [dbo].[BuildOrders] ([BlockedReasonId]);
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_BuildOrders_BlockedReasons_BlockedReasonId')
                            ALTER TABLE [dbo].[BuildOrders] ADD CONSTRAINT [FK_BuildOrders_BlockedReasons_BlockedReasonId]
                                FOREIGN KEY ([BlockedReasonId]) REFERENCES [dbo].[BlockedReasons] ([Id]) ON DELETE NO ACTION;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure BuildOrder planning columns (migrations AddBuildOrderPlanningFields, AddStandardHoursAndPlannedWeek)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[BuildOrders]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BuildOrders]') AND name = 'CustomerName')
                    BEGIN
                        ALTER TABLE [dbo].[BuildOrders] ADD [DueDate] datetime2 NULL;
                        ALTER TABLE [dbo].[BuildOrders] ADD [Priority] int NOT NULL DEFAULT 2;
                        ALTER TABLE [dbo].[BuildOrders] ADD [CustomerName] nvarchar(max) NOT NULL DEFAULT '';
                        ALTER TABLE [dbo].[BuildOrders] ADD [ProjectRef] nvarchar(max) NULL;
                        ALTER TABLE [dbo].[BuildOrders] ADD [OwnerUserId] nvarchar(450) NOT NULL DEFAULT '';
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BuildOrders_DueDate' AND object_id = OBJECT_ID(N'[dbo].[BuildOrders]'))
                            CREATE INDEX [IX_BuildOrders_DueDate] ON [dbo].[BuildOrders] ([DueDate]);
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BuildOrders_OwnerUserId' AND object_id = OBJECT_ID(N'[dbo].[BuildOrders]'))
                            CREATE INDEX [IX_BuildOrders_OwnerUserId] ON [dbo].[BuildOrders] ([OwnerUserId]);
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BuildOrders_Priority' AND object_id = OBJECT_ID(N'[dbo].[BuildOrders]'))
                            CREATE INDEX [IX_BuildOrders_Priority] ON [dbo].[BuildOrders] ([Priority]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[BuildOrders]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BuildOrders]') AND name = 'PlannedWeek')
                    BEGIN
                        ALTER TABLE [dbo].[BuildOrders] ADD [PlannedWeek] nvarchar(20) NOT NULL DEFAULT '';
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure MachineModels.StandardHours (migration AddStandardHoursAndPlannedWeek)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MachineModels]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MachineModels]') AND name = 'StandardHours')
                    BEGIN
                        ALTER TABLE [dbo].[MachineModels] ADD [StandardHours] decimal(18,2) NOT NULL DEFAULT 1;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure ProductionInterruptions table (migration AddProductionInterruption)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[ProductionInterruptions]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[ProductionInterruptions] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [BuildOrderId] int NOT NULL,
                            [Category] int NOT NULL,
                            [StartTime] datetime2 NOT NULL,
                            [EndTime] datetime2 NULL,
                            [Notes] nvarchar(max) NOT NULL,
                            [CreatedByUserId] nvarchar(450) NOT NULL,
                            CONSTRAINT [PK_ProductionInterruptions] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_ProductionInterruptions_BuildOrders_BuildOrderId] FOREIGN KEY ([BuildOrderId]) REFERENCES [dbo].[BuildOrders] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_ProductionInterruptions_BuildOrderId] ON [dbo].[ProductionInterruptions] ([BuildOrderId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure Suppliers table and Material fields (QR, Price, SupplierId)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[Suppliers]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[Suppliers] (
                            [Id] int NOT NULL IDENTITY,
                            [Name] nvarchar(max) NOT NULL,
                            [Code] nvarchar(max) NULL,
                            CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
                        );
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'Qr')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [Qr] nvarchar(max) NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'Price')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [Price] decimal(18,2) NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'SupplierId')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [SupplierId] int NULL;
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Materials_Suppliers_SupplierId')
                            ALTER TABLE [dbo].[Materials] ADD CONSTRAINT [FK_Materials_Suppliers_SupplierId]
                                FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE SET NULL;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Materials_SupplierId' AND object_id = OBJECT_ID(N'[dbo].[Materials]'))
                            CREATE INDEX [IX_Materials_SupplierId] ON [dbo].[Materials] ([SupplierId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND EXISTS (SELECT * FROM sys.tables WHERE name = 'Locations')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'LocationId')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [LocationId] int NULL;
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Materials_Locations_LocationId')
                            ALTER TABLE [dbo].[Materials] ADD CONSTRAINT [FK_Materials_Locations_LocationId]
                                FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE SET NULL;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Materials_LocationId' AND object_id = OBJECT_ID(N'[dbo].[Materials]'))
                            CREATE INDEX [IX_Materials_LocationId] ON [dbo].[Materials] ([LocationId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'Reference')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [Reference] nvarchar(256) NULL;
                    END
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Materials')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'Notes')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [Notes] nvarchar(max) NULL;
                    END
                    IF OBJECT_ID(N'[dbo].[Materials]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Materials]') AND name = 'MinimumStock')
                    BEGIN
                        ALTER TABLE [dbo].[Materials] ADD [MinimumStock] int NOT NULL DEFAULT 0;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MaterialStocks]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[MaterialStocks] (
                            [Id] int NOT NULL IDENTITY,
                            [MaterialId] int NOT NULL,
                            [LocationId] int NOT NULL,
                            [Quantity] int NOT NULL DEFAULT 0,
                            [UpdatedAtUtc] datetime2 NOT NULL,
                            CONSTRAINT [PK_MaterialStocks] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_MaterialStocks_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_MaterialStocks_Locations_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION
                        );
                        CREATE UNIQUE INDEX [IX_MaterialStocks_MaterialId_LocationId] ON [dbo].[MaterialStocks] ([MaterialId], [LocationId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MaterialStockMovements]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[MaterialStockMovements] (
                            [Id] int NOT NULL IDENTITY,
                            [MaterialId] int NOT NULL,
                            [LocationId] int NOT NULL,
                            [QuantityDelta] int NOT NULL,
                            [Type] nvarchar(32) NOT NULL,
                            [Notes] nvarchar(512) NULL,
                            [PerformedBy] nvarchar(256) NOT NULL,
                            [TimestampUtc] datetime2 NOT NULL,
                            CONSTRAINT [PK_MaterialStockMovements] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_MaterialStockMovements_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_MaterialStockMovements_Locations_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION
                        );
                        CREATE INDEX [IX_MaterialStockMovements_MaterialId] ON [dbo].[MaterialStockMovements] ([MaterialId]);
                        CREATE INDEX [IX_MaterialStockMovements_LocationId] ON [dbo].[MaterialStockMovements] ([LocationId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Material Out: add BuildOrderId and DestinationClient to MaterialStockMovements if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MaterialStockMovements]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialStockMovements]') AND name = 'BuildOrderId')
                    BEGIN
                        ALTER TABLE [dbo].[MaterialStockMovements] ADD [BuildOrderId] int NULL;
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_MaterialStockMovements_BuildOrders_BuildOrderId')
                            ALTER TABLE [dbo].[MaterialStockMovements] ADD CONSTRAINT [FK_MaterialStockMovements_BuildOrders_BuildOrderId]
                                FOREIGN KEY ([BuildOrderId]) REFERENCES [dbo].[BuildOrders] ([Id]) ON DELETE SET NULL;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MaterialStockMovements_BuildOrderId' AND object_id = OBJECT_ID(N'[dbo].[MaterialStockMovements]'))
                            CREATE INDEX [IX_MaterialStockMovements_BuildOrderId] ON [dbo].[MaterialStockMovements] ([BuildOrderId]);
                    END
                    IF OBJECT_ID(N'[dbo].[MaterialStockMovements]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaterialStockMovements]') AND name = 'DestinationClient')
                    BEGIN
                        ALTER TABLE [dbo].[MaterialStockMovements] ADD [DestinationClient] nvarchar(256) NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Fix typo: rename Materialld to MaterialId if it exists (EF expects MaterialId)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MachineModelComponents]', N'U') IS NOT NULL
                    AND EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]') AND name = 'Materialld')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]') AND name = 'MaterialId')
                    BEGIN
                        EXEC sp_rename 'dbo.MachineModelComponents.Materialld', 'MaterialId', 'COLUMN';
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure MachineModelComponents has MaterialId (components use materials)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MachineModelComponents]', N'U') IS NOT NULL
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]') AND name = 'MaterialId')
                    BEGIN
                        ALTER TABLE [dbo].[MachineModelComponents] ADD [MaterialId] int NULL;
                        IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_MachineModelComponents_Materials_MaterialId')
                            ALTER TABLE [dbo].[MachineModelComponents] ADD CONSTRAINT [FK_MachineModelComponents_Materials_MaterialId]
                                FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE NO ACTION;
                        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MachineModelComponents_MaterialId' AND object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]'))
                            CREATE INDEX [IX_MachineModelComponents_MaterialId] ON [dbo].[MachineModelComponents] ([MaterialId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MachineModelComponents]', N'U') IS NOT NULL
                    AND EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]') AND name = 'ItemId')
                    BEGIN
                        ALTER TABLE [dbo].[MachineModelComponents] ALTER COLUMN [ItemId] int NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure AiPrompts table (migration AddAiPrompts)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[AiPrompts]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[AiPrompts] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [Name] nvarchar(max) NOT NULL,
                            [Description] nvarchar(max) NULL,
                            [PromptText] nvarchar(max) NOT NULL,
                            [LastUpdate] datetime2 NOT NULL,
                            CONSTRAINT [PK_AiPrompts] PRIMARY KEY ([Id])
                        );
                        INSERT INTO [dbo].[AiPrompts] ([Name], [Description], [PromptText], [LastUpdate]) VALUES
                        (N'Prompt_Template_Report', N'Activity analysis report',
                         N'You are an expert manufacturing analyst. Analyze the following activity data from a manufacturing management system and produce a structured report in English.

DATA:
{DATA}

Produce a clear, professional report with the following sections:
1. **Executive Summary** - brief overview of activity in the period
2. **Key Metrics** - total orders, steps completed, average duration, active technicians
3. **Main Incidents & Issues** - any blocked orders, incomplete steps, or anomalies
4. **Repeated Failures / Patterns** - machines or steps that repeatedly appear in issues
5. **Top Machines by Activity** - which machines had the most work
6. **Technician Performance Overview** - brief summary per technician (time logged, steps done)
7. **Recommendations** - concrete actions to improve efficiency

Be concise and data-driven. Use bullet points where appropriate.', GETUTCDATE()),
                        (N'Prompt_Template_Technician_Analysis', N'Technician improvement report',
                         N'You are an expert manufacturing performance coach. Analyze the following data for a specific technician and produce a professional improvement report in English.

TECHNICIAN DATA:
{DATA}

Produce a structured report with the following sections:
1. **Work Summary** - total time worked, sessions, steps completed, orders involved
2. **Strengths** - areas where the technician performs well (speed, consistency, quality)
3. **Areas for Improvement** - steps or tasks that took longer than expected, repeated issues
4. **Specific Improvement Suggestions** - concrete, actionable recommendations
5. **Training Recommendations** - skills or processes to train on
6. **Overall Performance Score** - a qualitative rating (Excellent / Good / Needs Improvement) with a brief justification

Be constructive, objective, and encouraging. Use bullet points where appropriate.', GETUTCDATE());
                    END
                    IF OBJECT_ID(N'[dbo].[AiPrompts]', N'U') IS NOT NULL AND (SELECT COUNT(*) FROM [dbo].[AiPrompts]) = 0
                    BEGIN
                        INSERT INTO [dbo].[AiPrompts] ([Name], [Description], [PromptText], [LastUpdate]) VALUES
                        (N'Prompt_Template_Report', N'Activity analysis report',
                         N'You are an expert manufacturing analyst. Analyze the following activity data and produce a structured report in English.

DATA:
{DATA}

Report sections: Executive Summary, Key Metrics, Main Incidents, Repeated Failures, Top Machines, Technician Overview, Recommendations.', GETUTCDATE()),
                        (N'Prompt_Template_Technician_Analysis', N'Technician improvement report',
                         N'You are an expert manufacturing performance coach. Analyze the technician data below and produce a structured improvement report in English.

TECHNICIAN DATA:
{DATA}

Report sections: Work Summary, Strengths, Areas for Improvement, Specific Suggestions, Training Recommendations, Overall Performance Score.', GETUTCDATE());
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Ensure AspNetUsers profile columns (FirstName, LastName, Dni) added in AddUserProfileFields migration
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'FirstName')
                        ALTER TABLE [dbo].[AspNetUsers] ADD [FirstName] nvarchar(max) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'LastName')
                        ALTER TABLE [dbo].[AspNetUsers] ADD [LastName] nvarchar(max) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'Dni')
                        ALTER TABLE [dbo].[AspNetUsers] ADD [Dni] nvarchar(max) NULL;
                    IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260311152719_AddUserProfileFields_FirstName_LastName_Dni')
                        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260311152719_AddUserProfileFields_FirstName_LastName_Dni', N'9.0.0');";
                await command.ExecuteNonQueryAsync();
            }

            // Material Sets: allow ad-hoc lines (ComponentId nullable) and create MaterialSets tables if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[OrderMaterialPickLines]', N'U') IS NOT NULL
                    BEGIN
                        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderMaterialPickLines_BuildOrderId_ComponentId' AND object_id = OBJECT_ID('OrderMaterialPickLines'))
                            DROP INDEX [IX_OrderMaterialPickLines_BuildOrderId_ComponentId] ON [OrderMaterialPickLines];
                        IF EXISTS (SELECT 1 FROM sys.columns c INNER JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = 'OrderMaterialPickLines' AND c.name = 'ComponentId' AND c.is_nullable = 0)
                            ALTER TABLE [OrderMaterialPickLines] ALTER COLUMN [ComponentId] int NULL;
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MaterialSets]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[MaterialSets] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [Name] nvarchar(max) NOT NULL,
                            [Description] nvarchar(max) NULL,
                            CONSTRAINT [PK_MaterialSets] PRIMARY KEY ([Id])
                        );
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[MaterialSetItems]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[MaterialSetItems] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [MaterialSetId] int NOT NULL,
                            [MaterialId] int NOT NULL,
                            [Quantity] int NOT NULL,
                            [SortOrder] int NOT NULL,
                            CONSTRAINT [PK_MaterialSetItems] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_MaterialSetItems_MaterialSets_MaterialSetId] FOREIGN KEY ([MaterialSetId]) REFERENCES [dbo].[MaterialSets] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_MaterialSetItems_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE NO ACTION
                        );
                        CREATE INDEX [IX_MaterialSetItems_MaterialId] ON [dbo].[MaterialSetItems] ([MaterialId]);
                        CREATE INDEX [IX_MaterialSetItems_MaterialSetId] ON [dbo].[MaterialSetItems] ([MaterialSetId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            // Tasks module (internal tasks, not linked to orders)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[Tasks]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[Tasks] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [Title] nvarchar(256) NOT NULL,
                            [Description] nvarchar(max) NULL,
                            [AssignedUserId] nvarchar(450) NOT NULL,
                            [Status] int NOT NULL DEFAULT 0,
                            [CreatedAtUtc] datetime2 NOT NULL,
                            CONSTRAINT [PK_Tasks] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_Tasks_AspNetUsers_AssignedUserId] FOREIGN KEY ([AssignedUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
                        );
                        CREATE INDEX [IX_Tasks_AssignedUserId] ON [dbo].[Tasks] ([AssignedUserId]);
                        CREATE INDEX [IX_Tasks_Status] ON [dbo].[Tasks] ([Status]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[TaskSessions]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[TaskSessions] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [TaskId] int NOT NULL,
                            [UserId] nvarchar(450) NOT NULL,
                            [StartedAtUtc] datetime2 NOT NULL,
                            [EndedAtUtc] datetime2 NULL,
                            CONSTRAINT [PK_TaskSessions] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_TaskSessions_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [dbo].[Tasks] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_TaskSessions_TaskId] ON [dbo].[TaskSessions] ([TaskId]);
                        CREATE INDEX [IX_TaskSessions_UserId] ON [dbo].[TaskSessions] ([UserId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF OBJECT_ID(N'[dbo].[TaskSessionPauses]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[TaskSessionPauses] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [TaskSessionId] int NOT NULL,
                            [StartUtc] datetime2 NOT NULL,
                            [EndUtc] datetime2 NULL,
                            CONSTRAINT [PK_TaskSessionPauses] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_TaskSessionPauses_TaskSessions_TaskSessionId] FOREIGN KEY ([TaskSessionId]) REFERENCES [dbo].[TaskSessions] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_TaskSessionPauses_TaskSessionId] ON [dbo].[TaskSessionPauses] ([TaskSessionId]);
                    END";
                await command.ExecuteNonQueryAsync();
            }

            // Ensure Hidden column exists on StepTemplates (code-based migration for model property addition)
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'StepTemplates')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[StepTemplates]') AND name = 'Hidden')
                    BEGIN
                        ALTER TABLE [dbo].[StepTemplates] ADD [Hidden] bit NOT NULL CONSTRAINT DF_StepTemplates_Hidden DEFAULT 0;
                    END";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        // Seed data
        await SeedData.InitializeAsync(context, userManager, roleManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();