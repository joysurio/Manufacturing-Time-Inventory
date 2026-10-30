using System.Text.Json;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ManufacturingTimeTracking.Services;

public class InventoryService
{
    private readonly ApplicationDbContext _db;

    public InventoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Item?> FindItemByTagAsync(string code)
    {
        return await _db.Tags.Include(t => t.Item).Where(t => t.Code == code).Select(t => t.Item).FirstOrDefaultAsync();
    }

    public async Task<Item> CreateOrGetItemMinimalAsync(string skuOrCode, string name, string family = "", string unit = "ud", int? materialId = null)
    {
        var existing = await _db.Items.SingleOrDefaultAsync(i => i.Sku == skuOrCode);
        if (existing != null) return existing;

        var item = new Item { Sku = skuOrCode, Name = name, Family = family, Unit = unit, MaterialId = materialId };
        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task<Tag> AttachTagAsync(int itemId, string code, string tagType, int packQuantity = 1)
    {
        var existing = await _db.Tags.SingleOrDefaultAsync(t => t.Code == code);
        if (existing != null) return existing;

        var tag = new Tag { ItemId = itemId, Code = code, TagType = tagType, PackQuantity = Math.Max(1, packQuantity) };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        return tag;
    }

    public async Task<Location?> GetLocationByCodeAsync(string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code)) return null;
        return await _db.Locations.SingleOrDefaultAsync(l => l.Code == code);
    }

    public async Task<Location> GetOrCreateGenericLocationAsync(string code, string type)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        var loc = await _db.Locations.SingleOrDefaultAsync(l => l.Code == code);
        if (loc != null) return loc;
        loc = new Location { Code = code, Type = type, Zone = type.ToUpperInvariant(), X = 0, Y = 0, CapacityUnits = 100000 };
        _db.Locations.Add(loc);
        await _db.SaveChangesAsync();
        return loc;
    }

    public async Task<Location> GetOrCreateSystemLocationAsync(string code, string type)
    {
        var loc = await _db.Locations.SingleOrDefaultAsync(l => l.Code == code);
        if (loc != null) return loc;

        loc = new Location { Code = code, Type = type, Zone = code, X = 0, Y = 0, CapacityUnits = 100000 };
        _db.Locations.Add(loc);
        await _db.SaveChangesAsync();
        return loc;
    }

    public async Task ApplyMovementAsync(string type, int itemId, int qty, int? fromLocId, int? toLocId, string performedBy, string notes = "")
    {
        var mv = new Movement
        {
            Type = type,
            ItemId = itemId,
            Quantity = qty,
            FromLocationId = fromLocId,
            ToLocationId = toLocId,
            PerformedBy = performedBy,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        };
        _db.Movements.Add(mv);

        if (fromLocId.HasValue)
        {
            var snap = await _db.StockSnapshots.SingleOrDefaultAsync(s => s.ItemId == itemId && s.LocationId == fromLocId.Value);
            if (snap != null)
            {
                snap.Quantity = Math.Max(0, snap.Quantity - qty);
                snap.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        if (toLocId.HasValue)
        {
            var snap = await _db.StockSnapshots.SingleOrDefaultAsync(s => s.ItemId == itemId && s.LocationId == toLocId.Value);
            if (snap == null)
            {
                snap = new StockSnapshot { ItemId = itemId, LocationId = toLocId.Value, Quantity = 0 };
                _db.StockSnapshots.Add(snap);
            }
            snap.Quantity += qty;
            snap.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<DeviceEvent> StoreDeviceEventAsync(string deviceId, string eventType, object payload)
    {
        var ev = new DeviceEvent
        {
            DeviceId = deviceId,
            EventType = eventType,
            TimestampUtc = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload)
        };
        _db.DeviceEvents.Add(ev);
        await _db.SaveChangesAsync();
        return ev;
    }
}
