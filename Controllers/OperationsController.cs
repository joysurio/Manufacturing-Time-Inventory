using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ManufacturingTimeTracking.Controllers;

[Authorize(Policy = "CanUseInventory")]
public class OperationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly InventoryService _inv;

    public OperationsController(ApplicationDbContext db, InventoryService inv)
    {
        _db = db;
        _inv = inv;
    }

    public IActionResult Index() => View();

    public class PutawayVm
    {
        public string TagCode { get; set; } = "";
        public string LocationCode { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public string? Sku { get; set; }
        public string? ItemName { get; set; }
        public int? ItemId { get; set; }
        public bool IsSerialized { get; set; }
        public int DefaultPackQty { get; set; } = 1;
    }

    [HttpGet]
    public IActionResult Putaway() => View(new PutawayVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Putaway(PutawayVm vm)
    {
        vm.TagCode = (vm.TagCode ?? "").Trim();
        vm.LocationCode = (vm.LocationCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(vm.TagCode) || string.IsNullOrWhiteSpace(vm.LocationCode))
        {
            ModelState.AddModelError("", "You must scan the TAG and the LOCATION.");
            return View(vm);
        }
        var tag = await _db.Tags.Include(t => t.Item).SingleOrDefaultAsync(t => t.Code == vm.TagCode);
        if (tag?.Item == null)
        {
            ModelState.AddModelError("", "TAG not recognized.");
            return View(vm);
        }
        var loc = await _inv.GetLocationByCodeAsync(vm.LocationCode);
        if (loc == null)
        {
            ModelState.AddModelError("", "Location does not exist.");
            return View(vm);
        }
        var qty = Math.Max(1, vm.Quantity);
        var defaultQty = Math.Max(1, tag.PackQuantity);
        if (tag.Item.IsSerialized) qty = 1;
        else if (qty == 1 && defaultQty > 1) qty = defaultQty;
        var reception = await _inv.GetOrCreateSystemLocationAsync("RECEPTION", "Reception");
        await _inv.ApplyMovementAsync("TRANSFER", tag.Item.Id, qty, reception.Id, loc.Id, User.Identity?.Name ?? "", $"Putaway {tag.Code}");
        TempData["Msg"] = $"Saved: {qty} unit(s) of {tag.Item.Sku} at {loc.Code}";
        return RedirectToAction(nameof(Putaway));
    }

    public class PickVm
    {
        public string LocationCode { get; set; } = "";
        public string TagCode { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public string DestinationType { get; set; } = "PROD";
        public string DestinationRef { get; set; } = "";
        public string? Sku { get; set; }
        public string? ItemName { get; set; }
        public bool IsSerialized { get; set; }
        public int DefaultPackQty { get; set; } = 1;
    }

    [HttpGet]
    public IActionResult Pick() => View(new PickVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pick(PickVm vm)
    {
        vm.LocationCode = (vm.LocationCode ?? "").Trim().ToUpperInvariant();
        vm.TagCode = (vm.TagCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(vm.LocationCode) || string.IsNullOrWhiteSpace(vm.TagCode))
        {
            ModelState.AddModelError("", "You must scan the LOCATION and the TAG.");
            return View(vm);
        }
        var fromLoc = await _inv.GetLocationByCodeAsync(vm.LocationCode);
        if (fromLoc == null)
        {
            ModelState.AddModelError("", "Location does not exist.");
            return View(vm);
        }
        var tag = await _db.Tags.Include(t => t.Item).SingleOrDefaultAsync(t => t.Code == vm.TagCode);
        if (tag?.Item == null)
        {
            ModelState.AddModelError("", "TAG not recognized.");
            return View(vm);
        }
        var qty = Math.Max(1, vm.Quantity);
        var defaultQty = Math.Max(1, tag.PackQuantity);
        if (tag.Item.IsSerialized) qty = 1;
        else if (qty == 1 && defaultQty > 1) qty = defaultQty;
        var snap = await _db.StockSnapshots.SingleOrDefaultAsync(s => s.ItemId == tag.Item.Id && s.LocationId == fromLoc.Id);
        var available = snap?.Quantity ?? 0;
        if (available < qty)
        {
            ModelState.AddModelError("", $"Insufficient stock at {fromLoc.Code}. Available={available}, requested={qty}.");
            return View(vm);
        }
        var destCode = ResolveDestinationCode(vm.DestinationType, vm.DestinationRef);
        if (string.IsNullOrWhiteSpace(destCode))
        {
            ModelState.AddModelError("", "Invalid destination.");
            return View(vm);
        }
        var toLoc = await _inv.GetOrCreateGenericLocationAsync(destCode, "Output");
        await _inv.ApplyMovementAsync("TRANSFER", tag.Item.Id, qty, fromLoc.Id, toLoc.Id, User.Identity?.Name ?? "", $"Pick {tag.Code}");
        TempData["Msg"] = $"Picked: {qty} unit(s) of {tag.Item.Sku} from {fromLoc.Code} → {toLoc.Code}";
        return RedirectToAction(nameof(Pick));
    }

    private static string ResolveDestinationCode(string destinationType, string destinationRef)
    {
        destinationType = (destinationType ?? "").Trim().ToUpperInvariant();
        destinationRef = (destinationRef ?? "").Trim().ToUpperInvariant();
        return destinationType switch
        {
            "PROD" => "DEST:PRODUCCION",
            "TALLER" => "DEST:TALLER",
            "WS" => string.IsNullOrWhiteSpace(destinationRef) ? "" : $"DEST:WS:{destinationRef}",
            "ORDER" => string.IsNullOrWhiteSpace(destinationRef) ? "" : $"DEST:PEDIDO:{destinationRef}",
            "CUSTOM" => string.IsNullOrWhiteSpace(destinationRef) ? "" : destinationRef,
            _ => ""
        };
    }
}
