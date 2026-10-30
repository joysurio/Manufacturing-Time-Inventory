using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class VariantsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public VariantsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetVariants([FromQuery] int modelId)
    {
        var variants = await _context.MachineVariants
            .Where(v => v.MachineModelId == modelId && v.Active)
            .Select(v => new { id = v.Id, name = v.Name, code = v.Code })
            .ToListAsync();

        return Ok(variants);
    }
}
