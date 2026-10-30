using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class ProcessTemplatesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ProcessTemplatesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: api/ProcessTemplates
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProcessTemplateDto>>> GetProcessTemplates()
    {
        var templates = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
            .Select(pt => new ProcessTemplateDto
            {
                Id = pt.Id,
                MachineModelId = pt.MachineModelId,
                MachineVariantId = pt.MachineVariantId,
                PhaseCount = pt.Phases.Count
            })
            .ToListAsync();

        return Ok(templates);
    }

    // GET: api/ProcessTemplates/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ProcessTemplateDetailDto>> GetProcessTemplate(int id)
    {
        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (template == null)
        {
            return NotFound();
        }

        var dto = new ProcessTemplateDetailDto
        {
            Id = template.Id,
            MachineModelId = template.MachineModelId,
            MachineVariantId = template.MachineVariantId,
            Phases = template.Phases.OrderBy(p => p.SortOrder).Select(p => new PhaseTemplateDto
            {
                Id = p.Id,
                ProcessTemplateId = p.ProcessTemplateId,
                SortOrder = p.SortOrder,
                Name = p.Name,
                StepCount = p.Steps.Count
            }).ToList()
        };

        return Ok(dto);
    }
    
    // GET: api/ProcessTemplates/full/5 - Get full template with all details
    [HttpGet("full/{id}")]
    public async Task<ActionResult<ProcessTemplateFullDto>> GetProcessTemplateFull(int id)
    {
        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Tools)
                        .ThenInclude(st => st.Tool)
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Materials)
                        .ThenInclude(sm => sm.Material)
            .AsSplitQuery()
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Media)
                        .ThenInclude(sm => sm.Media)
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (template == null)
        {
            return NotFound();
        }

        var dto = new ProcessTemplateFullDto
        {
            Id = template.Id,
            MachineModelId = template.MachineModelId,
            MachineVariantId = template.MachineVariantId,
            Phases = template.Phases.OrderBy(p => p.SortOrder).Select(p => new PhaseTemplateFullDto
            {
                Id = p.Id,
                ProcessTemplateId = p.ProcessTemplateId,
                SortOrder = p.SortOrder,
                Name = p.Name,
                Steps = p.Steps.OrderBy(s => s.SortOrder).Select(s => new StepTemplateDto
                {
                    Id = s.Id,
                    PhaseTemplateId = s.PhaseTemplateId,
                    SortOrder = s.SortOrder,
                    Title = s.Title,
                    Instructions = s.Instructions,
                    AllowSkip = s.AllowSkip,
                    Tools = s.Tools.Select(t => new StepToolDto
                    {
                        Id = t.Id,
                        ToolId = t.ToolId,
                        ToolName = t.Tool.Name
                    }).ToList(),
                    Materials = s.Materials.Select(m => new StepMaterialDto
                    {
                        Id = m.Id,
                        MaterialId = m.MaterialId,
                        MaterialName = m.Material.Name,
                        Qty = m.Qty,
                        Unit = m.Material.Unit
                    }).ToList(),
                    Media = s.Media.Select(med => new StepMediaDto
                    {
                        Id = med.Id,
                        MediaId = med.MediaId,
                        Caption = med.Caption,
                        MediaType = med.Media.Type,
                        MediaUrl = med.Media.UrlOrPath
                    }).ToList()
                }).ToList()
            }).ToList()
        };

        return Ok(dto);
    }

    // POST: api/ProcessTemplates
    [HttpPost]
    public async Task<ActionResult<ProcessTemplateDto>> CreateProcessTemplate(CreateProcessTemplateDto dto)
    {
        // Validate that MachineModel and MachineVariant exist
        var machineModel = await _context.MachineModels.FindAsync(dto.MachineModelId);
        if (machineModel == null)
        {
            return BadRequest(new { error = "MachineModel not found." });
        }

        var machineVariant = await _context.MachineVariants.FindAsync(dto.MachineVariantId);
        if (machineVariant == null)
        {
            return BadRequest(new { error = "MachineVariant not found." });
        }

        // Check if a template already exists for this model/variant combination
        var existingTemplate = await _context.ProcessTemplates
            .FirstOrDefaultAsync(pt => pt.MachineModelId == dto.MachineModelId && 
                                      pt.MachineVariantId == dto.MachineVariantId);

        if (existingTemplate != null)
        {
            return Conflict(new { error = "A ProcessTemplate already exists for this MachineModel and MachineVariant combination." });
        }

        var template = new ProcessTemplate
        {
            MachineModelId = dto.MachineModelId,
            MachineVariantId = dto.MachineVariantId
        };

        _context.ProcessTemplates.Add(template);
        await _context.SaveChangesAsync();

        var result = new ProcessTemplateDto
        {
            Id = template.Id,
            MachineModelId = template.MachineModelId,
            MachineVariantId = template.MachineVariantId,
            PhaseCount = 0
        };

        return CreatedAtAction(nameof(GetProcessTemplate), new { id = template.Id }, result);
    }

    // PUT: api/ProcessTemplates/5
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProcessTemplate(int id, UpdateProcessTemplateDto dto)
    {
        if (id != dto.Id)
        {
            return BadRequest(new { error = "ID mismatch." });
        }

        var template = await _context.ProcessTemplates.FindAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        // Validate that MachineModel and MachineVariant exist
        var machineModel = await _context.MachineModels.FindAsync(dto.MachineModelId);
        if (machineModel == null)
        {
            return BadRequest(new { error = "MachineModel not found." });
        }

        var machineVariant = await _context.MachineVariants.FindAsync(dto.MachineVariantId);
        if (machineVariant == null)
        {
            return BadRequest(new { error = "MachineVariant not found." });
        }

        // Check if another template already exists for this model/variant combination
        var existingTemplate = await _context.ProcessTemplates
            .FirstOrDefaultAsync(pt => pt.MachineModelId == dto.MachineModelId && 
                                      pt.MachineVariantId == dto.MachineVariantId &&
                                      pt.Id != id);

        if (existingTemplate != null)
        {
            return Conflict(new { error = "Another ProcessTemplate already exists for this MachineModel and MachineVariant combination." });
        }

        template.MachineModelId = dto.MachineModelId;
        template.MachineVariantId = dto.MachineVariantId;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ProcessTemplateExists(id))
            {
                return NotFound();
            }
            throw;
        }

        return NoContent();
    }

    // DELETE: api/ProcessTemplates/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProcessTemplate(int id)
    {
        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (template == null)
        {
            return NotFound();
        }

        // Check if template is being used by any BuildOrders
        var isInUse = await _context.BuildOrders
            .AnyAsync(bo => bo.MachineModelId == template.MachineModelId && 
                           bo.MachineVariantId == template.MachineVariantId);

        if (isInUse)
        {
            return Conflict(new { error = "Cannot delete ProcessTemplate because it is being used by existing BuildOrders." });
        }

        _context.ProcessTemplates.Remove(template);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ProcessTemplateExists(int id)
    {
        return _context.ProcessTemplates.Any(e => e.Id == id);
    }
}

