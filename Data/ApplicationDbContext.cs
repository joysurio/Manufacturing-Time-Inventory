using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Models.Tasks;
using ManufacturingTimeTracking.Models.AI;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Linq;

namespace ManufacturingTimeTracking.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Catalog
    public DbSet<MachineModel> MachineModels { get; set; }
    public DbSet<MachineVariant> MachineVariants { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<MachineModelComponent> MachineModelComponents { get; set; }
    public DbSet<MachineModelComponentAlternative> MachineModelComponentAlternatives { get; set; }
    public DbSet<MachineVariantComponent> MachineVariantComponents { get; set; }
    public DbSet<MachineVariantComponentAlternative> MachineVariantComponentAlternatives { get; set; }
    public DbSet<Workstation> Workstations { get; set; }
    public DbSet<BlockedReason> BlockedReasons { get; set; }

    // Templates
    public DbSet<ProcessTemplate> ProcessTemplates { get; set; }
    public DbSet<PhaseTemplate> PhaseTemplates { get; set; }
    public DbSet<StepTemplate> StepTemplates { get; set; }
    public DbSet<Tool> Tools { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<Media> Media { get; set; }
    public DbSet<StepTemplateTool> StepTemplateTools { get; set; }
    public DbSet<StepTemplateMaterial> StepTemplateMaterials { get; set; }
    public DbSet<StepTemplateMedia> StepTemplateMedia { get; set; }

    // Execution
    public DbSet<BuildOrder> BuildOrders { get; set; }
    public DbSet<OrderPickLine> OrderPickLines { get; set; }
    public DbSet<OrderMaterialPickLine> OrderMaterialPickLines { get; set; }
    public DbSet<BuildExecution> BuildExecutions { get; set; }
    public DbSet<PhaseExec> PhaseExecs { get; set; }
    public DbSet<StepExec> StepExecs { get; set; }
    public DbSet<StepExecTool> StepExecTools { get; set; }
    public DbSet<StepExecMaterial> StepExecMaterials { get; set; }
    public DbSet<StepRun> StepRuns { get; set; }
    public DbSet<StepEvidence> StepEvidence { get; set; }
    public DbSet<ProductionInterruption> ProductionInterruptions { get; set; }

    // Inventory (shared database)
    public DbSet<Item> Items { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<StockSnapshot> StockSnapshots { get; set; }
    public DbSet<Movement> Movements { get; set; }
    public DbSet<MaterialStock> MaterialStocks { get; set; }
    public DbSet<MaterialStockMovement> MaterialStockMovements { get; set; }
    public DbSet<MaterialSet> MaterialSets { get; set; }
    public DbSet<MaterialSetItem> MaterialSetItems { get; set; }
    public DbSet<DeviceEvent> DeviceEvents { get; set; }
    public DbSet<RobotTask> RobotTasks { get; set; }

    // Time tracking
    public DbSet<BreakReason> BreakReasons { get; set; }
    public DbSet<TimeTrackingEntry> TimeTrackingEntries { get; set; }
    public DbSet<DailyTimeEvaluation> DailyTimeEvaluations { get; set; }

    // Tasks (internal, not linked to orders)
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<TaskSession> TaskSessions { get; set; }
    public DbSet<TaskSessionPause> TaskSessionPauses { get; set; }

    // AI
    public DbSet<AiPrompt> AiPrompts { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure relationships
        builder.Entity<MachineVariant>()
            .HasOne(v => v.MachineModel)
            .WithMany(m => m.Variants)
            .HasForeignKey(v => v.MachineModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MachineVariantComponent>()
            .HasOne(c => c.MachineVariant)
            .WithMany(v => v.Components)
            .HasForeignKey(c => c.MachineVariantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MachineVariantComponent>()
            .HasOne(c => c.Item)
            .WithMany()
            .HasForeignKey(c => c.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MachineVariantComponent>()
            .HasOne(c => c.Material)
            .WithMany()
            .HasForeignKey(c => c.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MachineVariantComponent>()
            .HasMany(c => c.Alternatives)
            .WithOne(a => a.Component)
            .HasForeignKey(a => a.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MachineVariantComponentAlternative>()
            .HasOne(a => a.Item)
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MachineModel>()
            .Property(m => m.StandardHours)
            .HasPrecision(18, 2);

        builder.Entity<MachineModelComponent>()
            .HasOne(c => c.MachineModel)
            .WithMany(m => m.Components)
            .HasForeignKey(c => c.MachineModelId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MachineModelComponent>()
            .HasOne(c => c.Item)
            .WithMany()
            .HasForeignKey(c => c.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MachineModelComponent>()
            .HasOne(c => c.Material)
            .WithMany()
            .HasForeignKey(c => c.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MachineModelComponent>()
            .HasMany(c => c.Alternatives)
            .WithOne(a => a.Component)
            .HasForeignKey(a => a.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MachineModelComponentAlternative>()
            .HasOne(a => a.Item)
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OrderPickLine>()
            .HasOne(l => l.BuildOrder)
            .WithMany()
            .HasForeignKey(l => l.BuildOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<OrderPickLine>()
            .HasOne(l => l.Item)
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrderPickLine>()
            .HasIndex(l => new { l.BuildOrderId, l.ItemId })
            .IsUnique();

        builder.Entity<PhaseTemplate>()
            .HasOne(p => p.ProcessTemplate)
            .WithMany(pt => pt.Phases)
            .HasForeignKey(p => p.ProcessTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepTemplate>()
            .HasOne(s => s.PhaseTemplate)
            .WithMany(p => p.Steps)
            .HasForeignKey(s => s.PhaseTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepTemplateTool>()
            .HasOne(st => st.StepTemplate)
            .WithMany(s => s.Tools)
            .HasForeignKey(st => st.StepTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepTemplateTool>()
            .HasOne(st => st.Tool)
            .WithMany(t => t.StepTemplates)
            .HasForeignKey(st => st.ToolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StepTemplateMaterial>()
            .HasOne(sm => sm.StepTemplate)
            .WithMany(s => s.Materials)
            .HasForeignKey(sm => sm.StepTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepTemplateMaterial>()
            .HasOne(sm => sm.Material)
            .WithMany(m => m.StepTemplates)
            .HasForeignKey(sm => sm.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StepTemplateMaterial>()
            .Property(sm => sm.Qty)
            .HasPrecision(18, 4);

        builder.Entity<Material>()
            .HasOne(m => m.Supplier)
            .WithMany()
            .HasForeignKey(m => m.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Material>()
            .HasOne(m => m.Location)
            .WithMany()
            .HasForeignKey(m => m.LocationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Material>()
            .Property(m => m.Price)
            .HasPrecision(18, 2);

        builder.Entity<StepTemplateMedia>()
            .HasOne(sm => sm.StepTemplate)
            .WithMany(s => s.Media)
            .HasForeignKey(sm => sm.StepTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepTemplateMedia>()
            .HasOne(sm => sm.Media)
            .WithMany(m => m.StepTemplates)
            .HasForeignKey(sm => sm.MediaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BuildExecution>()
            .HasOne(be => be.BuildOrder)
            .WithMany(bo => bo.Executions)
            .HasForeignKey(be => be.BuildOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BuildOrder>()
            .HasIndex(b => b.DueDate);
        builder.Entity<BuildOrder>()
            .HasIndex(b => b.Priority);
        builder.Entity<BuildOrder>()
            .HasIndex(b => b.OwnerUserId);

        builder.Entity<BuildOrder>()
            .HasOne(b => b.BlockedReason)
            .WithMany()
            .HasForeignKey(b => b.BlockedReasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProductionInterruption>()
            .HasOne(pi => pi.BuildOrder)
            .WithMany(b => b.Interruptions)
            .HasForeignKey(pi => pi.BuildOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductionInterruption>()
            .HasOne(pi => pi.StepExec)
            .WithMany()
            .HasForeignKey(pi => pi.StepExecId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PhaseExec>()
            .HasOne(p => p.BuildExecution)
            .WithMany(be => be.Phases)
            .HasForeignKey(p => p.BuildExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepExec>()
            .HasOne(s => s.PhaseExec)
            .WithMany(p => p.Steps)
            .HasForeignKey(s => s.PhaseExecId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepExecTool>()
            .HasOne(st => st.StepExec)
            .WithMany(s => s.Tools)
            .HasForeignKey(st => st.StepExecId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepExecMaterial>()
            .HasOne(sm => sm.StepExec)
            .WithMany(s => s.Materials)
            .HasForeignKey(sm => sm.StepExecId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<StepExecMaterial>()
            .Property(sm => sm.Qty)
            .HasPrecision(18, 4);

        builder.Entity<StepRun>()
            .HasOne(sr => sr.StepExec)
            .WithMany(s => s.Runs)
            .HasForeignKey(sr => sr.StepExecId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepEvidence>()
            .HasOne(se => se.StepExec)
            .WithMany(s => s.Evidence)
            .HasForeignKey(se => se.StepExecId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StepEvidence>()
            .HasOne(se => se.Media)
            .WithMany(m => m.StepEvidence)
            .HasForeignKey(se => se.MediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Inventory
        builder.Entity<Tag>()
            .HasIndex(x => x.Code)
            .IsUnique();
        builder.Entity<Tag>()
            .HasOne(t => t.Item)
            .WithMany(i => i.Tags)
            .HasForeignKey(t => t.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Location>()
            .HasIndex(x => x.Code)
            .IsUnique();

        builder.Entity<StockSnapshot>()
            .HasIndex(x => new { x.ItemId, x.LocationId })
            .IsUnique();
        builder.Entity<StockSnapshot>()
            .HasOne(s => s.Item)
            .WithMany()
            .HasForeignKey(s => s.ItemId);
        builder.Entity<StockSnapshot>()
            .HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId);

        builder.Entity<Movement>()
            .HasOne(m => m.Item)
            .WithMany()
            .HasForeignKey(m => m.ItemId);
        builder.Entity<Movement>()
            .HasOne(m => m.FromLocation)
            .WithMany()
            .HasForeignKey(m => m.FromLocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Entity<Movement>()
            .HasOne(m => m.ToLocation)
            .WithMany()
            .HasForeignKey(m => m.ToLocationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<MaterialStock>()
            .HasOne(ms => ms.Material)
            .WithMany()
            .HasForeignKey(ms => ms.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MaterialStock>()
            .HasOne(ms => ms.Location)
            .WithMany()
            .HasForeignKey(ms => ms.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MaterialStock>()
            .HasIndex(ms => new { ms.MaterialId, ms.LocationId })
            .IsUnique();

        builder.Entity<MaterialStockMovement>()
            .HasOne(m => m.Material)
            .WithMany()
            .HasForeignKey(m => m.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MaterialStockMovement>()
            .HasOne(m => m.Location)
            .WithMany()
            .HasForeignKey(m => m.LocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Entity<MaterialStockMovement>()
            .HasOne(m => m.BuildOrder)
            .WithMany()
            .HasForeignKey(m => m.BuildOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<MaterialSetItem>()
            .HasOne(i => i.MaterialSet)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.MaterialSetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MaterialSetItem>()
            .HasOne(i => i.Material)
            .WithMany()
            .HasForeignKey(i => i.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        // Item <-> Material link (shared items between inventory and production)
        builder.Entity<Item>()
            .HasOne(i => i.Material)
            .WithMany(m => m.InventoryItems)
            .HasForeignKey(i => i.MaterialId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<RobotTask>()
            .HasOne(t => t.Item)
            .WithMany()
            .HasForeignKey(t => t.ItemId);
        builder.Entity<RobotTask>()
            .HasOne(t => t.FromLocation)
            .WithMany()
            .HasForeignKey(t => t.FromLocationId);
        builder.Entity<RobotTask>()
            .HasOne(t => t.ToLocation)
            .WithMany()
            .HasForeignKey(t => t.ToLocationId);

        builder.Entity<TimeTrackingEntry>()
            .HasOne(e => e.BreakReason)
            .WithMany()
            .HasForeignKey(e => e.BreakReasonId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<TimeTrackingEntry>()
            .HasIndex(e => new { e.UserId, e.TimestampUtc });
        builder.Entity<TimeTrackingEntry>()
            .HasIndex(e => e.SessionId);

        builder.Entity<DailyTimeEvaluation>()
            .HasIndex(e => new { e.UserId, e.EvalDateUtc })
            .IsUnique();
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.ScoreEntry).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.ScoreExit).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.ScoreTasks).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.ScoreWorked).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.ScoreBreak).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.AverageScore).HasPrecision(6, 4);
        builder.Entity<DailyTimeEvaluation>()
            .Property(e => e.TaskPercent).HasPrecision(9, 4);

        builder.Entity<TaskSession>()
            .HasOne(s => s.Task)
            .WithMany(t => t.Sessions)
            .HasForeignKey(s => s.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TaskSessionPause>()
            .HasOne(p => p.TaskSession)
            .WithMany(s => s.Pauses)
            .HasForeignKey(p => p.TaskSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
