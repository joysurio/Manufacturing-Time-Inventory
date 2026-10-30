using System.Data.Common;

namespace ManufacturingTimeTracking.Data;

internal static class DatabaseStartup
{
    public static async Task EnsureInventorySchemaAsync(DbConnection connection, ILogger logger)
    {
        var batches = new[]
        {
            @"IF OBJECT_ID(N'[dbo].[Locations]', N'U') IS NULL
CREATE TABLE [dbo].[Locations] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(64) NOT NULL,
    [Zone] nvarchar(64) NOT NULL,
    [X] int NOT NULL,
    [Y] int NOT NULL,
    [Z] int NULL,
    [Type] nvarchar(32) NOT NULL,
    [CapacityUnits] int NOT NULL,
    [IsBlocked] bit NOT NULL,
    CONSTRAINT [PK_Locations] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Locations_Code' AND object_id = OBJECT_ID(N'[dbo].[Locations]'))
CREATE UNIQUE INDEX [IX_Locations_Code] ON [dbo].[Locations] ([Code]);",
            @"IF OBJECT_ID(N'[dbo].[Items]', N'U') IS NULL
CREATE TABLE [dbo].[Items] (
    [Id] int NOT NULL IDENTITY,
    [Sku] nvarchar(64) NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [Family] nvarchar(256) NOT NULL,
    [ModelOrType] nvarchar(64) NOT NULL DEFAULT '',
    [Unit] nvarchar(32) NOT NULL,
    [IsSerialized] bit NOT NULL,
    [ImagePath] nvarchar(512) NOT NULL,
    [MaterialId] int NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Items_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [dbo].[Materials] ([Id]) ON DELETE SET NULL
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Items_MaterialId' AND object_id = OBJECT_ID(N'[dbo].[Items]'))
CREATE INDEX [IX_Items_MaterialId] ON [dbo].[Items] ([MaterialId]);",
            @"IF OBJECT_ID(N'[dbo].[Tags]', N'U') IS NULL
CREATE TABLE [dbo].[Tags] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(128) NOT NULL,
    [TagType] nvarchar(16) NOT NULL,
    [PackQuantity] int NOT NULL,
    [ItemId] int NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Tags_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tags_Code' AND object_id = OBJECT_ID(N'[dbo].[Tags]'))
CREATE UNIQUE INDEX [IX_Tags_Code] ON [dbo].[Tags] ([Code]);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tags_ItemId' AND object_id = OBJECT_ID(N'[dbo].[Tags]'))
CREATE INDEX [IX_Tags_ItemId] ON [dbo].[Tags] ([ItemId]);",
            @"IF OBJECT_ID(N'[dbo].[StockSnapshots]', N'U') IS NULL
CREATE TABLE [dbo].[StockSnapshots] (
    [Id] int NOT NULL IDENTITY,
    [ItemId] int NOT NULL,
    [LocationId] int NOT NULL,
    [Quantity] int NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_StockSnapshots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StockSnapshots_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_StockSnapshots_Locations_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE CASCADE
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockSnapshots_ItemId_LocationId' AND object_id = OBJECT_ID(N'[dbo].[StockSnapshots]'))
CREATE UNIQUE INDEX [IX_StockSnapshots_ItemId_LocationId] ON [dbo].[StockSnapshots] ([ItemId], [LocationId]);",
            @"IF OBJECT_ID(N'[dbo].[Movements]', N'U') IS NULL
CREATE TABLE [dbo].[Movements] (
    [Id] int NOT NULL IDENTITY,
    [Type] nvarchar(32) NOT NULL,
    [ItemId] int NOT NULL,
    [Quantity] int NOT NULL,
    [FromLocationId] int NULL,
    [ToLocationId] int NULL,
    [PerformedBy] nvarchar(256) NOT NULL,
    [TimestampUtc] datetime2 NOT NULL,
    [Notes] nvarchar(512) NOT NULL,
    CONSTRAINT [PK_Movements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Movements_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Movements_Locations_FromLocationId] FOREIGN KEY ([FromLocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Movements_Locations_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Movements_ItemId' AND object_id = OBJECT_ID(N'[dbo].[Movements]'))
CREATE INDEX [IX_Movements_ItemId] ON [dbo].[Movements] ([ItemId]);",
            @"IF OBJECT_ID(N'[dbo].[DeviceEvents]', N'U') IS NULL
CREATE TABLE [dbo].[DeviceEvents] (
    [Id] int NOT NULL IDENTITY,
    [DeviceId] nvarchar(64) NOT NULL,
    [EventType] nvarchar(64) NOT NULL,
    [TimestampUtc] datetime2 NOT NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_DeviceEvents] PRIMARY KEY ([Id])
);",
            @"IF OBJECT_ID(N'[dbo].[RobotTasks]', N'U') IS NULL
CREATE TABLE [dbo].[RobotTasks] (
    [Id] int NOT NULL IDENTITY,
    [TaskType] nvarchar(16) NOT NULL,
    [ItemId] int NULL,
    [FromLocationId] int NULL,
    [ToLocationId] int NULL,
    [Status] nvarchar(16) NOT NULL,
    [Priority] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [ErrorMessage] nvarchar(512) NULL,
    CONSTRAINT [PK_RobotTasks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RobotTasks_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RobotTasks_Locations_FromLocationId] FOREIGN KEY ([FromLocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RobotTasks_Locations_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [dbo].[Locations] ([Id]) ON DELETE NO ACTION
);",
            @"IF OBJECT_ID(N'[dbo].[MachineModelComponents]', N'U') IS NULL
CREATE TABLE [dbo].[MachineModelComponents] (
    [Id] int NOT NULL IDENTITY,
    [MachineModelId] int NOT NULL,
    [ItemId] int NOT NULL,
    [Quantity] int NOT NULL DEFAULT 1,
    [Notes] nvarchar(128) NULL,
    CONSTRAINT [PK_MachineModelComponents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MachineModelComponents_MachineModels_MachineModelId] FOREIGN KEY ([MachineModelId]) REFERENCES [dbo].[MachineModels] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_MachineModelComponents_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE NO ACTION
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MachineModelComponents_MachineModelId' AND object_id = OBJECT_ID(N'[dbo].[MachineModelComponents]'))
CREATE INDEX [IX_MachineModelComponents_MachineModelId] ON [dbo].[MachineModelComponents] ([MachineModelId]);",
            @"IF OBJECT_ID(N'[dbo].[MachineModelComponentAlternatives]', N'U') IS NULL
CREATE TABLE [dbo].[MachineModelComponentAlternatives] (
    [Id] int NOT NULL IDENTITY,
    [ComponentId] int NOT NULL,
    [ItemId] int NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_MachineModelComponentAlternatives] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MachineModelComponentAlternatives_MachineModelComponents_ComponentId] FOREIGN KEY ([ComponentId]) REFERENCES [dbo].[MachineModelComponents] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_MachineModelComponentAlternatives_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE NO ACTION
);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MachineModelComponentAlternatives_ComponentId' AND object_id = OBJECT_ID(N'[dbo].[MachineModelComponentAlternatives]'))
CREATE INDEX [IX_MachineModelComponentAlternatives_ComponentId] ON [dbo].[MachineModelComponentAlternatives] ([ComponentId]);",
            @"IF OBJECT_ID(N'[dbo].[OrderPickLines]', N'U') IS NULL
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
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderPickLines_BuildOrderId' AND object_id = OBJECT_ID(N'[dbo].[OrderPickLines]'))
CREATE INDEX [IX_OrderPickLines_BuildOrderId] ON [dbo].[OrderPickLines] ([BuildOrderId]);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderPickLines_BuildOrderId_ItemId' AND object_id = OBJECT_ID(N'[dbo].[OrderPickLines]'))
CREATE UNIQUE INDEX [IX_OrderPickLines_BuildOrderId_ItemId] ON [dbo].[OrderPickLines] ([BuildOrderId], [ItemId]);"
        };

        foreach (var batch in batches)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "EnsureInventorySchema: one batch failed (table may already exist).");
            }
        }
    }
}
