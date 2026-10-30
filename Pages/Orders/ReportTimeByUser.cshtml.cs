using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Tasks;
using PaginationInfo = ManufacturingTimeTracking.Models.PaginationInfo;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Policy = "CanUseTimeManagement")]
public class ReportTimeByUserModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportTimeByUserModel> _logger;

    public ReportTimeByUserModel(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userMgr,
        IConfiguration configuration,
        ILogger<ReportTimeByUserModel> logger)
    {
        _context = context;
        _userManager = userMgr;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetExportPdfAsync()
    {
        try
        {
            // Reuse the same filtering logic as CSV export
            if (!FromDate.HasValue)
            {
                FromDate = DateTime.UtcNow.Date;
            }

            var query = _context.StepRuns
                .Include(r => r.StepExec)
                .Include(r => r.StepExec!.PhaseExec)
                    .ThenInclude(p => p!.BuildExecution)
                        .ThenInclude(e => e!.BuildOrder)
                .AsNoTracking()
                .Where(r => r.StepExec != null
                    && r.StepExec.PhaseExec != null
                    && r.StepExec.PhaseExec.BuildExecution != null
                    && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null);

            if (!string.IsNullOrWhiteSpace(UserIdFilter))
                query = query.Where(r => r.UserId == UserIdFilter);

            if (OrderIdFilter.HasValue)
                query = query.Where(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId == OrderIdFilter.Value);

            if (string.Equals(StatusFilter, "InProgress", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.FinishedAt == null);
            else if (string.Equals(StatusFilter, "Completed", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.FinishedAt != null);

            if (FromDate.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(r => r.StartedAt >= fromUtc);
            }

            if (ToDate.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(r => r.StartedAt < toUtc);
            }

            var runs = await query.OrderBy(r => r.StartedAt).ToListAsync();

            var orderIds = runs.Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId).Distinct().ToList();
            var orders = await _context.BuildOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o);

            var modelIds = orders.Values.Select(o => o.MachineModelId).Distinct().ToList();
            var variantIds = orders.Values.Select(o => o.MachineVariantId).Distinct().ToList();

            var models = await _context.MachineModels.AsNoTracking().Where(m => modelIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
            var variants = await _context.MachineVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => string.IsNullOrEmpty(v.Code) ? v.Name : $"{v.Name} ({v.Code})");

            var utcNow = DateTime.UtcNow;
            var rows = runs.Select(r =>
            {
                var order = orders.GetValueOrDefault(r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId);
                var modelName = order != null && models.TryGetValue(order.MachineModelId, out var mn) ? mn : "—";
                var variantName = order != null && variants.TryGetValue(order.MachineVariantId, out var vn) ? vn : "—";
                var durationSeconds = r.FinishedAt.HasValue
                    ? (r.DurationSeconds ?? (int)(r.FinishedAt.Value - r.StartedAt).TotalSeconds)
                    : (int)(utcNow - r.StartedAt).TotalSeconds;
                return new TimeByUserRow
                {
                    StepRunId = r.Id,
                    StepExecId = r.StepExecId,
                    UserId = r.UserId ?? "",
                    UserDisplayName = UserDisplayNames.GetValueOrDefault(r.UserId ?? "", r.UserId ?? ""),
                    MachineModel = modelName,
                    MachineVariant = variantName,
                    OrderRef = order?.ExternalRef ?? "",
                    SerialNumber = order?.SerialNumber ?? "",
                    StepTitle = r.StepExec?.Title ?? "—",
                    StartedAt = r.StartedAt,
                    FinishedAt = r.FinishedAt,
                    DurationSeconds = durationSeconds,
                    IsInProgress = !r.FinishedAt.HasValue,
                    IsInternalTask = false,
                    TaskSessionId = null,
                    CostEuros = Math.Round((decimal)durationSeconds / 3600m * 20m, 2)
                };
            }).ToList();

            // Simple KPIs: total per user
            var userKpis = rows.GroupBy(r => r.UserDisplayName).Select(g => new { User = g.Key, TotalSeconds = g.Sum(x => x.DurationSeconds ?? 0) }).OrderByDescending(x => x.TotalSeconds).ToList();

            // Build PDF using QuestPDF
            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Row(row =>
                    {
                        row.RelativeColumn().Column(col =>
                        {
                            col.Item().Text("Time by User Report").FontSize(18).SemiBold();
                            col.Item().Text($"Filters: from {FromDate?.ToString("yyyy-MM-dd")} to {ToDate?.ToString("yyyy-MM-dd")} ").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Text("KPIs: total time per user").SemiBold().FontSize(12);
                        // Render KPIs in a simple 4-column table so items wrap correctly on page width
                        var kpiList = userKpis.Take(8).ToList();
                        col.Item().Table(kpiTable =>
                        {
                            kpiTable.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn();
                                cd.RelativeColumn();
                                cd.RelativeColumn();
                                cd.RelativeColumn();
                            });

                            foreach (var k in kpiList)
                            {
                                kpiTable.Cell().Border(1).Padding(5).Column(c2 =>
                                {
                                    c2.Item().Text(k.User).SemiBold();
                                    c2.Item().Text((k.TotalSeconds / 3600.0).ToString("0.##") + " h").FontSize(9).FontColor(Colors.Grey.Darken1);
                                });
                            }
                        });

                        col.Item().Text("Detail table").SemiBold().FontSize(12);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(); // User
                                columns.RelativeColumn(); // Machine
                                columns.RelativeColumn(); // Order
                                columns.RelativeColumn(); // Task
                                columns.ConstantColumn(70); // Started
                                columns.ConstantColumn(70); // Finished
                                columns.ConstantColumn(50); // Duration
                                columns.ConstantColumn(50); // Amount
                            });

                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("User");
                                header.Cell().Element(CellStyle).Text("Machine");
                                header.Cell().Element(CellStyle).Text("Order");
                                header.Cell().Element(CellStyle).Text("Task");
                                header.Cell().Element(CellStyle).AlignCenter().Text("Started");
                                header.Cell().Element(CellStyle).AlignCenter().Text("Finished");
                                header.Cell().Element(CellStyle).AlignCenter().Text("Duration");
                                header.Cell().Element(CellStyle).AlignCenter().Text("€");
                            });

                            // Rows
                            foreach (var r in rows)
                            {
                                table.Cell().Element(CellStyle).Text(r.UserDisplayName);
                                table.Cell().Element(CellStyle).Text(r.MachineModel);
                                table.Cell().Element(CellStyle).Text(r.OrderRef + (string.IsNullOrEmpty(r.SerialNumber) ? "" : " / " + r.SerialNumber));
                                table.Cell().Element(CellStyle).Text(r.StepTitle);
                                table.Cell().Element(CellStyle).AlignCenter().Text(r.StartedAt.ToLocalTime().ToString("g"));
                                table.Cell().Element(CellStyle).AlignCenter().Text(r.FinishedAt?.ToLocalTime().ToString("g") ?? (r.IsInProgress ? "In progress" : "—"));
                                table.Cell().Element(CellStyle).AlignCenter().Text(FormatDuration(r.DurationSeconds));
                                table.Cell().Element(CellStyle).AlignCenter().Text("€" + r.CostEuros.ToString("0.00"));
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm UTC}").FontSize(8);
                });
            }).GeneratePdf();

            var fileName = $"TimeByUser_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error generating PDF: {ex.Message}");
        }

        static IContainer CellStyle(IContainer container)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(5);
        }
    }

    /// <summary>Filter by user ID (optional).</summary>
    [BindProperty(SupportsGet = true)]
    public string? UserIdFilter { get; set; }

    /// <summary>Filter by build order ID (optional) — tasks for a specific machine/order.</summary>
    [BindProperty(SupportsGet = true)]
    public int? OrderIdFilter { get; set; }

    /// <summary>Filter by status: All, InProgress, Completed.</summary>
    [BindProperty(SupportsGet = true)
    ]
    public string? StatusFilter { get; set; }

    /// <summary>Filter: from date (inclusive).</summary>
    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    /// <summary>Filter: to date (inclusive).</summary>
    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public List<TimeByUserRow> Rows { get; set; } = new();
    /// <summary>Total time (seconds) for all filtered rows, including in-progress tasks (start to now).</summary>
    public int TotalDurationSeconds { get; set; }
    public Dictionary<string, string> UserDisplayNames { get; set; } = new();
    public SelectList? UserSelectList { get; set; }
    public SelectList? OrderSelectList { get; set; }
    public List<UserHoursKpi> UserHoursKpis { get; set; } = new();
    public List<MachineUserHoursKpi> MachineUserHoursKpis { get; set; } = new();
    public List<MachineCompletionRow> MachineCompletionRows { get; set; } = new();
    public List<MachineUserCompareRow> MachineUserCompareRows { get; set; } = new();
    public List<CompareRow> CompareRows { get; set; } = new();
    /// <summary>Distinct step options (from current result set) for the Add run modal.</summary>
    public List<StepOption> StepOptions { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!FromDate.HasValue)
        {
            // Default to today's date (UTC) if not specified.
            FromDate = DateTime.UtcNow.Date;
        }

        var query = _context.StepRuns
            .Include(r => r.StepExec)
            .Include(r => r.StepExec!.PhaseExec)
                .ThenInclude(p => p!.BuildExecution)
                    .ThenInclude(e => e!.BuildOrder)
            .AsNoTracking()
            .Where(r => r.StepExec != null
                && r.StepExec.PhaseExec != null
                && r.StepExec.PhaseExec.BuildExecution != null
                && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null);

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            query = query.Where(r => r.UserId == UserIdFilter);

        if (OrderIdFilter.HasValue)
            query = query.Where(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId == OrderIdFilter.Value);

        if (string.Equals(StatusFilter, "InProgress", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => r.FinishedAt == null);
        else if (string.Equals(StatusFilter, "Completed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => r.FinishedAt != null);

        if (FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(r => r.StartedAt >= fromUtc);
        }

        if (ToDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(r => r.StartedAt < toUtc);
        }

        var runs = await query
            .OrderBy(r => r.StartedAt)
            .ToListAsync();

        // Orders dropdown: orders that have step runs (with same user/date filters, no order/status filter)
        var dropdownQuery = _context.StepRuns
            .Include(r => r.StepExec)
            .Include(r => r.StepExec!.PhaseExec)
            .Include(r => r.StepExec!.PhaseExec!.BuildExecution)
            .AsNoTracking()
            .Where(r => r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null);
        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            dropdownQuery = dropdownQuery.Where(r => r.UserId == UserIdFilter);
        if (FromDate.HasValue)
            dropdownQuery = dropdownQuery.Where(r => r.StartedAt >= DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc));
        if (ToDate.HasValue)
            dropdownQuery = dropdownQuery.Where(r => r.StartedAt < DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc));
        var orderIdsForDropdown = await dropdownQuery
            .Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId)
            .Distinct()
            .ToListAsync();
        var ordersForDropdown = await _context.BuildOrders
            .Where(o => orderIdsForDropdown.Contains(o.Id))
            .OrderBy(o => o.ExternalRef)
            .ThenBy(o => o.SerialNumber)
            .ToListAsync();
        var orderList = new List<(string Value, string Text)> { ("", "— All orders —") };
        foreach (var o in ordersForDropdown)
            orderList.Add((o.Id.ToString(), string.IsNullOrEmpty(o.SerialNumber) ? o.ExternalRef : $"{o.ExternalRef} / {o.SerialNumber}"));
        OrderSelectList = new SelectList(orderList.Select(x => new { Value = x.Value, Text = x.Text }), "Value", "Text", OrderIdFilter?.ToString());

        var orderIds = runs
            .Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId)
            .Distinct()
            .ToList();

        var orders = await _context.BuildOrders
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync();

        var modelIds = orders.Select(o => o.MachineModelId).Distinct().ToList();
        var variantIds = orders.Select(o => o.MachineVariantId).Distinct().ToList();

        var models = await _context.MachineModels
            .Where(m => modelIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name);
        var variants = await _context.MachineVariants
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => string.IsNullOrEmpty(v.Code) ? v.Name : $"{v.Name} ({v.Code})");

        var userIds = runs.Select(r => r.UserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        foreach (var uid in userIds)
        {
            var user = await _userManager.FindByIdAsync(uid);
            var display = user?.Email ?? user?.UserName ?? uid;
            // If the display value looks like an email, keep only the part before '@' to save space.
            var at = display.IndexOf('@');
            if (at > 0) display = display[..at];
            UserDisplayNames[uid] = display;
        }

        // In-progress tasks: show time from start to "now" (report creation time). E.g. started 13:00, report at 14:00 → 1h; at 15:00 → 2h.
        var utcNow = DateTime.UtcNow;
        const decimal HourlyRate = 20m;
        var stepRows = runs.Select(r =>
        {
            var order = orders.FirstOrDefault(o => o.Id == r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId);
            var modelName = order != null && models.TryGetValue(order.MachineModelId, out var mn) ? mn : "—";
            var variantName = order != null && variants.TryGetValue(order.MachineVariantId, out var vn) ? vn : "—";
            var durationSeconds = r.FinishedAt.HasValue
                ? (r.DurationSeconds ?? (int)(r.FinishedAt.Value - r.StartedAt).TotalSeconds)
                : (int)(utcNow - r.StartedAt).TotalSeconds;
            return new TimeByUserRow
            {
                StepRunId = r.Id,
                StepExecId = r.StepExecId,
                UserId = r.UserId ?? "",
                UserDisplayName = UserDisplayNames.GetValueOrDefault(r.UserId ?? "", r.UserId ?? ""),
                MachineModel = modelName,
                MachineVariant = variantName,
                OrderRef = order?.ExternalRef ?? "",
                SerialNumber = order?.SerialNumber ?? "",
                StepTitle = r.StepExec?.Title ?? "—",
                StartedAt = r.StartedAt,
                FinishedAt = r.FinishedAt,
                DurationSeconds = durationSeconds,
                CostEuros = Math.Round((decimal)durationSeconds / 3600m * HourlyRate, 2),
                IsInProgress = !r.FinishedAt.HasValue,
                IsInternalTask = false,
                TaskSessionId = null
            };
        }).ToList();

        // Include time spent on internal tasks (Tasks module). Only when not filtering by order.
        var taskRows = new List<TimeByUserRow>();
        if (!OrderIdFilter.HasValue)
        {
            var fromUtc = FromDate.HasValue ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc) : DateTime.UtcNow.Date;
            var toUtc = ToDate.HasValue ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc) : fromUtc.AddDays(1);
            var taskSessionsQuery = _context.TaskSessions
                .AsNoTracking()
                .Include(s => s.Task)
                .Include(s => s.Pauses)
                .Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc < toUtc);
            if (!string.IsNullOrWhiteSpace(UserIdFilter))
                taskSessionsQuery = taskSessionsQuery.Where(s => s.UserId == UserIdFilter);
            if (string.Equals(StatusFilter, "InProgress", StringComparison.OrdinalIgnoreCase))
                taskSessionsQuery = taskSessionsQuery.Where(s => s.EndedAtUtc == null);
            else if (string.Equals(StatusFilter, "Completed", StringComparison.OrdinalIgnoreCase))
                taskSessionsQuery = taskSessionsQuery.Where(s => s.EndedAtUtc != null);

            var taskSessions = await taskSessionsQuery.OrderBy(s => s.StartedAtUtc).ToListAsync();
            foreach (var s in taskSessions)
            {
                var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
                var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : utcNow);
                var span = effectiveEnd - s.StartedAtUtc;
                var pauseTotal = TimeSpan.Zero;
                foreach (var p in s.Pauses.Where(p => p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd))
                    pauseTotal += p.EndUtc!.Value - p.StartUtc;
                var durationSeconds = (int)(span - pauseTotal).TotalSeconds;
                var displayName = UserDisplayNames.GetValueOrDefault(s.UserId, s.UserId);
                if (string.IsNullOrEmpty(displayName))
                {
                    var u = await _userManager.FindByIdAsync(s.UserId);
                    displayName = u?.Email ?? u?.UserName ?? s.UserId;
                    var at = displayName.IndexOf('@');
                    if (at > 0) displayName = displayName[..at];
                    UserDisplayNames[s.UserId] = displayName;
                }
                taskRows.Add(new TimeByUserRow
                {
                    StepRunId = 0,
                    StepExecId = 0,
                    UserId = s.UserId,
                    UserDisplayName = displayName,
                    MachineModel = "—",
                    MachineVariant = "—",
                    OrderRef = "—",
                    SerialNumber = "",
                    StepTitle = s.Task?.Title ?? "Internal task",
                    StartedAt = s.StartedAtUtc,
                    FinishedAt = s.EndedAtUtc,
                    DurationSeconds = durationSeconds,
                    CostEuros = Math.Round((decimal)durationSeconds / 3600m * HourlyRate, 2),
                    IsInProgress = s.EndedAtUtc == null,
                    IsInternalTask = true,
                    TaskSessionId = s.Id
                });
            }
        }

        Rows = stepRows.Concat(taskRows).OrderBy(r => r.StartedAt).ToList();

        // External API seconds (for KPI merge / clock-out style totals only). Detail rows stay MTI-only
        // so Machine and Task always come from the local database (step runs + internal task sessions).
        var fromUtcRange = FromDate.HasValue ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc) : DateTime.UtcNow.Date;
        var toUtcRange = ToDate.HasValue ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc) : fromUtcRange.AddDays(1);
        var externalSecondsByUser = await LoadExternalSecondsByUserAsync(fromUtcRange, toUtcRange, UserIdFilter);

        foreach (var uid in externalSecondsByUser.Keys)
        {
            if (string.IsNullOrEmpty(uid) || UserDisplayNames.ContainsKey(uid)) continue;
            var u = await _userManager.FindByIdAsync(uid);
            var display = u?.Email ?? u?.UserName ?? uid;
            var at = display.IndexOf('@');
            if (at > 0) display = display[..at];
            UserDisplayNames[uid] = display;
        }

        // Tiempo total por máquina (terminadas) agrupado por usuario (solo pasos de orden con hora de fin)
        MachineCompletionRows = stepRows
            .Where(r => r.FinishedAt.HasValue)
            .GroupBy(r => new { r.MachineModel, r.UserId })
            .Select(g => new MachineCompletionRow
            {
                MachineModel = g.Key.MachineModel,
                UserId = g.Key.UserId,
                UserDisplayName = g.First().UserDisplayName,
                TotalSeconds = g.Sum(x => x.DurationSeconds ?? 0)
            })
            .OrderBy(r => r.MachineModel)
            .ThenByDescending(r => r.TotalSeconds)
            .ThenBy(r => r.UserDisplayName)
            .ToList();

        // Comparación por máquina: listar usuarios y sus tiempos totales (solo terminadas) para la misma máquina
        MachineUserCompareRows = MachineCompletionRows
            .GroupBy(r => r.MachineModel)
            .Select(g => new MachineUserCompareRow
            {
                MachineModel = g.Key,
                Users = g
                    .Select(u => new MachineUserTotal
                    {
                        UserId = u.UserId,
                        UserDisplayName = u.UserDisplayName,
                        TotalSeconds = u.TotalSeconds
                        ,
                        CostEuros = Math.Round((decimal)u.TotalSeconds / 3600m * HourlyRate, 2)
                    })
                    .OrderByDescending(u => u.TotalSeconds)
                    .ThenBy(u => u.UserDisplayName)
                    .ToList()
            })
            .Where(r => r.Users.Count > 1)
            .OrderBy(r => r.MachineModel)
            .ToList();


        // KPI by technician: merge MTI + API (take max to avoid double-counting same work tracked in both systems).
        var localSecondsByUser = Rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationSeconds ?? 0));
        var allKpiUsers = localSecondsByUser.Keys.Union(externalSecondsByUser.Keys).Distinct().ToList();
        UserHoursKpis = allKpiUsers
            .Select(uid =>
            {
                var local = localSecondsByUser.TryGetValue(uid, out var l) ? l : 0;
                var external = externalSecondsByUser.TryGetValue(uid, out var e) ? e : 0;
                var merged = Math.Max(local, external);
                var display = Rows.FirstOrDefault(r => r.UserId == uid)?.UserDisplayName
                              ?? UserDisplayNames.GetValueOrDefault(uid, uid);
                return new UserHoursKpi { UserId = uid, UserDisplayName = display, TotalSeconds = merged };
            })
            .Where(x => x.TotalSeconds > 0) // hide users with only 0%
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        MachineUserHoursKpis = Rows
            .GroupBy(r => new { r.MachineModel, r.UserId })
            .Select(g => new MachineUserHoursKpi
            {
                MachineModel = g.Key.MachineModel,
                UserId = g.Key.UserId,
                UserDisplayName = g.First().UserDisplayName,
                TotalSeconds = g.Sum(x => x.DurationSeconds ?? 0)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ThenBy(x => x.MachineModel)
            .ThenBy(x => x.UserDisplayName)
            .ToList();

        TotalDurationSeconds = Rows.Sum(r => r.DurationSeconds ?? 0);

        var totalItems = Rows.Count;
        Rows = Rows.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };

        var allUserIds = await _context.StepRuns
            .Where(r => !string.IsNullOrEmpty(r.UserId))
            .Select(r => r.UserId!)
            .Distinct()
            .ToListAsync();
        var fromForTaskUsers = FromDate.HasValue ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc) : DateTime.UtcNow.Date;
        var toForTaskUsers = ToDate.HasValue ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc) : fromForTaskUsers.AddDays(1);
        var taskUserIds = await _context.TaskSessions
            .Where(s => s.StartedAtUtc >= fromForTaskUsers && s.StartedAtUtc < toForTaskUsers)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync();
        allUserIds = allUserIds.Union(taskUserIds).Distinct().ToList();

        var userList = new List<(string Id, string Name)> { ("", "— All users —") };
        foreach (var uid in allUserIds.OrderBy(x => x))
        {
            var u = await _userManager.FindByIdAsync(uid);
            var display = u?.Email ?? u?.UserName ?? uid;
            var at = display.IndexOf('@');
            if (at > 0) display = display[..at];
            userList.Add((uid, display));
        }
        UserSelectList = new SelectList(userList.Select(x => new { Value = x.Id, Text = x.Name }), "Value", "Text", UserIdFilter);

        return Page();
    }

    public async Task<IActionResult> OnPostEditRunAsync(int id, string startedLocal, string? finishedLocal)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var run = await _context.StepRuns.FindAsync(id);
        if (run == null)
            return NotFound();

        if (!DateTime.TryParse(startedLocal, out var startLocal))
            return BadRequest("Invalid start time.");

        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, TimeZoneInfo.Local);
        DateTime? finishedUtc = null;
        if (!string.IsNullOrWhiteSpace(finishedLocal) && DateTime.TryParse(finishedLocal, out var endLocal))
            finishedUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, TimeZoneInfo.Local);

        run.StartedAt = startUtc;
        run.FinishedAt = finishedUtc;
        if (finishedUtc.HasValue && finishedUtc.Value > startUtc)
            run.DurationSeconds = (int)(finishedUtc.Value - startUtc).TotalSeconds;
        else
            run.DurationSeconds = null;

        await _context.SaveChangesAsync();
        return RedirectToPage(new
        {
            UserIdFilter,
            OrderIdFilter,
            StatusFilter,
            FromDate = FromDate?.ToString("yyyy-MM-dd"),
            ToDate = ToDate?.ToString("yyyy-MM-dd"),
            PageNumber,
            PageSize
        });
    }

    public async Task<IActionResult> OnPostDeleteRunAsync(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var run = await _context.StepRuns.FindAsync(id);
        if (run == null)
            return NotFound();

        _context.StepRuns.Remove(run);
        await _context.SaveChangesAsync();

        return RedirectToPage(new
        {
            UserIdFilter,
            OrderIdFilter,
            StatusFilter,
            FromDate = FromDate?.ToString("yyyy-MM-dd"),
            ToDate = ToDate?.ToString("yyyy-MM-dd"),
            PageNumber,
            PageSize
        });
    }

    public async Task<IActionResult> OnPostAddRunAsync(string userId, int stepExecId, string startedLocal, string? finishedLocal)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(userId) || stepExecId <= 0)
            return BadRequest("User and step are required.");

        if (!DateTime.TryParse(startedLocal, out var startLocal))
            return BadRequest("Invalid start time.");

        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, TimeZoneInfo.Local);
        DateTime? finishedUtc = null;
        if (!string.IsNullOrWhiteSpace(finishedLocal) && DateTime.TryParse(finishedLocal, out var endLocal))
            finishedUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, TimeZoneInfo.Local);

        int? durationSeconds = null;
        if (finishedUtc.HasValue && finishedUtc.Value > startUtc)
            durationSeconds = (int)(finishedUtc.Value - startUtc).TotalSeconds;

        var run = new StepRun
        {
            StepExecId = stepExecId,
            UserId = userId,
            StartedAt = startUtc,
            FinishedAt = finishedUtc,
            DurationSeconds = durationSeconds
        };

        _context.StepRuns.Add(run);
        await _context.SaveChangesAsync();

        return RedirectToPage(new
        {
            UserIdFilter,
            OrderIdFilter,
            StatusFilter,
            FromDate = FromDate?.ToString("yyyy-MM-dd"),
            ToDate = ToDate?.ToString("yyyy-MM-dd"),
            PageNumber,
            PageSize
        });
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            // Reuse the same filtering logic as OnGetAsync, but do not apply pagination.
            if (!FromDate.HasValue)
            {
                FromDate = DateTime.UtcNow.Date;
            }

            var query = _context.StepRuns
                .Include(r => r.StepExec)
                .Include(r => r.StepExec!.PhaseExec)
                    .ThenInclude(p => p!.BuildExecution)
                        .ThenInclude(e => e!.BuildOrder)
                .AsNoTracking()
                .Where(r => r.StepExec != null
                    && r.StepExec.PhaseExec != null
                    && r.StepExec.PhaseExec.BuildExecution != null
                    && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null);

            if (!string.IsNullOrWhiteSpace(UserIdFilter))
                query = query.Where(r => r.UserId == UserIdFilter);

            if (OrderIdFilter.HasValue)
                query = query.Where(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId == OrderIdFilter.Value);

            if (string.Equals(StatusFilter, "InProgress", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.FinishedAt == null);
            else if (string.Equals(StatusFilter, "Completed", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.FinishedAt != null);

            if (FromDate.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(r => r.StartedAt >= fromUtc);
            }

            if (ToDate.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(r => r.StartedAt < toUtc);
            }

            var runs = await query
                .OrderBy(r => r.StartedAt)
                .ToListAsync();

            var orderIds = runs
                .Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId)
                .Distinct()
                .ToList();

            var orders = await _context.BuildOrders
                .AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o);

            var modelIds = orders.Values.Select(o => o.MachineModelId).Distinct().ToList();
            var variantIds = orders.Values.Select(o => o.MachineVariantId).Distinct().ToList();

            var models = await _context.MachineModels
                .AsNoTracking()
                .Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name);

            var variants = await _context.MachineVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => string.IsNullOrEmpty(v.Code) ? v.Name : $"{v.Name} ({v.Code})");

            // Build user display map
            var userIds = runs.Select(r => r.UserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var userDisplay = new Dictionary<string, string>();
            foreach (var uid in userIds)
            {
                var user = await _userManager.FindByIdAsync(uid);
                var display = user?.Email ?? user?.UserName ?? uid;
                var at = display.IndexOf('@');
                if (at > 0) display = display[..at];
                userDisplay[uid] = display;
            }

            var utcNow = DateTime.UtcNow;

            static string Csv(string? v)
            {
                v ??= "";
                if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
                    return "\"" + v.Replace("\"", "\"\"") + "\"";
                return v;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Type,User,Machine,Variant,Order,Serial,Task,Started (UTC),Finished (UTC),DurationSeconds,InProgress");

            foreach (var r in runs)
            {
                var orderId = r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId;
                orders.TryGetValue(orderId, out var order);

                var modelName = order != null && models.TryGetValue(order.MachineModelId, out var mn) ? mn : "—";
                var variantName = order != null && variants.TryGetValue(order.MachineVariantId, out var vn) ? vn : "—";
                var durationSeconds = r.FinishedAt.HasValue ? (r.DurationSeconds ?? 0) : (int)(utcNow - r.StartedAt).TotalSeconds;

                sb.AppendLine(string.Join(",", new[]
                {
                    "Order step",
                    Csv(userDisplay.GetValueOrDefault(r.UserId ?? "", r.UserId ?? "")),
                    Csv(modelName),
                    Csv(variantName),
                    Csv(order?.ExternalRef ?? ""),
                    Csv(order?.SerialNumber ?? ""),
                    Csv(r.StepExec?.Title ?? "—"),
                    Csv(r.StartedAt.ToString("O")),
                    Csv(r.FinishedAt?.ToString("O") ?? ""),
                    durationSeconds.ToString(),
                    (!r.FinishedAt.HasValue).ToString()
                }));
            }

            if (!OrderIdFilter.HasValue)
            {
                var fromUtc = FromDate.HasValue ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc) : DateTime.UtcNow.Date;
                var toUtc = ToDate.HasValue ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc) : fromUtc.AddDays(1);
                var taskSessionsExport = await _context.TaskSessions
                    .AsNoTracking()
                    .Include(s => s.Task)
                    .Include(s => s.Pauses)
                    .Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc < toUtc)
                    .Where(s => string.IsNullOrWhiteSpace(UserIdFilter) || s.UserId == UserIdFilter)
                    .Where(s => StatusFilter != "InProgress" || s.EndedAtUtc == null)
                    .Where(s => StatusFilter != "Completed" || s.EndedAtUtc != null)
                    .OrderBy(s => s.StartedAtUtc)
                    .ToListAsync();
                foreach (var s in taskSessionsExport)
                {
                    var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
                    var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : utcNow);
                    var span = effectiveEnd - s.StartedAtUtc;
                    var pauseTotal = s.Pauses.Where(p => p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd).Sum(p => (p.EndUtc!.Value - p.StartUtc).TotalSeconds);
                    var durationSeconds = (int)(span.TotalSeconds - pauseTotal);
                    sb.AppendLine(string.Join(",", new[]
                    {
                        "Internal task",
                        Csv(userDisplay.GetValueOrDefault(s.UserId, s.UserId)),
                        "—",
                        "—",
                        "—",
                        "—",
                        Csv(s.Task?.Title ?? "Internal task"),
                        Csv(s.StartedAtUtc.ToString("O")),
                        Csv(s.EndedAtUtc?.ToString("O") ?? ""),
                        durationSeconds.ToString(),
                        (s.EndedAtUtc == null).ToString()
                    }));
                }
            }

            var fileName = $"TimeByUser_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error generating CSV: {ex.Message}");
        }
    }

    public static string FormatDuration(int? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0) return "—";
        var t = TimeSpan.FromSeconds(seconds.Value);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    /// <summary>Format duration for summary (e.g. "2h 30m" or "45m 0s").</summary>
    public static string FormatDurationSummary(int totalSeconds)
    {
        if (totalSeconds <= 0) return "0h 0m";
        var t = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)t.TotalHours}h {t.Minutes}m";
    }

    private HttpClient CreateExternalApiHttpClient()
    {
        var dangerousCert = _configuration.GetValue<bool>("ExternalApi:DangerousAcceptAnyServerCertificate");
        var hostHeader = _configuration["ExternalApi:HostHeader"];
        var skipSsl = dangerousCert || !string.IsNullOrEmpty(hostHeader);
        if (!skipSsl) return new HttpClient();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler);
    }

    private static IEnumerable<string> GetExternalUserCandidates(string? email, string? userName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Yield(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) yield break;
            v = v.Trim();
            if (seen.Add(v)) yield return v;
            var at = v.IndexOf('@');
            if (at > 0)
            {
                var prefix = v[..at];
                if (seen.Add(prefix)) yield return prefix;
            }
        }
        return Yield(email).Concat(Yield(userName));
    }

    private async Task<Dictionary<string, int>> LoadExternalSecondsByUserAsync(DateTime fromUtc, DateTime toUtc, string? userIdFilter)
    {
        var result = new Dictionary<string, int>();
        var usersQuery = _userManager.Users.AsNoTracking().Select(u => new { u.Id, u.Email, u.UserName });
        if (!string.IsNullOrWhiteSpace(userIdFilter))
            usersQuery = usersQuery.Where(u => u.Id == userIdFilter);
        var users = await usersQuery.ToListAsync();

        for (var day = fromUtc.Date; day < toUtc.Date; day = day.AddDays(1))
        {
            foreach (var user in users)
            {
                var minutes = await FetchExternalTaskMinutesForDayAsync(user.Email, user.UserName, day);
                if (minutes <= 0) continue;
                var seconds = minutes * 60;
                result[user.Id] = result.TryGetValue(user.Id, out var prev) ? prev + seconds : seconds;
            }
        }

        return result;
    }

    private async Task<int> FetchExternalTaskMinutesForDayAsync(string? email, string? userName, DateTime dateUtc)
    {
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return 0;

        var apiKey = _configuration["ExternalApi:ApiKey"];
        var hostHeader = _configuration["ExternalApi:HostHeader"];
        using var client = CreateExternalApiHttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var candidate in GetExternalUserCandidates(email, userName))
        {
            var requestUrl = $"{baseUrl}/api/TecnicosApi/Timeline?user={Uri.EscapeDataString(candidate)}&date={dateUtc:yyyy-MM-dd}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrEmpty(hostHeader))
                    req.Headers.Host = hostHeader;

                using var response = await client.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var minutes = ParseTimelineTaskMinutes(json);
                if (minutes > 0) return minutes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timeline API query failed for user candidate {Candidate}", candidate);
            }
        }

        return 0;
    }

    private static int ParseTimelineTaskMinutes(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return 0;

            if (root.TryGetProperty("taskMinutes", out var tm) && tm.ValueKind == System.Text.Json.JsonValueKind.Number && tm.TryGetInt32(out var direct))
                return Math.Max(0, direct);

            if (root.TryGetProperty("timeline", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var ranges = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (item.TryGetProperty("start", out var s) &&
                        item.TryGetProperty("end", out var e) &&
                        s.ValueKind == System.Text.Json.JsonValueKind.String &&
                        e.ValueKind == System.Text.Json.JsonValueKind.String &&
                        DateTimeOffset.TryParse(s.GetString(), out var start) &&
                        DateTimeOffset.TryParse(e.GetString(), out var end) &&
                        end > start)
                    {
                        ranges.Add((start, end));
                    }
                }
                return CalculateMergedTimelineMinutes(ranges);
            }
        }
        catch (System.Text.Json.JsonException) { }
        return 0;
    }

    private static int CalculateMergedTimelineMinutes(List<(DateTimeOffset Start, DateTimeOffset End)> ranges)
    {
        if (ranges.Count == 0) return 0;
        var ordered = ranges
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();

        var mergedMinutes = 0.0;
        var currentStart = ordered[0].Start;
        var currentEnd = ordered[0].End;

        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (next.Start <= currentEnd)
            {
                if (next.End > currentEnd) currentEnd = next.End;
                continue;
            }

            mergedMinutes += (currentEnd - currentStart).TotalMinutes;
            currentStart = next.Start;
            currentEnd = next.End;
        }

        mergedMinutes += (currentEnd - currentStart).TotalMinutes;
        return Math.Max(0, (int)Math.Round(mergedMinutes));
    }
}

