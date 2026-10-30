using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.Templates;

[Authorize(Policy = "CanEditSteps")]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public CreateModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public ProcessTemplate ProcessTemplate { get; set; } = default!;

    public SelectList MachineModels { get; set; } = default!;
    public SelectList MachineVariants { get; set; } = default!;

    [BindProperty]
    public int SelectedModelId { get; set; }

    [BindProperty]
    public int? CopyFromTemplateId { get; set; }

    public SelectList AvailableTemplates { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync()
    {
        MachineModels = new SelectList(await _context.MachineModels.Where(m => m.Active).ToListAsync(), "Id", "Name");
        var templates = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
            .ToListAsync();

        // Load machine model and variant names for display (same as Index "Name" column)
        var modelIds = templates.Select(t => t.MachineModelId).Distinct().ToList();
        var models = await _context.MachineModels.Where(m => modelIds.Contains(m.Id)).ToListAsync();
        var modelNames = models.ToDictionary(m => m.Id, m => m.Name);

        var variantIds = templates.Select(t => t.MachineVariantId).Distinct().ToList();
        var variants = await _context.MachineVariants.Where(v => variantIds.Contains(v.Id)).ToListAsync();
        var variantDisplay = variants.ToDictionary(v => v.Id, v => string.IsNullOrEmpty(v.Code) ? v.Name : $"{v.Name} ({v.Code})");

        AvailableTemplates = new SelectList(templates.Select(t => new {
            Id = t.Id,
            Name = $"{(modelNames.TryGetValue(t.MachineModelId, out var mn) ? mn : $"ID:{t.MachineModelId}")} - {(variantDisplay.TryGetValue(t.MachineVariantId, out var vd) ? vd : $"ID:{t.MachineVariantId}")}"
        }), "Id", "Name");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || _context.ProcessTemplates == null || ProcessTemplate == null)
        {
            MachineModels = new SelectList(await _context.MachineModels.Where(m => m.Active).ToListAsync(), "Id", "Name");
            var templates = await _context.ProcessTemplates
                .Include(pt => pt.Phases)
                    .ThenInclude(p => p.Steps)
                .ToListAsync();

            var modelIds = templates.Select(t => t.MachineModelId).Distinct().ToList();
            var models = await _context.MachineModels.Where(m => modelIds.Contains(m.Id)).ToListAsync();
            var modelNames = models.ToDictionary(m => m.Id, m => m.Name);
            var variantIds = templates.Select(t => t.MachineVariantId).Distinct().ToList();
            var variants = await _context.MachineVariants.Where(v => variantIds.Contains(v.Id)).ToListAsync();
            var variantDisplay = variants.ToDictionary(v => v.Id, v => string.IsNullOrEmpty(v.Code) ? v.Name : $"{v.Name} ({v.Code})");

            AvailableTemplates = new SelectList(templates.Select(t => new {
                Id = t.Id,
                Name = $"{(modelNames.TryGetValue(t.MachineModelId, out var mn) ? mn : $"ID:{t.MachineModelId}")} - {(variantDisplay.TryGetValue(t.MachineVariantId, out var vd) ? vd : $"ID:{t.MachineVariantId}")}"
            }), "Id", "Name");
            return Page();
        }

        _context.ProcessTemplates.Add(ProcessTemplate);
        await _context.SaveChangesAsync();

        // Copy phases and steps if template selected
        if (CopyFromTemplateId.HasValue && CopyFromTemplateId.Value > 0)
        {
            var sourceTemplate = await _context.ProcessTemplates
                .Include(pt => pt.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Tools)
                            .ThenInclude(st => st.Tool)
                .Include(pt => pt.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Materials)
                            .ThenInclude(sm => sm.Material)
                .AsSplitQuery()
                .Include(pt => pt.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Media)
                            .ThenInclude(sm => sm.Media)
                .FirstOrDefaultAsync(pt => pt.Id == CopyFromTemplateId.Value);

            if (sourceTemplate != null)
            {
                foreach (var sourcePhase in sourceTemplate.Phases.OrderBy(p => p.SortOrder))
                {
                    var newPhase = new PhaseTemplate
                    {
                        ProcessTemplateId = ProcessTemplate.Id,
                        Name = sourcePhase.Name,
                        SortOrder = sourcePhase.SortOrder
                    };
                    _context.PhaseTemplates.Add(newPhase);
                    await _context.SaveChangesAsync();

                    foreach (var sourceStep in sourcePhase.Steps.OrderBy(s => s.SortOrder))
                    {
                        var newStep = new StepTemplate
                        {
                            PhaseTemplateId = newPhase.Id,
                            Title = sourceStep.Title,
                            Instructions = sourceStep.Instructions,
                            AllowSkip = sourceStep.AllowSkip,
                            SortOrder = sourceStep.SortOrder
                        };
                        _context.StepTemplates.Add(newStep);
                        await _context.SaveChangesAsync();

                        // Copy tools
                        foreach (var tool in sourceStep.Tools)
                        {
                            _context.StepTemplateTools.Add(new StepTemplateTool
                            {
                                StepTemplateId = newStep.Id,
                                ToolId = tool.ToolId
                            });
                        }

                        // Copy materials
                        foreach (var material in sourceStep.Materials)
                        {
                            _context.StepTemplateMaterials.Add(new StepTemplateMaterial
                            {
                                StepTemplateId = newStep.Id,
                                MaterialId = material.MaterialId,
                                Qty = material.Qty
                            });
                        }

                        // Copy media references
                        foreach (var media in sourceStep.Media)
                        {
                            _context.StepTemplateMedia.Add(new StepTemplateMedia
                            {
                                StepTemplateId = newStep.Id,
                                MediaId = media.MediaId,
                                Caption = media.Caption
                            });
                        }
                    }
                }
                await _context.SaveChangesAsync();
                await PackingListSync.SyncFromTemplateStepsAsync(_context, ProcessTemplate.MachineModelId);
            }
        }

        return RedirectToPage("./Edit", new { id = ProcessTemplate.Id });
    }
}
