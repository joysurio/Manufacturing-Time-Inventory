using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Services;
using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ManufacturingTimeTracking.Pages.Templates;

[Authorize(Policy = "CanEditSteps")]
public class EditStepModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public EditStepModel(ApplicationDbContext context, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _context = context;
        _configuration = configuration;
        _environment = environment;
    }

    public StepTemplate StepTemplate { get; set; } = default!;
    public PhaseTemplate PhaseTemplate { get; set; } = default!;
    public ProcessTemplate ProcessTemplate { get; set; } = default!;
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public StepEditInputModel StepEdit { get; set; } = new();

    public SelectList AvailableTools { get; set; } = default!;
    public SelectList AvailableMaterials { get; set; } = default!;
    public List<StepTemplateTool> StepTools { get; set; } = new();
    public List<StepTemplateMaterial> StepMaterials { get; set; } = new();
    public List<StepTemplateMedia> StepMedia { get; set; } = new();

    public class StepEditInputModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public bool AllowSkip { get; set; } = true;
            public bool Hidden { get; set; } = false;
        [BindNever] // Optional: do not validate; we bind manually from form
        public List<int> SelectedToolIds { get; set; } = new();
        [BindNever] // Optional: do not validate; we bind manually from form
        public List<int> SelectedMaterialIds { get; set; } = new();
        [BindNever] // Optional: do not validate; we bind manually from form
        public List<decimal?> MaterialQuantities { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var step = await _context.StepTemplates
            .Include(s => s.PhaseTemplate)
                .ThenInclude(p => p.ProcessTemplate)
            .Include(s => s.Tools)
                .ThenInclude(st => st.Tool)
            .Include(s => s.Materials)
                .ThenInclude(sm => sm.Material)
            .AsSplitQuery()
            .Include(s => s.Media)
                .ThenInclude(sm => sm.Media)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (step == null)
        {
            return NotFound();
        }

        StepTemplate = step;
        PhaseTemplate = step.PhaseTemplate;
        ProcessTemplate = step.PhaseTemplate.ProcessTemplate;

        StepEdit.Id = step.Id;
        StepEdit.Title = step.Title;
        StepEdit.Instructions = step.Instructions;
        StepEdit.AllowSkip = step.AllowSkip;
        StepEdit.Hidden = step.Hidden;
        StepEdit.SelectedToolIds = step.Tools.Select(t => t.ToolId).ToList();
        StepEdit.SelectedMaterialIds = step.Materials.Select(m => m.MaterialId).ToList();
        StepEdit.MaterialQuantities = step.Materials.Select(m => m.Qty).ToList();

        StepTools = step.Tools.ToList();
        StepMaterials = step.Materials.ToList();
        StepMedia = step.Media.ToList();

        // Load available tools and materials
        var tools = await _context.Tools.OrderBy(t => t.Name).ToListAsync();
        var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();

        AvailableTools = new SelectList(tools, "Id", "Name");
        var materialItems = materials
            .Select(m =>
            {
                var label = string.IsNullOrWhiteSpace(m.Reference) ? m.Name : $"{m.Name} (Ref: {m.Reference})";
                return new SelectListItem { Value = m.Id.ToString(), Text = label };
            })
            .ToList();
        AvailableMaterials = new SelectList(materialItems, "Value", "Text");

        // For client-side material search: allow typing either Name or Reference and still resolve MaterialId.
        ViewData["MaterialsSearchOptions"] = materials
            .Select(m => new
            {
                id = m.Id.ToString(),
                name = m.Name ?? "",
                reference = m.Reference ?? "",
                label = string.IsNullOrWhiteSpace(m.Reference) ? (m.Name ?? "") : $"{m.Name} (Ref: {m.Reference})"
            })
            .ToList();
        
        // Store material images in ViewData for JavaScript access
        ViewData["MaterialImages"] = materials.Where(m => !string.IsNullOrEmpty(m.ImageUrl))
            .ToDictionary(m => m.Id.ToString(), m => m.ImageUrl ?? "");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Clear optional-field validation first so tools/materials never block save
        RemoveOptionalFieldErrors();

        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<EditStepModel>>();
        logger.LogInformation("OnPostAsync called. StepEdit is null: {IsNull}, StepEdit.Id: {Id}", 
            StepEdit == null, StepEdit?.Id ?? 0);
        
        // Validate that StepEdit has an Id
        if (StepEdit == null || StepEdit.Id <= 0)
        {
            logger.LogWarning("Invalid step data - StepEdit is null or Id is invalid");
            TempData["Error"] = $"Invalid step data. StepEdit is null: {StepEdit == null}, Id: {StepEdit?.Id ?? 0}";
            // Try to get ProcessTemplateId from route or query
            var processTemplateId = Request.Query["processTemplateId"].FirstOrDefault();
            if (string.IsNullOrEmpty(processTemplateId))
            {
                return RedirectToPage("./Index");
            }
            return RedirectToPage("./Edit", new { id = int.Parse(processTemplateId) });
        }
        
        logger.LogInformation("Processing save for Step Id: {StepId}, Title: '{Title}'", 
            StepEdit.Id, StepEdit.Title);

        // Bind optional collections from form (they have [BindNever] so binder does not set them)
        var toolIdsFromForm = Request.Form["StepEdit.SelectedToolIds"];
        StepEdit.SelectedToolIds = toolIdsFromForm.Count > 0
            ? toolIdsFromForm.Select(v => int.TryParse(v, out var id) ? id : 0).Where(id => id != 0).ToList()
            : new List<int>();

        // Load step with minimal includes first to get ProcessTemplate ID
        // Use AsNoTracking to avoid tracking conflicts later
        var step = await _context.StepTemplates
            .Include(s => s.PhaseTemplate)
                .ThenInclude(p => p.ProcessTemplate)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StepEdit.Id);

        if (step == null)
        {
            TempData["Error"] = "Step not found.";
            return NotFound();
        }
        
        // Store ProcessTemplate for use in error handling
        var processTemplate = step.PhaseTemplate.ProcessTemplate;
        
        // Load Tools and Materials separately for updating
        var existingTools = await _context.StepTemplateTools
            .Where(st => st.StepTemplateId == StepEdit.Id)
            .ToListAsync();
        var existingMaterials = await _context.StepTemplateMaterials
            .Where(sm => sm.StepTemplateId == StepEdit.Id)
            .ToListAsync();

        // Manually bind SelectedMaterialIds and MaterialQuantities from form
        var materialIdsFromForm = Request.Form.Where(k => k.Key.StartsWith("StepEdit.SelectedMaterialIds["));
        var quantitiesFromForm = Request.Form.Where(k => k.Key.StartsWith("StepEdit.MaterialQuantities["));

        if (materialIdsFromForm.Any())
        {
            StepEdit.SelectedMaterialIds = new List<int>();
            StepEdit.MaterialQuantities = new List<decimal?>();

            var materialDict = materialIdsFromForm
                .Select(kvp => new { Index = ExtractIndex(kvp.Key), Value = kvp.Value.ToString() })
                .Where(x => !string.IsNullOrEmpty(x.Value) && int.TryParse(x.Value, out _))
                .ToDictionary(x => x.Index, x => int.Parse(x.Value));

            var quantityDict = quantitiesFromForm
                .Select(kvp => new { Index = ExtractIndex(kvp.Key), Value = kvp.Value.ToString() })
                .Where(x => !string.IsNullOrEmpty(x.Value) && TryParseFlexibleDecimal(x.Value, out _))
                .ToDictionary(x => x.Index, x => TryParseFlexibleDecimal(x.Value, out var dec) ? (decimal?)dec : null);

            // Sort by index and add to lists
            foreach (var kvp in materialDict.OrderBy(x => x.Key))
            {
                StepEdit.SelectedMaterialIds.Add(kvp.Value);
                StepEdit.MaterialQuantities.Add(quantityDict.ContainsKey(kvp.Key) ? quantityDict[kvp.Key] : null);
            }
        }
        else
        {
            StepEdit.SelectedMaterialIds = StepEdit.SelectedMaterialIds ?? new List<int>();
            StepEdit.MaterialQuantities = StepEdit.MaterialQuantities ?? new List<decimal?>();
        }

        RemoveOptionalFieldErrors();
        
        // Handle AllowSkip checkbox binding explicitly
        // When checkbox is checked: form sends both hidden "false" and checkbox "true"
        // When unchecked: only hidden "false" is sent
        // Find the form key that contains "AllowSkip" (could be "StepEdit.AllowSkip" or "StepEdit_AllowSkip")
        var allowSkipFormKey = Request.Form.Keys.FirstOrDefault(k => k.Contains("AllowSkip"));
        
        if (allowSkipFormKey != null)
        {
            var formValues = Request.Form[allowSkipFormKey].ToList();
            
            // If multiple values exist, checkbox was checked (hidden=false, checkbox=true)
            // If only one value exists, check if it's "true" (checked) or "false" (unchecked)
            if (formValues.Count > 1)
            {
                // Multiple values means checkbox was checked - take the "true" value
                StepEdit.AllowSkip = formValues.Any(v => v == "true");
            }
            else if (formValues.Count == 1)
            {
                // Single value - check if it's "true" (checked) or "false" (unchecked)
                StepEdit.AllowSkip = formValues[0] == "true";
            }
            else
            {
                StepEdit.AllowSkip = false;
            }
        }
        else
        {
            // If form key not found, check if model binding set it
            // If StepEdit.AllowSkip is still at default (true), it means checkbox wasn't checked
            // But we can't rely on default value, so check ModelState
            if (!ModelState.ContainsKey("StepEdit.AllowSkip") && 
                !ModelState.ContainsKey("StepEdit_AllowSkip") &&
                Request.Form.Keys.Any(k => k.Contains("StepEdit")))
            {
                // Form was submitted but AllowSkip key not found - checkbox was unchecked
                StepEdit.AllowSkip = false;
            }
            // Otherwise trust the model binding value
        }
        
        // Validate required fields
        if (string.IsNullOrWhiteSpace(StepEdit.Title))
        {
            ModelState.AddModelError("StepEdit.Title", "Title is required.");
        }

        // Handle Hidden checkbox binding explicitly (same pattern as AllowSkip)
        var hiddenFormKey = Request.Form.Keys.FirstOrDefault(k => k.Contains("Hidden"));
        if (hiddenFormKey != null)
        {
            var formValues = Request.Form[hiddenFormKey].ToList();
            if (formValues.Count > 1)
            {
                StepEdit.Hidden = formValues.Any(v => v == "true");
            }
            else if (formValues.Count == 1)
            {
                StepEdit.Hidden = formValues[0] == "true";
            }
            else
            {
                StepEdit.Hidden = false;
            }
        }

        RemoveOptionalFieldErrors();

        // Log ModelState errors for debugging
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value.Errors.Count > 0)
                .Select(x => new { Key = x.Key, Errors = x.Value.Errors.Select(e => e.ErrorMessage) })
                .ToList();
            
            logger.LogWarning("ModelState is invalid. Errors: {Errors}", 
                string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(", ", e.Errors)}")));
            
            TempData["Error"] = $"Validation failed: {string.Join("; ", errors.SelectMany(e => e.Errors))}";
            
            // Reload data for display but preserve user input
            var stepReload = await _context.StepTemplates
                .Include(s => s.PhaseTemplate)
                    .ThenInclude(p => p.ProcessTemplate)
                .Include(s => s.Tools)
                    .ThenInclude(st => st.Tool)
                .Include(s => s.Materials)
                    .ThenInclude(sm => sm.Material)
                .Include(s => s.Media)
                    .ThenInclude(sm => sm.Media)
                .FirstOrDefaultAsync(s => s.Id == StepEdit.Id);

            if (stepReload != null)
            {
                StepTemplate = stepReload;
                PhaseTemplate = stepReload.PhaseTemplate;
                ProcessTemplate = stepReload.PhaseTemplate.ProcessTemplate;
                StepTools = stepReload.Tools.ToList();
                StepMaterials = stepReload.Materials.ToList();
                StepMedia = stepReload.Media.ToList();

                var tools = await _context.Tools.OrderBy(t => t.Name).ToListAsync();
                var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
                AvailableTools = new SelectList(tools, "Id", "Name");
                AvailableMaterials = new SelectList(materials, "Id", "Name");
                
                // Preserve user input - StepEdit is already bound from the form
                // Ensure StepEdit values are preserved
                if (StepEdit != null)
                {
                    StepEdit.Id = stepReload.Id;
                    // Title, Instructions, and AllowSkip are already bound from form
                }
            }
            return Page();
        }

        try
        {
            logger.LogInformation("Starting save operation for Step Id: {StepId}", StepEdit.Id);
            
            // Load the entity WITHOUT tracking first to avoid conflicts
            var stepToUpdate = await _context.StepTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == StepEdit.Id);
            
            if (stepToUpdate == null)
            {
                logger.LogError("Step not found with Id: {StepId}", StepEdit.Id);
                TempData["Error"] = $"Step not found with Id: {StepEdit.Id}";
                return RedirectToPage("./Edit", new { id = processTemplate.Id });
            }
            
            logger.LogInformation("Current values - Title: '{CurrentTitle}', Instructions length: {CurrentInstructionsLength}, AllowSkip: {CurrentAllowSkip}", 
                stepToUpdate.Title, stepToUpdate.Instructions?.Length ?? 0, stepToUpdate.AllowSkip);
            logger.LogInformation("New values - Title: '{NewTitle}', Instructions length: {NewInstructionsLength}, AllowSkip: {NewAllowSkip}", 
                StepEdit.Title?.Trim() ?? string.Empty, StepEdit.Instructions?.Length ?? 0, StepEdit.AllowSkip);
            
            // Update step properties
            stepToUpdate.Title = StepEdit.Title?.Trim() ?? string.Empty;
            stepToUpdate.Instructions = StepEdit.Instructions ?? string.Empty;
            stepToUpdate.AllowSkip = StepEdit.AllowSkip;
            stepToUpdate.Hidden = StepEdit.Hidden;
            
            // Use the same pattern as other working edit pages - Attach and set state to Modified
            // This ensures EF Core will save ALL properties
            _context.Attach(stepToUpdate).State = EntityState.Modified;
            
            // Explicitly mark properties as modified to ensure they're saved
            var entry = _context.Entry(stepToUpdate);
            entry.Property(s => s.Title).IsModified = true;
            entry.Property(s => s.Instructions).IsModified = true;
            entry.Property(s => s.AllowSkip).IsModified = true;
            entry.Property(s => s.Hidden).IsModified = true;
            
            logger.LogInformation("Entity attached. State: {State}, Title modified: {TitleModified}, Instructions modified: {InstructionsModified}, AllowSkip modified: {AllowSkipModified}",
                entry.State, entry.Property(s => s.Title).IsModified, 
                entry.Property(s => s.Instructions).IsModified, 
                entry.Property(s => s.AllowSkip).IsModified);

            // Update tools - remove tools that are no longer selected
            var existingToolIds = existingTools.Select(t => t.ToolId).ToList();
            var selectedToolIds = StepEdit.SelectedToolIds ?? new List<int>();
            var toolsToRemove = existingTools.Where(t => !selectedToolIds.Contains(t.ToolId)).ToList();
            var toolsToAdd = selectedToolIds.Where(id => !existingToolIds.Contains(id)).ToList();

            foreach (var toolToRemove in toolsToRemove)
            {
                _context.StepTemplateTools.Remove(toolToRemove);
            }

            foreach (var toolId in toolsToAdd)
            {
                _context.StepTemplateTools.Add(new StepTemplateTool
                {
                    StepTemplateId = stepToUpdate.Id, // Use stepToUpdate.Id instead of step.Id
                    ToolId = toolId
                });
            }

            // Update materials - remove all first, then add back selected ones
            foreach (var materialToRemove in existingMaterials)
            {
                _context.StepTemplateMaterials.Remove(materialToRemove);
            }

            // Add back selected materials with quantities
            if (StepEdit.SelectedMaterialIds != null && StepEdit.MaterialQuantities != null)
            {
                for (int i = 0; i < StepEdit.SelectedMaterialIds.Count; i++)
                {
                    var materialId = StepEdit.SelectedMaterialIds[i];
                    if (materialId > 0) // Only add if a material is selected
                    {
                        var quantity = i < StepEdit.MaterialQuantities.Count ? StepEdit.MaterialQuantities[i] : null;
                        _context.StepTemplateMaterials.Add(new StepTemplateMaterial
                        {
                            StepTemplateId = stepToUpdate.Id, // Use stepToUpdate.Id instead of step.Id
                            MaterialId = materialId,
                            Qty = quantity
                        });
                    }
                }
            }

            // Save all changes to database
            logger.LogInformation("Calling SaveChangesAsync...");
            var changesSaved = await _context.SaveChangesAsync();
            logger.LogInformation("SaveChangesAsync completed. {ChangesSaved} entities were updated.", changesSaved);
            
            // Verify changes were actually saved
            if (changesSaved == 0)
            {
                logger.LogWarning("SaveChangesAsync returned 0 - no changes detected. Forcing update...");
                
                // Force update using Update method
                _context.StepTemplates.Update(stepToUpdate);
                var entry2 = _context.Entry(stepToUpdate);
                entry2.Property(s => s.Title).IsModified = true;
                entry2.Property(s => s.Instructions).IsModified = true;
                entry2.Property(s => s.AllowSkip).IsModified = true;
                entry2.Property(s => s.Hidden).IsModified = true;
                
                changesSaved = await _context.SaveChangesAsync();
                logger.LogInformation("Forced update SaveChangesAsync completed. {ChangesSaved} entities were updated.", changesSaved);
            }
            
            if (changesSaved > 0)
            {
                logger.LogInformation("Successfully saved {ChangesSaved} changes to database.", changesSaved);
                await PackingListSync.SyncFromTemplateStepsAsync(_context, processTemplate.MachineModelId);
                TempData["Success"] = "Step updated successfully!";
            }
            else
            {
                logger.LogError("Failed to save changes - SaveChangesAsync returned 0");
                TempData["Error"] = "Warning: Changes may not have been saved. Please verify in the database.";
            }
            
            // If a return URL was provided, redirect back there (must be local)
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                // Use LocalRedirect if it's a local path, otherwise fallback to template edit
                if (Url.IsLocalUrl(ReturnUrl))
                    return LocalRedirect(ReturnUrl);
            }

            return RedirectToPage("./Edit", new { id = processTemplate.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred while saving step {StepId}", StepEdit.Id);
            TempData["Error"] = $"An error occurred while saving: {ex.Message}. Inner exception: {ex.InnerException?.Message ?? "None"}";
            
            // Reload data for display
            var stepReload = await _context.StepTemplates
                .Include(s => s.PhaseTemplate)
                    .ThenInclude(p => p.ProcessTemplate)
                .Include(s => s.Tools)
                    .ThenInclude(st => st.Tool)
                .Include(s => s.Materials)
                    .ThenInclude(sm => sm.Material)
                .Include(s => s.Media)
                    .ThenInclude(sm => sm.Media)
                .FirstOrDefaultAsync(s => s.Id == StepEdit.Id);

            if (stepReload != null)
            {
                StepTemplate = stepReload;
                PhaseTemplate = stepReload.PhaseTemplate;
                ProcessTemplate = stepReload.PhaseTemplate.ProcessTemplate;
                StepTools = stepReload.Tools.ToList();
                StepMaterials = stepReload.Materials.ToList();
                StepMedia = stepReload.Media.ToList();

                var tools = await _context.Tools.OrderBy(t => t.Name).ToListAsync();
                var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
                AvailableTools = new SelectList(tools, "Id", "Name");
                AvailableMaterials = new SelectList(materials, "Id", "Name");
                
                // Preserve user input
                if (StepEdit != null)
                {
                    StepEdit.Id = stepReload.Id;
                }
            }
            return Page();
        }
    }

    private static bool TryParseFlexibleDecimal(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();

        // Accept both "1.5" and "1,5" regardless of server culture.
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out value)) return true;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;

        // Last resort: swap comma/dot
        var swapped = s.Contains(',') && !s.Contains('.') ? s.Replace(',', '.') : s.Replace('.', ',');
        return decimal.TryParse(swapped, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(swapped, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>
    /// Removes all ModelState errors for optional fields (Select Tools, Materials, Quantities).
    /// These fields must not block save - user can leave tools/materials empty.
    /// </summary>
    private void RemoveOptionalFieldErrors()
    {
        // Remove keys that match optional field names
        var keysToRemove = ModelState.Keys
            .Where(k => k != null && (
                k.Contains("SelectedToolIds", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("SelectedMaterialIds", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("MaterialQuantities", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var key in keysToRemove)
        {
            ModelState.Remove(key);
        }
        // Exact keys in case binder uses different format
        ModelState.Remove("StepEdit.SelectedToolIds");
        ModelState.Remove("StepEdit_SelectedToolIds");
        ModelState.Remove("StepEdit.SelectedMaterialIds");
        ModelState.Remove("StepEdit_MaterialQuantities");
        // When binder attaches error to parent StepEdit, clear those errors
        if (ModelState.TryGetValue("StepEdit", out var stepEditEntry) && stepEditEntry?.Errors.Count > 0)
        {
            var toKeep = stepEditEntry.Errors
                .Where(e => e.ErrorMessage == null || (
                    !e.ErrorMessage.Contains("SelectedToolIds", StringComparison.OrdinalIgnoreCase) &&
                    !e.ErrorMessage.Contains("SelectedMaterialIds", StringComparison.OrdinalIgnoreCase) &&
                    !e.ErrorMessage.Contains("MaterialQuantities", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            stepEditEntry.Errors.Clear();
            foreach (var err in toKeep)
                stepEditEntry.Errors.Add(err);
        }
    }

    private int ExtractIndex(string key)
    {
        var start = key.IndexOf('[') + 1;
        var end = key.IndexOf(']');
        if (start > 0 && end > start)
        {
            if (int.TryParse(key.Substring(start, end - start), out int index))
            {
                return index;
            }
        }
        return -1;
    }

    public async Task<IActionResult> OnPostUploadMediaAsync(int stepId, IFormFile? file, string? caption)
    {
        // Check if this is an AJAX request
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        var step = await _context.StepTemplates
            .Include(s => s.PhaseTemplate)
            .FirstOrDefaultAsync(s => s.Id == stepId);

        if (step == null)
        {
            if (isAjax)
            {
                return new JsonResult(new { success = false, message = "Step not found." }) { StatusCode = 404 };
            }
            return NotFound();
        }

        if (file == null || file.Length == 0)
        {
            if (isAjax)
            {
                return new JsonResult(new { success = false, message = "Please select a file to upload." }) { StatusCode = 400 };
            }
            TempData["Error"] = "Please select a file to upload.";
            return RedirectToPage("./EditStep", new { id = stepId });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
        // Save under WebRootPath so files are served by UseStaticFiles (avoids 404 when running from VS where CurrentDirectory can be bin/Debug)
        var uploadsRoot = Path.Combine(_environment.WebRootPath ?? "", "uploads");
        var templatePath = Path.Combine(uploadsRoot, "templates", step.PhaseTemplate.ProcessTemplateId.ToString(), stepId.ToString());

        if (!Directory.Exists(templatePath))
        {
            Directory.CreateDirectory(templatePath);
        }

        var extension = Path.GetExtension(file.FileName).ToLower();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".pdf", ".mp4", ".avi", ".mov", ".webm" };
        
        if (!allowedExtensions.Contains(extension))
        {
            if (isAjax)
            {
                return new JsonResult(new { success = false, message = "Invalid file type. Allowed: images, PDFs, and videos." }) { StatusCode = 400 };
            }
            TempData["Error"] = "Invalid file type. Allowed: images, PDFs, and videos.";
            return RedirectToPage("./EditStep", new { id = stepId });
        }

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(templatePath, fileName);
        var relativePath = Path.Combine("templates", step.PhaseTemplate.ProcessTemplateId.ToString(), stepId.ToString(), fileName).Replace('\\', '/');

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Determine media type
        var mediaType = extension == ".pdf" ? "PDF" 
            : (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".webm") ? "Video" 
            : "Image";

        var media = new ManufacturingTimeTracking.Models.Templates.Media
        {
            Type = mediaType,
            UrlOrPath = relativePath,
            UploadedBy = userId,
            UploadedAt = DateTime.UtcNow
        };
        _context.Media.Add(media);
        await _context.SaveChangesAsync();

        var stepMedia = new StepTemplateMedia
        {
            StepTemplateId = stepId,
            MediaId = media.Id,
            Caption = caption
        };
        _context.StepTemplateMedia.Add(stepMedia);
        await _context.SaveChangesAsync();

        if (isAjax)
        {
            return new JsonResult(new 
            { 
                success = true, 
                message = "File uploaded successfully!",
                stepMediaId = stepMedia.Id,
                mediaId = media.Id,
                mediaType = mediaType,
                url = relativePath,
                caption = caption ?? ""
            });
        }

        TempData["Success"] = "File uploaded successfully!";
        return RedirectToPage("./EditStep", new { id = stepId });
    }

    public async Task<IActionResult> OnPostDeleteMediaAsync(int stepMediaId)
    {
        // Check if this is an AJAX request
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        var stepMedia = await _context.StepTemplateMedia
            .Include(sm => sm.StepTemplate)
            .FirstOrDefaultAsync(sm => sm.Id == stepMediaId);

        if (stepMedia == null)
        {
            if (isAjax)
            {
                return new JsonResult(new { success = false, message = "Media not found." }) { StatusCode = 404 };
            }
            return NotFound();
        }

        var stepId = stepMedia.StepTemplateId;
        _context.StepTemplateMedia.Remove(stepMedia);
        await _context.SaveChangesAsync();

        if (isAjax)
        {
            return new JsonResult(new 
            { 
                success = true, 
                message = "Media removed successfully!" 
            });
        }

        TempData["Success"] = "Media removed successfully!";
        return RedirectToPage("./EditStep", new { id = stepId });
    }
}
