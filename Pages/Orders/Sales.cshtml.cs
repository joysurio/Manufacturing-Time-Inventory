using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Policy = "CanUseTimeManagement")]
public class SalesModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBuildOrderNumberService _orderNumberService;

    public SalesModel(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IBuildOrderNumberService orderNumberService)
    {
        _context = context;
        _userManager = userManager;
        _orderNumberService = orderNumberService;
    }

    public List<SalesOrderRow> Orders { get; set; } = new();
    public string PreviewOrderNumber { get; set; } = "";
    public string PreviewSerialNumber { get; set; } = "";
    public SelectList MachineModels { get; set; } = default!;
    public SelectList PriorityList { get; set; } = default!;
    public bool CanCreateOrders { get; set; }

    [BindProperty]
    public SalesOrderInput Input { get; set; } = new();

    public class SalesOrderInput
    {
        public string CustomerName { get; set; } = "";
        public int MachineModelId { get; set; }
        public int MachineVariantId { get; set; }
        public DateTime? DueDate { get; set; }
        public string PlannedWeek { get; set; } = "";
        public int Priority { get; set; } = BuildOrderPriority.Normal;
        public string? ProjectRef { get; set; }
    }

    public class SalesOrderRow
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string VariantName { get; set; } = "";
        public string Status { get; set; } = "";
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsBlocked { get; set; }
        public bool HasInProgressExecution { get; set; }
    }

    public async Task OnGetAsync()
    {
        CanCreateOrders = User.IsInRole("Admin") || User.IsInRole("Supervisor");
        await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        CanCreateOrders = User.IsInRole("Admin") || User.IsInRole("Supervisor");
        if (!CanCreateOrders)
            return Forbid();

        if (Input.MachineModelId <= 0)
            ModelState.AddModelError("Input.MachineModelId", "You must select a machine model.");
        if (Input.MachineVariantId <= 0)
            ModelState.AddModelError("Input.MachineVariantId", "You must select a machine variant.");
        if (string.IsNullOrWhiteSpace(Input.CustomerName))
            ModelState.AddModelError("Input.CustomerName", "Customer name is required.");
        if (Input.Priority < BuildOrderPriority.Low || Input.Priority > BuildOrderPriority.Urgent)
            ModelState.AddModelError("Input.Priority", "Please select a valid priority.");

        var plannedWeek = string.IsNullOrWhiteSpace(Input.PlannedWeek)
            ? BuildOrderCreationHelper.CurrentPlannedWeek()
            : Input.PlannedWeek.Trim();
        var dueDate = Input.DueDate ?? DateTime.UtcNow.Date.AddDays(14);

        if (!ModelState.IsValid)
        {
            await LoadPageAsync();
            return Page();
        }

        var (externalRef, serialNumber) = await _orderNumberService.GenerateNextAsync();
        var ownerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (string.IsNullOrEmpty(ownerUserId))
        {
            ModelState.AddModelError(string.Empty, "Could not determine the current user.");
            await LoadPageAsync();
            return Page();
        }

        var order = new BuildOrder
        {
            ExternalRef = externalRef,
            SerialNumber = serialNumber,
            MachineModelId = Input.MachineModelId,
            MachineVariantId = Input.MachineVariantId,
            CustomerName = Input.CustomerName.Trim(),
            ProjectRef = string.IsNullOrWhiteSpace(Input.ProjectRef) ? null : Input.ProjectRef.Trim(),
            PlannedWeek = plannedWeek,
            DueDate = dueDate,
            Priority = Input.Priority,
            OwnerUserId = ownerUserId,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.BuildOrders.Add(order);
        await _context.SaveChangesAsync();
        await BuildOrderCreationHelper.PopulatePickLinesAsync(_context, order);
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = $"Sales order {order.ExternalRef} created.";
        return RedirectToPage();
    }

    private async Task LoadPageAsync()
    {
        var (externalRef, serialNumber) = await _orderNumberService.GenerateNextAsync();
        PreviewOrderNumber = externalRef;
        PreviewSerialNumber = serialNumber;

        if (string.IsNullOrWhiteSpace(Input.PlannedWeek))
            Input.PlannedWeek = BuildOrderCreationHelper.CurrentPlannedWeek();
        if (!Input.DueDate.HasValue)
            Input.DueDate = DateTime.UtcNow.Date.AddDays(14);

        MachineModels = new SelectList(
            await _context.MachineModels.Where(m => m.Active).OrderBy(m => m.Name).ToListAsync(),
            "Id",
            "Name",
            Input.MachineModelId);

        PriorityList = new SelectList(new[]
        {
            new { Value = BuildOrderPriority.Low, Text = "Low" },
            new { Value = BuildOrderPriority.Normal, Text = "Normal" },
            new { Value = BuildOrderPriority.High, Text = "High" },
            new { Value = BuildOrderPriority.Urgent, Text = "Urgent" }
        }, "Value", "Text", Input.Priority);

        var query = _context.BuildOrders
            .AsNoTracking()
            .Include(o => o.Executions)
            .Where(o => !o.IsBlocked && o.Status != "Completed" && o.Status != "Cancelled")
            .OrderByDescending(o => o.Priority)
            .ThenByDescending(o => o.CreatedAt);

        var orders = await query.ToListAsync();
        var modelIds = orders.Select(o => o.MachineModelId).Distinct().ToList();
        var variantIds = orders.Select(o => o.MachineVariantId).Distinct().ToList();

        var modelNames = modelIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.MachineModels.AsNoTracking()
                .Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name);
        var variantNames = variantIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.MachineVariants.AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name);

        Orders = orders.Select(o => new SalesOrderRow
        {
            Id = o.Id,
            OrderNumber = o.ExternalRef,
            SerialNumber = o.SerialNumber,
            CustomerName = o.CustomerName,
            ModelName = modelNames.TryGetValue(o.MachineModelId, out var mn) ? mn : $"Model {o.MachineModelId}",
            VariantName = variantNames.TryGetValue(o.MachineVariantId, out var vn) ? vn : $"Variant {o.MachineVariantId}",
            Status = o.Status,
            Priority = o.Priority,
            CreatedAt = o.CreatedAt,
            IsBlocked = o.IsBlocked,
            HasInProgressExecution = o.Executions.Any(e => e.Status == "InProgress")
        }).ToList();
    }
}
