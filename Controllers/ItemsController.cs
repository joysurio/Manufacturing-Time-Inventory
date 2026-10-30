using System.Text.RegularExpressions;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace ManufacturingTimeTracking.Controllers;

[Authorize(Policy = "CanUseInventory")]
public class ItemsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly InventoryService _inv;

    public ItemsController(ApplicationDbContext db, InventoryService inv)
    {
        _db = db;
        _inv = inv;
    }

    public async Task<IActionResult> Index(string? q = null, int pageNumber = 1, int pageSize = 10)
    {
        var query = _db.Items.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(i => i.Sku.Contains(q) || i.Name.Contains(q));
        var ordered = query.OrderBy(i => i.Sku);
        var totalItems = await ordered.CountAsync();
        var items = await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Query = q ?? "";
        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = totalItems };
        return View(items);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Item model)
    {
        if (id != model.Id) return NotFound();
        if (string.IsNullOrWhiteSpace(model.Sku) || string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError("", "SKU and Name are required.");
            return View(model);
        }
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();
        item.Sku = model.Sku;
        item.Name = model.Name;
        item.Family = model.Family ?? "";
        item.ModelOrType = model.ModelOrType ?? "";
        item.Unit = model.Unit ?? "ud";
        item.IsSerialized = model.IsSerialized;
        item.MaterialId = model.MaterialId;
        await _db.SaveChangesAsync();
        TempData["Msg"] = "Item updated.";
        return RedirectToAction(nameof(Details), new { id = item.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Check all foreign key references before deleting
        var usedInComponents = await _db.MachineModelComponents
            .AnyAsync(c => c.ItemId == id);
        if (usedInComponents)
        {
            var count = await _db.MachineModelComponents.CountAsync(c => c.ItemId == id);
            TempData["DeleteError"] = $"Cannot delete \"{item.Sku}\" because it is used in {count} machine model component(s). Remove it from all machine models first.";
            return RedirectToAction(nameof(Index));
        }

        var usedInAlternatives = await _db.MachineModelComponentAlternatives
            .AnyAsync(a => a.ItemId == id);
        if (usedInAlternatives)
        {
            var count = await _db.MachineModelComponentAlternatives.CountAsync(a => a.ItemId == id);
            TempData["DeleteError"] = $"Cannot delete \"{item.Sku}\" because it is used in {count} machine model component alternative(s). Remove those alternatives first.";
            return RedirectToAction(nameof(Index));
        }

        var usedInPickLines = await _db.OrderPickLines
            .AnyAsync(l => l.ItemId == id);
        if (usedInPickLines)
        {
            var count = await _db.OrderPickLines.CountAsync(l => l.ItemId == id);
            TempData["DeleteError"] = $"Cannot delete \"{item.Sku}\" because it is referenced in {count} order pick line(s). Complete or cancel those orders first.";
            return RedirectToAction(nameof(Index));
        }

        var hasRobotTasks = await _db.RobotTasks
            .AnyAsync(t => t.ItemId == id);
        if (hasRobotTasks)
        {
            var count = await _db.RobotTasks.CountAsync(t => t.ItemId == id);
            TempData["DeleteError"] = $"Cannot delete \"{item.Sku}\" because it is referenced in {count} robot task(s).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            _db.Items.Remove(item);
            await _db.SaveChangesAsync();
            TempData["Msg"] = "Item deleted.";
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException?.Message?.Contains("REFERENCE") == true)
        {
            TempData["DeleteError"] = $"Cannot delete \"{item.Sku}\" because it is still referenced elsewhere. Remove those references first.";
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> QuickCreate()
    {
        var materials = await _db.Materials.OrderBy(m => m.Name).ToListAsync();
        ViewBag.Materials = new SelectList(materials, "Id", "Name");
        return View(new Item());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate(string sku, string name, string family, string modelOrType, string unit, string tagCode, string tagType, int packQuantity, bool isSerialized, int? materialId)
    {
        if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("", "SKU and Name are required.");
            var materials = await _db.Materials.OrderBy(m => m.Name).ToListAsync();
            ViewBag.Materials = new SelectList(materials, "Id", "Name");
            return View(new Item { Sku = sku ?? "", Name = name ?? "", Family = family ?? "", ModelOrType = modelOrType ?? "", Unit = unit ?? "ud", MaterialId = materialId });
        }
        if (string.IsNullOrWhiteSpace(tagCode))
        {
            tagCode = $"IT-{Guid.NewGuid().ToString()[..8].ToUpper()}";
            tagType = "QR";
        }
        var item = await _inv.CreateOrGetItemMinimalAsync(sku.Trim(), name.Trim(), family?.Trim() ?? "", unit?.Trim() ?? "ud", materialId);
        item.ModelOrType = modelOrType?.Trim() ?? "";
        item.IsSerialized = isSerialized;
        await _db.SaveChangesAsync();
        await _inv.AttachTagAsync(item.Id, tagCode.Trim(), string.IsNullOrWhiteSpace(tagType) ? "RFID" : tagType.Trim().ToUpperInvariant(), packQuantity <= 0 ? 1 : packQuantity);
        TempData["Msg"] = "Item created. Print the label below.";
        return RedirectToAction(nameof(PrintLabel), new { itemId = item.Id });
    }

    public IActionResult CreateWithPhoto() => View(new Item());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWithPhoto(IFormFile? photo, string sku, string name, string tagCode, string tagType = "RFID", int packQuantity = 1, bool isSerialized = false, string ocrText = "", bool applyOcr = false, int? materialId = null)
    {
        if (string.IsNullOrWhiteSpace(sku) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(ocrText) && (photo == null || photo.Length == 0))
        {
            ModelState.AddModelError("", "SKU or Name is required.");
            return View(new Item { Sku = sku ?? "", Name = name ?? "" });
        }
        var imagePath = "";
        if (photo != null && photo.Length > 0)
        {
            var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "inventory");
            Directory.CreateDirectory(uploads);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}";
            var filePath = Path.Combine(uploads, fileName);
            await using var stream = System.IO.File.Create(filePath);
            await photo.CopyToAsync(stream);
            imagePath = "/uploads/inventory/" + fileName;
        }
        var textSource = ocrText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) && photo != null && !string.IsNullOrWhiteSpace(photo.FileName))
            textSource = Path.GetFileNameWithoutExtension(photo.FileName).Replace('_', ' ').Trim();
        if (applyOcr && !string.IsNullOrWhiteSpace(ocrText))
        {
            var firstLine = ocrText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                name = firstLine;
                sku = Regex.Replace(name.ToUpperInvariant(), "\\s+", "-");
                sku = Regex.Replace(sku, "[^A-Z0-9\\-]", "");
            }
        }
        else if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(textSource))
        {
            var firstLine = textSource.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine)) name = firstLine;
            if (string.IsNullOrWhiteSpace(sku) && !string.IsNullOrWhiteSpace(name))
            {
                sku = Regex.Replace(name.ToUpperInvariant(), "\\s+", "-");
                sku = Regex.Replace(sku, "[^A-Z0-9\\-]", "");
            }
        }
        var item = await _inv.CreateOrGetItemMinimalAsync((sku ?? "").Trim(), (name ?? "").Trim(), "", "ud", materialId);
        item.IsSerialized = isSerialized;
        if (!string.IsNullOrEmpty(imagePath)) item.ImagePath = imagePath;
        await _db.SaveChangesAsync();
        if (string.IsNullOrWhiteSpace(tagCode))
        {
            tagCode = $"IT-{Guid.NewGuid().ToString()[..8].ToUpper()}";
            tagType = "QR";
        }
        await _inv.AttachTagAsync(item.Id, tagCode.Trim(), string.IsNullOrWhiteSpace(tagType) ? "RFID" : tagType.Trim().ToUpperInvariant(), packQuantity <= 0 ? 1 : packQuantity);
        TempData["Msg"] = "Item created. Print the label below.";
        return RedirectToAction(nameof(PrintLabel), new { itemId = item.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var item = await _db.Items.Include(i => i.Tags).Include(i => i.Material).FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpGet]
    public async Task<IActionResult> PrintLabel(int itemId, int? tagId = null)
    {
        var item = await _db.Items.Include(i => i.Tags).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        var tag = tagId.HasValue ? item.Tags.FirstOrDefault(t => t.Id == tagId.Value) : item.Tags.FirstOrDefault();
        ViewBag.TagCode = tag?.Code ?? item.Sku;
        return View(item);
    }

    public IActionResult ItemQr(string code, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(code)) return NotFound();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return File(png, "image/png");
    }
}
