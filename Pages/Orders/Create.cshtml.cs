using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.Orders;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBuildOrderNumberService _orderNumberService;

    public CreateModel(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IBuildOrderNumberService orderNumberService)
    {
        _context = context;
        _userManager = userManager;
        _orderNumberService = orderNumberService;
    }

    [BindProperty]
    public BuildOrder BuildOrder { get; set; } = default!;

    public string PreviewOrderNumber { get; set; } = "";
    public string PreviewSerialNumber { get; set; } = "";

    public SelectList MachineModels { get; set; } = default!;
    public SelectList MachineVariants { get; set; } = default!;
    public SelectList OwnerUsers { get; set; } = default!;
    public SelectList PriorityList { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync()
    {
        await PopulateDropdownsAsync();
        return Page();
    }

    private async Task PopulateDropdownsAsync()
    {
        var (externalRef, serialNumber) = await _orderNumberService.GenerateNextAsync();
        PreviewOrderNumber = externalRef;
        PreviewSerialNumber = serialNumber;

        if (BuildOrder == null)
            BuildOrder = new BuildOrder();

        if (string.IsNullOrWhiteSpace(BuildOrder.ExternalRef))
            BuildOrder.ExternalRef = externalRef;
        if (string.IsNullOrWhiteSpace(BuildOrder.SerialNumber))
            BuildOrder.SerialNumber = serialNumber;
        if (string.IsNullOrWhiteSpace(BuildOrder.PlannedWeek))
            BuildOrder.PlannedWeek = BuildOrderCreationHelper.CurrentPlannedWeek();
        if (!BuildOrder.DueDate.HasValue)
            BuildOrder.DueDate = DateTime.UtcNow.Date.AddDays(14);

        MachineModels = new SelectList(await _context.MachineModels.Where(m => m.Active).ToListAsync(), "Id", "Name");
        MachineVariants = new SelectList(new List<MachineVariant>(), "Id", "Name");
        var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        OwnerUsers = new SelectList(users, "Id", "UserName");
        PriorityList = new SelectList(new[]
        {
            new { Value = BuildOrderPriority.Low, Text = "Low" },
            new { Value = BuildOrderPriority.Normal, Text = "Normal" },
            new { Value = BuildOrderPriority.High, Text = "High" },
            new { Value = BuildOrderPriority.Urgent, Text = "Urgent" }
        }, "Value", "Text");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_context.BuildOrders == null || BuildOrder == null)
        {
            await PopulateDropdownsAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(BuildOrder.ExternalRef) || string.IsNullOrWhiteSpace(BuildOrder.SerialNumber))
        {
            var (externalRef, serialNumber) = await _orderNumberService.GenerateNextAsync();
            if (string.IsNullOrWhiteSpace(BuildOrder.ExternalRef))
                BuildOrder.ExternalRef = externalRef;
            if (string.IsNullOrWhiteSpace(BuildOrder.SerialNumber))
                BuildOrder.SerialNumber = serialNumber;
        }

        if (BuildOrder.MachineModelId <= 0)
            ModelState.AddModelError("BuildOrder.MachineModelId", "You must select a machine model.");
        if (BuildOrder.MachineVariantId <= 0)
            ModelState.AddModelError("BuildOrder.MachineVariantId", "You must select a machine variant.");
        if (string.IsNullOrWhiteSpace(BuildOrder.CustomerName))
            ModelState.AddModelError("BuildOrder.CustomerName", "Customer name is required.");
        if (string.IsNullOrWhiteSpace(BuildOrder.OwnerUserId))
            ModelState.AddModelError("BuildOrder.OwnerUserId", "Owner is required.");
        if (BuildOrder.Priority < BuildOrderPriority.Low || BuildOrder.Priority > BuildOrderPriority.Urgent)
            ModelState.AddModelError("BuildOrder.Priority", "Please select a valid priority.");
        if (string.IsNullOrWhiteSpace(BuildOrder.PlannedWeek))
            ModelState.AddModelError("BuildOrder.PlannedWeek", "Planned week is required.");
        if (!BuildOrder.DueDate.HasValue)
            ModelState.AddModelError("BuildOrder.DueDate", "Due date is required for new orders.");

        var duplicateExternalRef = await _context.BuildOrders
            .AnyAsync(o => o.ExternalRef == BuildOrder.ExternalRef.Trim());
        if (duplicateExternalRef)
            ModelState.AddModelError("BuildOrder.ExternalRef", "An order with this order number already exists.");

        if (!string.IsNullOrWhiteSpace(BuildOrder.SerialNumber))
        {
            var duplicateSerial = await _context.BuildOrders
                .AnyAsync(o => o.SerialNumber == BuildOrder.SerialNumber.Trim());
            if (duplicateSerial)
                ModelState.AddModelError("BuildOrder.SerialNumber", "An order with this serial number already exists.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return Page();
        }

        BuildOrder.ExternalRef = BuildOrder.ExternalRef.Trim();
        BuildOrder.SerialNumber = BuildOrder.SerialNumber.Trim();
        BuildOrder.Status = "Pending";
        BuildOrder.CreatedAt = DateTime.UtcNow;
        _context.BuildOrders.Add(BuildOrder);
        await _context.SaveChangesAsync();

        await BuildOrderCreationHelper.PopulatePickLinesAsync(_context, BuildOrder);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}
