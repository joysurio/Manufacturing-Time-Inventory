using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Reports;

[Authorize(Roles = "Admin,Supervisor")]
public class BackfillStepTargetsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public BackfillStepTargetsModel(ApplicationDbContext db) { _db = db; }

    public int UpdatedCount { get; set; }
    public int TotalChecked { get; set; }
    public string Message { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        // Load all step execs that do not have TargetSeconds set
        var stepExecs = await _db.StepExecs
            .Include(s => s.PhaseExec).ThenInclude(p => p.BuildExecution).ThenInclude(b => b.BuildOrder)
            .Where(s => s.TargetSeconds == null)
            .ToListAsync();

        TotalChecked = stepExecs.Count;
        if (TotalChecked == 0)
        {
            Message = "No StepExec rows to update.";
            return Page();
        }

        // Preload templates referenced by id
        var templateIds = stepExecs.Where(s => s.StepTemplateId.HasValue).Select(s => s.StepTemplateId!.Value).Distinct().ToList();
        var tplMap = new Dictionary<int,int?>();
        if (templateIds.Any())
        {
            tplMap = await _db.StepTemplates.AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TargetSeconds);
        }

        // Also preload templates by model/variant/title for fallback
        var modelIds = stepExecs.Select(s => s.PhaseExec.BuildExecution.BuildOrder.MachineModelId).Distinct().ToList();
        var variantIds = stepExecs.Select(s => s.PhaseExec.BuildExecution.BuildOrder.MachineVariantId).Distinct().ToList();
        var templatesByKey = new Dictionary<(int modelId,int variantId,string title), int?>();
        if (modelIds.Any() && variantIds.Any())
        {
            var templates = await _db.StepTemplates.AsNoTracking()
                .Include(st => st.PhaseTemplate).ThenInclude(pt => pt.ProcessTemplate)
                .Where(st => st.PhaseTemplate != null && st.PhaseTemplate.ProcessTemplate != null && modelIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineModelId) && variantIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineVariantId))
                .ToListAsync();
            foreach (var t in templates)
            {
                var key = (t.PhaseTemplate.ProcessTemplate.MachineModelId, t.PhaseTemplate.ProcessTemplate.MachineVariantId, (t.Title ?? "").Trim());
                if (!templatesByKey.ContainsKey(key)) templatesByKey[key] = t.TargetSeconds;
            }
        }

        int updated = 0;
        foreach (var s in stepExecs)
        {
            int? target = null;
            if (s.StepTemplateId.HasValue && tplMap.TryGetValue(s.StepTemplateId.Value, out var tv)) target = tv;
            if (target == null)
            {
                try
                {
                    var order = s.PhaseExec.BuildExecution.BuildOrder;
                    var key = (order.MachineModelId, order.MachineVariantId, (s.Title ?? "").Trim());
                    if (!string.IsNullOrEmpty(key.Item3) && templatesByKey.TryGetValue(key, out var ttv)) target = ttv;
                }
                catch { }
            }

            if (target.HasValue)
            {
                s.TargetSeconds = target;
                updated++;
            }
        }

        await _db.SaveChangesAsync();
        UpdatedCount = updated;
        Message = $"Updated {updated} / {TotalChecked} step execs.";
        return Page();
    }
}
