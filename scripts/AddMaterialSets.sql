-- ============================================================
-- Material Sets feature: create tables and alter OrderMaterialPickLines
-- Run this script on your database if you get "Invalid object name 'MaterialSets'"
-- or if "dotnet ef database update" fails due to migration history mismatch.
-- ============================================================

-- 1. Allow multiple ad-hoc material lines per order (from material sets)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderMaterialPickLines_BuildOrderId_ComponentId' AND object_id = OBJECT_ID('OrderMaterialPickLines'))
    DROP INDEX [IX_OrderMaterialPickLines_BuildOrderId_ComponentId] ON [OrderMaterialPickLines];

-- 2. Make ComponentId nullable (so material-set lines can be stored with ComponentId = NULL)
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OrderMaterialPickLines')
BEGIN
    ALTER TABLE [OrderMaterialPickLines] ALTER COLUMN [ComponentId] int NULL;
END
GO

-- 3. Create MaterialSets table (skip if exists)
IF OBJECT_ID(N'[dbo].[MaterialSets]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MaterialSets] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        CONSTRAINT [PK_MaterialSets] PRIMARY KEY ([Id])
    );
END
GO

-- 4. Create MaterialSetItems table (skip if exists)
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
END
GO

-- 5. Record that this migration was applied (optional; only if you use EF migrations and want to skip AddMaterialSets)
-- INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('20260309211327_AddMaterialSets', '9.0.0');
-- (Uncomment the line above only if you need to mark the migration as applied so future "dotnet ef database update" does not try to run it again.)
