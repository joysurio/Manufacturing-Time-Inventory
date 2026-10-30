using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using System.Security.Claims;

namespace ManufacturingTimeTracking.Pages.PlantMonitor;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public enum GroupByMode
    {
        Workstation,
        Person,
        Machine
    }

    [BindProperty(SupportsGet = true)]
    public GroupByMode GroupBy { get; set; } = GroupByMode.Person;

    public sealed class ActiveRunVm
    {
        public int StepRunId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime StartedAtLocal { get; set; }
        public int ElapsedSeconds { get; set; }
        public int? EstimatedSeconds { get; set; }

        public int? WorkstationId { get; set; }
        public string WorkstationName { get; set; } = "";

        public string OperatorUserId { get; set; } = "";
        public string OperatorDisplayName { get; set; } = "";

        public string OrderRef { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string MachineModel { get; set; } = "";
        public string MachineVariant { get; set; } = "";
        public int MachineModelId { get; set; }
        public int MachineVariantId { get; set; }
        public int? OrderId { get; set; }

        public string StepTitle { get; set; } = "";
    }

    public sealed class GroupCardVm
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public bool IsBusy => ActiveRuns.Count > 0;
        public List<ActiveRunVm> ActiveRuns { get; set; } = new();
    }

    public List<GroupCardVm> Groups { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetSnapshotAsync()
    {
        var activeCount = await _context.StepRuns.CountAsync(r => r.FinishedAt == null && r.WorkstationId != null);
        return new JsonResult(new { forceReload = false, activeCount });
    }

    private async Task LoadAsync()
    {
        var running = await _context.StepRuns
            .AsNoTracking()
            .Where(r => r.FinishedAt == null)
            .Include(r => r.Workstation)
            .Include(r => r.StepExec)
                .ThenInclude(se => se.PhaseExec)
                    .ThenInclude(pe => pe.BuildExecution)
                        .ThenInclude(be => be.BuildOrder)
            .ToListAsync();

        var nowUtc = DateTime.UtcNow;

        // Load model/variant names for running orders
        var orderIds = running.Select(r => r.StepExec.PhaseExec.BuildExecution.BuildOrderId).Distinct().ToList();
        var orders = await _context.BuildOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToListAsync();
        var modelIds = orders.Select(o => o.MachineModelId).Distinct().ToList();
        var variantIds = orders.Select(o => o.MachineVariantId).Distinct().ToList();
        var models = await _context.MachineModels.AsNoTracking().Where(m => modelIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
        var variants = await _context.MachineVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name);

        // Operator display name
        var userIds = running.Select(r => r.UserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var userEntities = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync();
        var users = userEntities.ToDictionary(u => u.Id, u =>
        {
            // Prefer first/last name when available
            var name = (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName))
                ? $"{(u.FirstName ?? "").Trim()} {(u.LastName ?? "").Trim()}".Trim()
                : (u.UserName ?? u.Email ?? u.Id);
            var at = name.IndexOf('@');
            if (at > 0) name = name[..at];
            return name;
        });

        var activeRuns = running.Select(run =>
        {
            var order = run.StepExec.PhaseExec.BuildExecution.BuildOrder;
            var elapsed = (int)Math.Max(0, (nowUtc - run.StartedAt).TotalSeconds);
            return new ActiveRunVm
            {
                StepRunId = run.Id,
                StartedAtUtc = run.StartedAt,
                StartedAtLocal = run.StartedAt.ToLocalTime(),
                ElapsedSeconds = elapsed,
                EstimatedSeconds = null,
                WorkstationId = run.WorkstationId,
                WorkstationName = run.Workstation?.Name ?? "—",
                OperatorUserId = run.UserId,
                OperatorDisplayName = users.TryGetValue(run.UserId, out var dn) ? dn : run.UserId,
                OrderRef = order.ExternalRef,
                SerialNumber = order.SerialNumber,
                MachineModel = models.TryGetValue(order.MachineModelId, out var mn) ? mn : $"Model {order.MachineModelId}",
                MachineVariant = variants.TryGetValue(order.MachineVariantId, out var vn) ? vn : $"Variant {order.MachineVariantId}",
                MachineModelId = order.MachineModelId,
                MachineVariantId = order.MachineVariantId,
                OrderId = order.Id,
                StepTitle = run.StepExec.Title
            };
        }).ToList();
        // Load template target seconds for runs that reference a template
        var templateIds = running.Select(r => r.StepExec.StepTemplateId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        Dictionary<int, int?> tplMapById = new();
        if (templateIds.Any())
        {
            tplMapById = await _context.StepTemplates.AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TargetSeconds);
        }

        // Additionally preload templates by model/variant/title for fallback matching when StepTemplateId is missing
        var templatesByKey = new Dictionary<(int modelId, int variantId, string title), int?>();
        if (modelIds.Any() && variantIds.Any())
        {
            var templates = await _context.StepTemplates.AsNoTracking()
                .Include(st => st.PhaseTemplate).ThenInclude(pt => pt.ProcessTemplate)
                .Where(st => st.PhaseTemplate != null && st.PhaseTemplate.ProcessTemplate != null && modelIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineModelId) && variantIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineVariantId))
                .ToListAsync();
            foreach (var t in templates)
            {
                var key = (t.PhaseTemplate.ProcessTemplate.MachineModelId, t.PhaseTemplate.ProcessTemplate.MachineVariantId, (t.Title ?? "").Trim());
                if (!templatesByKey.ContainsKey(key)) templatesByKey[key] = t.TargetSeconds;
            }
        }

        // Map TargetSeconds back into ActiveRunVm EstimatedSeconds using template id first, then fallback by title+model+variant
        for (int i = 0; i < activeRuns.Count; i++)
        {
            var run = running[i];
            var vm = activeRuns[i];
            if (run.StepExec.StepTemplateId.HasValue && tplMapById.TryGetValue(run.StepExec.StepTemplateId.Value, out var targetById))
            {
                vm.EstimatedSeconds = targetById;
                continue;
            }
            // fallback: match by title/model/variant
            try
            {
                var order = run.StepExec.PhaseExec.BuildExecution.BuildOrder;
                var modelId = order.MachineModelId;
                var variantId = order.MachineVariantId;
                var title = (run.StepExec.Title ?? "").Trim();
                var key = (modelId, variantId, title);
                if (!string.IsNullOrEmpty(title) && templatesByKey.TryGetValue(key, out var targetByTitle))
                {
                    vm.EstimatedSeconds = targetByTitle;
                }
            }
            catch { /* ignore and leave EstimatedSeconds null */ }
        }

        Groups = GroupBy switch
        {
            GroupByMode.Person => activeRuns
                .GroupBy(r => r.OperatorUserId)
                .Select(g => new GroupCardVm
                {
                    Key = g.Key,
                    Title = users.TryGetValue(g.Key, out var dn) ? dn : g.Key,
                    Subtitle = "Operator",
                    ActiveRuns = g.OrderByDescending(x => x.StartedAtUtc).ToList()
                })
                .OrderBy(g => g.Title)
                .ToList(),

            GroupByMode.Machine => activeRuns
                .GroupBy(r => $"{r.MachineModel}||{r.MachineVariant}")
                .Select(g => new GroupCardVm
                {
                    Key = g.Key,
                    Title = g.First().MachineModel,
                    Subtitle = g.First().MachineVariant,
                    ActiveRuns = g.OrderByDescending(x => x.StartedAtUtc).ToList()
                })
                .OrderBy(g => g.Title)
                .ThenBy(g => g.Subtitle)
                .ToList(),

            _ => activeRuns
                .Where(r => r.WorkstationId != null)
                .GroupBy(r => r.WorkstationId!.Value)
                .Select(g => new GroupCardVm
                {
                    Key = g.Key.ToString(),
                    Title = g.First().WorkstationName,
                    Subtitle = null,
                    ActiveRuns = g.OrderByDescending(x => x.StartedAtUtc).Take(1).ToList()
                })
                .OrderBy(g => g.Title)
                .ToList()
        };

        // Add idle workstations only when grouping by workstation.
        if (GroupBy == GroupByMode.Workstation)
        {
            var wsList = await _context.Workstations.AsNoTracking().OrderBy(w => w.Name).ToListAsync();
            var existingKeys = Groups.Select(g => g.Key).ToHashSet();
            foreach (var ws in wsList)
            {
                var key = ws.Id.ToString();
                if (existingKeys.Contains(key)) continue;
                Groups.Add(new GroupCardVm { Key = key, Title = ws.Name, Subtitle = ws.DeviceId, ActiveRuns = new() });
            }
            Groups = Groups.OrderBy(g => g.Title).ToList();
        }
    }

    public static string FormatDuration(int seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        }
        return $"{ts.Minutes}m {ts.Seconds:00}s";
    }
}
