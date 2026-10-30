using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [BindProperty]
    public BuildOrder BuildOrder { get; set; } = default!;

    public SelectList MachineModels { get; set; } = default!;
    public SelectList MachineVariants { get; set; } = default!;
    public SelectList OwnerUsers { get; set; } = default!;
    public SelectList PriorityList { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null || _context.BuildOrders == null)
            return RedirectToPage("./Index");

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        if (order.Status != "Pending")
        {
            TempData["ToastrError"] = "Only orders with status Pending can be edited.";
            return RedirectToPage("./Index");
        }

        BuildOrder = order;
        await PopulateDropdownsAsync();
        return Page();
    }

    private async Task PopulateDropdownsAsync()
    {
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
        if (BuildOrder == null || _context.BuildOrders == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        var existing = await _context.BuildOrders.FindAsync(BuildOrder.Id);
        if (existing == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        if (existing.Status != "Pending")
        {
            TempData["ToastrError"] = "Only orders with status Pending can be edited.";
            return RedirectToPage("./Index");
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
            ModelState.AddModelError("BuildOrder.DueDate", "Due date is required for non-cancelled orders.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return Page();
        }

        var modelChanged = existing.MachineModelId != BuildOrder.MachineModelId;
        var variantChanged = existing.MachineVariantId != BuildOrder.MachineVariantId;
        existing.ExternalRef = BuildOrder.ExternalRef;
        existing.SerialNumber = BuildOrder.SerialNumber;
        existing.MachineModelId = BuildOrder.MachineModelId;
        existing.MachineVariantId = BuildOrder.MachineVariantId;
        existing.CustomerName = BuildOrder.CustomerName;
        existing.ProjectRef = BuildOrder.ProjectRef;
        existing.PlannedWeek = BuildOrder.PlannedWeek;
        existing.DueDate = BuildOrder.DueDate;
        existing.Priority = BuildOrder.Priority;
        existing.OwnerUserId = BuildOrder.OwnerUserId;
        await _context.SaveChangesAsync();

        // If machine model or variant changed, regenerate pick list from model+variant components
        if (modelChanged || variantChanged)
        {
            var existingLines = await _context.OrderPickLines.Where(l => l.BuildOrderId == existing.Id).ToListAsync();
            _context.OrderPickLines.RemoveRange(existingLines);

            var machineModel = await _context.MachineModels
                .Include(m => m.Components)
                    .ThenInclude(c => c.Alternatives)
                .FirstOrDefaultAsync(m => m.Id == existing.MachineModelId);
            var machineVariant = await _context.MachineVariants
                .Include(v => v.Components)
                    .ThenInclude(c => c.Alternatives)
                .FirstOrDefaultAsync(v => v.Id == existing.MachineVariantId);

            async Task AddComponentAsync(int? itemId, int? materialId, int qty, IEnumerable<int> altItemIds)
            {
                var itemIdsToAdd = new List<int>();
                if (itemId.HasValue)
                {
                    itemIdsToAdd.Add(itemId.Value);
                    itemIdsToAdd.AddRange(altItemIds);
                }
                else if (materialId.HasValue)
                {
                    var itemIdForMaterial = await _context.Items
                        .Where(i => i.MaterialId == materialId.Value)
                        .Select(i => (int?)i.Id)
                        .FirstOrDefaultAsync();
                    if (itemIdForMaterial.HasValue)
                        itemIdsToAdd.Add(itemIdForMaterial.Value);
                }
                foreach (var itemIdToAdd in itemIdsToAdd.Distinct())
                {
                    _context.OrderPickLines.Add(new OrderPickLine
                    {
                        BuildOrderId = existing.Id,
                        ItemId = itemIdToAdd,
                        QuantityRequired = qty,
                        QuantityPicked = 0
                    });
                }
            }

            foreach (var c in machineModel?.Components ?? new List<MachineModelComponent>())
                await AddComponentAsync(c.ItemId, c.MaterialId, c.Quantity, c.Alternatives.Select(a => a.ItemId));
            foreach (var c in machineVariant?.Components ?? new List<MachineVariantComponent>())
                await AddComponentAsync(c.ItemId, c.MaterialId, c.Quantity, c.Alternatives.Select(a => a.ItemId));

            await _context.SaveChangesAsync();
        }

        TempData["ToastrSuccess"] = "Order updated successfully.";
        return RedirectToPage("./Index");
    }
}
