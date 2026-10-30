using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {

        // Create roles (Time: Operator, Supervisor, Admin | Inventory: Inventory, Admin | Normal: restricted, no step edits/prices)
        string[] roles = { "Admin", "Supervisor", "Operator", "Inventory", "Normal" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Create test users (Admin = both; Operator = time only; inventory@ = inventory only)
        await CreateUserAsync(userManager, "admin@test.com", "Admin123!", "Admin");
        await CreateUserAsync(userManager, "supervisor@test.com", "Supervisor123!", "Supervisor");
        await CreateUserAsync(userManager, "operator@test.com", "Operator123!", "Operator");
        await CreateUserAsync(userManager, "inventory@test.com", "Inventory123!", "Inventory");
        await CreateUserAsync(userManager, "user@test.com", "User123!", "Normal");

        // Seed catalog data if not exists
        if (!context.MachineModels.Any())
        {
            var d600Model = new MachineModel { Name = "D600", Active = true, StandardHours = 1 };
            context.MachineModels.Add(d600Model);
            await context.SaveChangesAsync();

            var uhfVariant = new MachineVariant
            {
                MachineModelId = d600Model.Id,
                Name = "UHF",
                Code = "UHF",
                Active = true
            };
            context.MachineVariants.Add(uhfVariant);
            await context.SaveChangesAsync();

            // Create example template with 3 phases and 8-12 steps
            var template = new ProcessTemplate
            {
                MachineModelId = d600Model.Id,
                MachineVariantId = uhfVariant.Id
            };
            context.ProcessTemplates.Add(template);
            await context.SaveChangesAsync();

            // Phase 1: Preparation
            var phase1 = new PhaseTemplate
            {
                ProcessTemplateId = template.Id,
                SortOrder = 1,
                Name = "Phase 1: Preparation"
            };
            context.PhaseTemplates.Add(phase1);
            await context.SaveChangesAsync();

            var step1 = new StepTemplate
            {
                PhaseTemplateId = phase1.Id,
                SortOrder = 1,
                Title = "Inspect Components",
                Instructions = "Check all components for damage and verify quantities match the bill of materials.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step1);

            var step2 = new StepTemplate
            {
                PhaseTemplateId = phase1.Id,
                SortOrder = 2,
                Title = "Prepare Workstation",
                Instructions = "Clean and organize the workstation. Ensure all required tools are available.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step2);

            var step3 = new StepTemplate
            {
                PhaseTemplateId = phase1.Id,
                SortOrder = 3,
                Title = "Verify Documentation",
                Instructions = "Review assembly instructions and verify all documentation is present.",
                AllowSkip = true
            };
            context.StepTemplates.Add(step3);

            // Phase 2: Assembly
            var phase2 = new PhaseTemplate
            {
                ProcessTemplateId = template.Id,
                SortOrder = 2,
                Name = "Phase 2: Assembly"
            };
            context.PhaseTemplates.Add(phase2);
            await context.SaveChangesAsync();

            var step4 = new StepTemplate
            {
                PhaseTemplateId = phase2.Id,
                SortOrder = 1,
                Title = "Mount Base Frame",
                Instructions = "Position and secure the base frame according to specifications. Use torque wrench to tighten bolts to 25 Nm.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step4);

            var step5 = new StepTemplate
            {
                PhaseTemplateId = phase2.Id,
                SortOrder = 2,
                Title = "Install Main Components",
                Instructions = "Install the main electronic components onto the base frame. Ensure proper alignment.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step5);

            var step6 = new StepTemplate
            {
                PhaseTemplateId = phase2.Id,
                SortOrder = 3,
                Title = "Connect Wiring",
                Instructions = "Route and connect all wiring according to the wiring diagram. Verify all connections are secure.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step6);

            var step7 = new StepTemplate
            {
                PhaseTemplateId = phase2.Id,
                SortOrder = 4,
                Title = "Install UHF Module",
                Instructions = "Install and secure the UHF module. Connect all required cables.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step7);

            var step8 = new StepTemplate
            {
                PhaseTemplateId = phase2.Id,
                SortOrder = 5,
                Title = "Mount Enclosure",
                Instructions = "Position and secure the enclosure. Ensure all fasteners are properly tightened.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step8);

            // Phase 3: Testing & Finalization
            var phase3 = new PhaseTemplate
            {
                ProcessTemplateId = template.Id,
                SortOrder = 3,
                Name = "Phase 3: Testing & Finalization"
            };
            context.PhaseTemplates.Add(phase3);
            await context.SaveChangesAsync();

            var step9 = new StepTemplate
            {
                PhaseTemplateId = phase3.Id,
                SortOrder = 1,
                Title = "Power-On Test",
                Instructions = "Connect power supply and perform initial power-on test. Verify all indicators are functioning.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step9);

            var step10 = new StepTemplate
            {
                PhaseTemplateId = phase3.Id,
                SortOrder = 2,
                Title = "Functional Test",
                Instructions = "Run functional test procedures. Verify all systems are operating correctly.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step10);

            var step11 = new StepTemplate
            {
                PhaseTemplateId = phase3.Id,
                SortOrder = 3,
                Title = "Final Inspection",
                Instructions = "Perform final visual inspection. Check for any defects or missing components.",
                AllowSkip = false
            };
            context.StepTemplates.Add(step11);

            var step12 = new StepTemplate
            {
                PhaseTemplateId = phase3.Id,
                SortOrder = 4,
                Title = "Documentation & Labeling",
                Instructions = "Complete all documentation, attach serial number label, and prepare for shipping.",
                AllowSkip = true
            };
            context.StepTemplates.Add(step12);

            await context.SaveChangesAsync();
        }

        // Seed workstations
        if (!context.Workstations.Any())
        {
            context.Workstations.AddRange(
                new Workstation { Name = "Workstation 1", Active = true },
                new Workstation { Name = "Workstation 2", Active = true },
                new Workstation { Name = "Workstation 3", Active = true },
                new Workstation { Name = "Assembly Station A", Active = true },
                new Workstation { Name = "Assembly Station B", Active = true }
            );
            await context.SaveChangesAsync();
        }

        // Seed inventory: system location for putaway source
        if (!context.Locations.Any(l => l.Code == "RECEPTION"))
        {
            context.Locations.Add(new Location { Code = "RECEPTION", Zone = "RECEPTION", Type = "Reception", X = 0, Y = 0, CapacityUnits = 100000 });
            await context.SaveChangesAsync();
        }

        // Seed BlockedReason catalog (issue #9)
        if (!context.BlockedReasons.Any())
        {
            context.BlockedReasons.AddRange(
                new BlockedReason { Name = "Material missing", Active = true },
                new BlockedReason { Name = "Design change", Active = true },
                new BlockedReason { Name = "Rework/defect", Active = true },
                new BlockedReason { Name = "Waiting decision", Active = true },
                new BlockedReason { Name = "Tool/machine issue", Active = true },
                new BlockedReason { Name = "Other", Active = true }
            );
            await context.SaveChangesAsync();
        }
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
        else
        {
            // User already exists (e.g. after pull on new machine, or registered manually). Ensure they have the expected role.
            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
    }
}