// DTOs
public class ProcessTemplateDto
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
    public int PhaseCount { get; set; }
}

public class ProcessTemplateDetailDto
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
    public List<PhaseTemplateDto> Phases { get; set; } = new();
}

public class PhaseTemplateDto
{
    public int Id { get; set; }
    public int ProcessTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StepCount { get; set; }
}

public class CreateProcessTemplateDto
{
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
}

public class UpdateProcessTemplateDto
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
}

public class ProcessTemplateFullDto
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
    public List<PhaseTemplateFullDto> Phases { get; set; } = new();
}

public class PhaseTemplateFullDto
{
    public int Id { get; set; }
    public int ProcessTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<StepTemplateDto> Steps { get; set; } = new();
}

public class StepTemplateDto
{
    public int Id { get; set; }
    public int PhaseTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool AllowSkip { get; set; }
    public List<StepToolDto> Tools { get; set; } = new();
    public List<StepMaterialDto> Materials { get; set; } = new();
    public List<StepMediaDto> Media { get; set; } = new();
}

public class StepToolDto
{
    public int Id { get; set; }
    public int ToolId { get; set; }
    public string ToolName { get; set; } = string.Empty;
}

public class StepMaterialDto
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public decimal? Qty { get; set; }
    public string? Unit { get; set; }
}

public class StepMediaDto
{
    public int Id { get; set; }
    public int MediaId { get; set; }
    public string? Caption { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string MediaUrl { get; set; } = string.Empty;
}