public class UserHoursKpi
{
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public int TotalSeconds { get; set; }
}

public class MachineUserHoursKpi
{
    public string MachineModel { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public int TotalSeconds { get; set; }
}

public class MachineCompletionRow
{
    public string MachineModel { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public int TotalSeconds { get; set; }
}

public class MachineUserCompareRow
{
    public string MachineModel { get; set; } = "";
    public List<MachineUserTotal> Users { get; set; } = new();
}

public class MachineUserTotal
{
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public int TotalSeconds { get; set; }
    public decimal CostEuros { get; set; }
}

public class CompareRow
{
    public string MachineModel { get; set; } = "";
    public string StepTitle { get; set; } = "";
    public List<UserTaskStat> UserStats { get; set; } = new();
}

public class UserTaskStat
{
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public int Runs { get; set; }
    public int TotalSeconds { get; set; }
    public int AverageSeconds => Runs > 0 ? TotalSeconds / Runs : 0;
}

public class StepOption
{
    public int StepExecId { get; set; }
    public string Display { get; set; } = "";
}

public class TimeByUserRow
{
    public int StepRunId { get; set; }
    public int StepExecId { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string MachineModel { get; set; } = "";
    public string MachineVariant { get; set; } = "";
    public string OrderRef { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string StepTitle { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public bool IsInProgress { get; set; }
    /// <summary>True when this row is from an internal task (Tasks module), not an order step.</summary>
    public bool IsInternalTask { get; set; }
    public int? TaskSessionId { get; set; }
    public decimal CostEuros { get; set; }
}
