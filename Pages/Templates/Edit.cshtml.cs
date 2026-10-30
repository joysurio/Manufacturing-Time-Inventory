using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using System.Linq;

namespace ManufacturingTimeTracking.Pages.Templates;

[Authorize(Policy = "CanEditSteps")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public EditModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public ProcessTemplate ProcessTemplate { get; set; } = default!;

    // Input models without navigation properties
    [BindProperty]
    public PhaseInputModel NewPhase { get; set; } = new();

    [BindProperty]
    public StepInputModel NewStep { get; set; } = new();

    [BindProperty]
    public int? SelectedPhaseId { get; set; }

    // Input model classes to avoid navigation property binding issues
    public class PhaseInputModel
    {
        public int ProcessTemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class StepInputModel
    {
        public int PhaseTemplateId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public bool? AllowSkip { get; set; } // Optional: null = not sent, we treat as false in handler
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Tools)
                        .ThenInclude(st => st.Tool)
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Materials)
                        .ThenInclude(sm => sm.Material)
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Media)
                        .ThenInclude(sm => sm.Media)
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (template == null)
        {
            return NotFound();
        }

        ProcessTemplate = template;
        
        // Initialize NewPhase with the template ID
        NewPhase.ProcessTemplateId = template.Id;
        NewPhase.Name = string.Empty;
        
        return Page();
    }

    public async Task<IActionResult> OnPostAddPhaseAsync(int? id)
    {
        // Get ProcessTemplateId from route parameter, form, or route data
        var templateId = id ?? NewPhase.ProcessTemplateId;
        if (templateId <= 0)
        {
            // Try to get it from the route data
            var routeData = RouteData.Values["id"];
            if (routeData != null && int.TryParse(routeData.ToString(), out int routeId))
            {
                templateId = routeId;
            }
        }
        
        // Ensure ProcessTemplateId is set
        if (templateId > 0)
        {
            NewPhase.ProcessTemplateId = templateId;
        }

        // Validate Name is not empty
        if (string.IsNullOrWhiteSpace(NewPhase.Name))
        {
            ModelState.AddModelError("NewPhase.Name", "Phase name is required.");
        }

        if (templateId <= 0)
        {
            ModelState.AddModelError("NewPhase.ProcessTemplateId", "Template ID is required.");
        }

        if (!ModelState.IsValid)
        {
            if (templateId > 0)
            {
                return await OnGetAsync(templateId);
            }
            TempData["Error"] = "Unable to determine template ID. Please try again.";
            return RedirectToPage("./Index");
        }

        // Verify the template exists
        var template = await _context.ProcessTemplates.FindAsync(templateId);
        if (template == null)
        {
            TempData["Error"] = "Template not found.";
            return RedirectToPage("./Index");
        }

        var maxOrder = await _context.PhaseTemplates
            .Where(p => p.ProcessTemplateId == templateId)
            .MaxAsync(p => (int?)p.SortOrder) ?? 0;

        // Create new phase without navigation properties
        var newPhase = new PhaseTemplate
        {
            ProcessTemplateId = templateId,
            Name = NewPhase.Name.Trim(),
            SortOrder = maxOrder + 1
        };

        _context.PhaseTemplates.Add(newPhase);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Phase added successfully!";
        return RedirectToPage("./Edit", new { id = templateId });
    }

    public async Task<IActionResult> OnPostAddStepAsync(int? id)
    {
        // Use SelectedPhaseId if available, otherwise fall back to NewStep.PhaseTemplateId
        int phaseId = SelectedPhaseId ?? NewStep.PhaseTemplateId;

        // Remove ALL validation errors for AllowSkip - it's optional
        // Try all possible key formats that ASP.NET Core might use
        ModelState.Remove("NewStep.AllowSkip");
        ModelState.Remove("NewStep_AllowSkip");
        ModelState.Remove("AllowSkip");
        
        // Also remove any errors that contain "AllowSkip" in the key
        var keysToRemove = ModelState.Keys.Where(k => k.Contains("AllowSkip", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove)
        {
            ModelState.Remove(key);
        }
        
        // Handle AllowSkip checkbox binding explicitly
        // When checkbox is checked: form sends both hidden "false" and checkbox "true"
        // When unchecked: only hidden "false" is sent
        var allowSkipFormKey = Request.Form.Keys.FirstOrDefault(k => k.Contains("AllowSkip", StringComparison.OrdinalIgnoreCase));
        
        if (allowSkipFormKey != null)
        {
            var formValues = Request.Form[allowSkipFormKey].ToList();
            
            // If multiple values exist, checkbox was checked (hidden=false, checkbox=true)
            if (formValues.Count > 1)
            {
                // Multiple values means checkbox was checked - take the "true" value
                NewStep.AllowSkip = formValues.Any(v => v == "true");
            }
            else if (formValues.Count == 1)
            {
                // Single value - check if it's "true" (checked) or "false" (unchecked)
                NewStep.AllowSkip = formValues[0] == "true";
            }
            else
            {
                // No values - default to false (optional field)
                NewStep.AllowSkip = false;
            }
        }
        else
        {
            // If form key not found, checkbox was unchecked - default to false (optional field)
            NewStep.AllowSkip = false;
        }
        
        // Clear any remaining validation errors for AllowSkip after setting the value
        keysToRemove = ModelState.Keys.Where(k => k.Contains("AllowSkip", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove)
        {
            ModelState.Remove(key);
        }

        // Validate required fields ONLY (AllowSkip is optional, already handled above)
        if (string.IsNullOrWhiteSpace(NewStep.Title))
        {
            ModelState.AddModelError("NewStep.Title", "Step title is required.");
        }

        if (phaseId <= 0)
        {
            ModelState.AddModelError("NewStep.PhaseTemplateId", "Phase ID is required.");
        }

        // Final check: Remove any remaining AllowSkip errors before validation check
        var remainingAllowSkipKeys = ModelState.Keys.Where(k => k.Contains("AllowSkip", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in remainingAllowSkipKeys)
        {
            ModelState.Remove(key);
        }

        // Check ModelState validity (AllowSkip errors have been removed)
        if (!ModelState.IsValid || phaseId <= 0)
        {
            if (phaseId > 0)
            {
                var phase = await _context.PhaseTemplates.FindAsync(phaseId);
                if (phase != null)
                {
                    return await OnGetAsync(phase.ProcessTemplateId);
                }
            }
            return RedirectToPage("./Index");
        }

        var phaseForStep = await _context.PhaseTemplates.FindAsync(phaseId);
        if (phaseForStep == null)
        {
            return NotFound();
        }

        var maxOrder = await _context.StepTemplates
            .Where(s => s.PhaseTemplateId == phaseId)
            .MaxAsync(s => (int?)s.SortOrder) ?? 0;

        // Create new step without navigation properties
        // AllowSkip: true if checkbox was checked, false if unchecked (optional field)
        var newStep = new StepTemplate
        {
            PhaseTemplateId = phaseId,
            Title = NewStep.Title.Trim(),
            Instructions = NewStep.Instructions?.Trim() ?? string.Empty,
            AllowSkip = NewStep.AllowSkip ?? false,
            SortOrder = maxOrder + 1
        };

        _context.StepTemplates.Add(newStep);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Step added successfully!";
        return RedirectToPage("./Edit", new { id = phaseForStep.ProcessTemplateId });
    }

    public async Task<IActionResult> OnPostMovePhaseAsync(int phaseId, string direction)
    {
        var phase = await _context.PhaseTemplates.FindAsync(phaseId);
        if (phase == null) return NotFound();

        var allPhases = await _context.PhaseTemplates
            .Where(p => p.ProcessTemplateId == phase.ProcessTemplateId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var currentIndex = allPhases.FindIndex(p => p.Id == phaseId);
        if (currentIndex == -1) return NotFound();

        int newIndex = direction == "up" ? currentIndex - 1 : currentIndex + 1;
        if (newIndex < 0 || newIndex >= allPhases.Count) return RedirectToPage("./Edit", new { id = phase.ProcessTemplateId });

        var temp = allPhases[currentIndex].SortOrder;
        allPhases[currentIndex].SortOrder = allPhases[newIndex].SortOrder;
        allPhases[newIndex].SortOrder = temp;

        await _context.SaveChangesAsync();
        return RedirectToPage("./Edit", new { id = phase.ProcessTemplateId });
    }

    public async Task<IActionResult> OnPostMoveStepAsync(int stepId, string direction)
    {
        var step = await _context.StepTemplates
            .Include(s => s.PhaseTemplate)
            .FirstOrDefaultAsync(s => s.Id == stepId);
        if (step == null) return NotFound();

        var allSteps = await _context.StepTemplates
            .Where(s => s.PhaseTemplateId == step.PhaseTemplateId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var currentIndex = allSteps.FindIndex(s => s.Id == stepId);
        if (currentIndex == -1) return NotFound();

        int newIndex = direction == "up" ? currentIndex - 1 : currentIndex + 1;
        if (newIndex < 0 || newIndex >= allSteps.Count) 
            return RedirectToPage("./Edit", new { id = step.PhaseTemplate.ProcessTemplateId });

        var temp = allSteps[currentIndex].SortOrder;
        allSteps[currentIndex].SortOrder = allSteps[newIndex].SortOrder;
        allSteps[newIndex].SortOrder = temp;

        await _context.SaveChangesAsync();
        return RedirectToPage("./Edit", new { id = step.PhaseTemplate.ProcessTemplateId });
    }

    public class ReorderStepsRequest
    {
        public int PhaseId { get; set; }
        public List<int> StepIds { get; set; } = new();
    }

    public async Task<IActionResult> OnPostReorderStepsAsync([FromBody] ReorderStepsRequest req)
    {
        if (req.PhaseId <= 0 || req.StepIds == null || req.StepIds.Count == 0)
            return BadRequest();

        // Load steps in this phase only
        var steps = await _context.StepTemplates
            .Where(s => s.PhaseTemplateId == req.PhaseId)
            .ToListAsync();

        // Ensure all ids belong to this phase
        var stepById = steps.ToDictionary(s => s.Id, s => s);
        if (req.StepIds.Any(id => !stepById.ContainsKey(id)))
            return BadRequest();

        // Apply new sequential SortOrder (1..n) based on provided order
        for (var i = 0; i < req.StepIds.Count; i++)
        {
            stepById[req.StepIds[i]].SortOrder = i + 1;
        }

        await _context.SaveChangesAsync();
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostDeletePhaseAsync(int phaseId)
    {
        var phase = await _context.PhaseTemplates
            .Include(p => p.ProcessTemplate)
            .FirstOrDefaultAsync(p => p.Id == phaseId);
        if (phase == null) return NotFound();

        var templateId = phase.ProcessTemplateId;
        _context.PhaseTemplates.Remove(phase);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Edit", new { id = templateId });
    }

    public async Task<IActionResult> OnPostDeleteStepAsync(int stepId)
    {
        var step = await _context.StepTemplates
            .Include(s => s.PhaseTemplate)
                .ThenInclude(p => p.ProcessTemplate)
            .FirstOrDefaultAsync(s => s.Id == stepId);
        if (step == null) return NotFound();

        var processTemplate = step.PhaseTemplate.ProcessTemplate;
        var templateId = processTemplate.Id;
        var phaseSortOrder = step.PhaseTemplate.SortOrder;
        var stepSortOrder = step.SortOrder;
        var stepTitle = step.Title;

        // Remove from orders: match by SortOrder OR Title; only Pending steps (preserve recorded times).
        var ordersUsingTemplate = await _context.BuildOrders
            .Where(o => o.MachineModelId == processTemplate.MachineModelId && o.MachineVariantId == processTemplate.MachineVariantId)
            .Select(o => o.Id)
            .ToListAsync();
        var executionIds = await _context.BuildExecutions
            .Where(e => ordersUsingTemplate.Contains(e.BuildOrderId))
            .Select(e => e.Id)
            .ToListAsync();
        var phaseExecIds = await _context.PhaseExecs
            .Where(p => executionIds.Contains(p.BuildExecutionId) && p.SortOrder == phaseSortOrder)
            .Select(p => p.Id)
            .ToListAsync();
        // Match by SortOrder or by Title (handles cases where order was created from a different template copy)
        var stepExecsToConsider = await _context.StepExecs
            .Where(s => phaseExecIds.Contains(s.PhaseExecId) && (s.SortOrder == stepSortOrder || s.Title == stepTitle))
            .ToListAsync();
        var removedCount = 0;
        foreach (var stepExec in stepExecsToConsider.Where(se => se.State == "Pending"))
        {
            var execSortOrder = stepExec.SortOrder;
            _context.StepExecs.Remove(stepExec);
            var laterSteps = await _context.StepExecs
                .Where(s => s.PhaseExecId == stepExec.PhaseExecId && s.SortOrder > execSortOrder)
                .ToListAsync();
            foreach (var later in laterSteps)
            {
                later.SortOrder--;
            }
            removedCount++;
        }

        _context.StepTemplates.Remove(step);
        await _context.SaveChangesAsync();

        if (removedCount > 0)
            TempData["Success"] = $"Step removed from template and {removedCount} order(s) updated.";
        else
            TempData["Success"] = "Step removed from template. No pending order steps matched (already started or completed).";
        return RedirectToPage("./Edit", new { id = templateId });
    }
}
