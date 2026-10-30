using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.AI;

namespace ManufacturingTimeTracking.Pages.AI.Prompts;

[Authorize(Roles = "Admin")]
public class PromptsIndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public PromptsIndexModel(ApplicationDbContext db) => _db = db;

    public List<AiPrompt> Prompts { get; set; } = new();

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string PromptText { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Prompts = await _db.AiPrompts.OrderBy(p => p.Name).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Name) || string.IsNullOrWhiteSpace(Input.PromptText))
        {
            TempData["ToastrError"] = "Name and Prompt Text are required.";
            Prompts = await _db.AiPrompts.OrderBy(p => p.Name).ToListAsync();
            return Page();
        }

        if (await _db.AiPrompts.AnyAsync(p => p.Name == Input.Name))
        {
            TempData["ToastrError"] = $"A prompt named '{Input.Name}' already exists.";
            Prompts = await _db.AiPrompts.OrderBy(p => p.Name).ToListAsync();
            return Page();
        }

        _db.AiPrompts.Add(new AiPrompt
        {
            Name = Input.Name.Trim(),
            Description = Input.Description?.Trim(),
            PromptText = Input.PromptText.Trim(),
            LastUpdate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["ToastrSuccess"] = $"Prompt '{Input.Name}' created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var prompt = await _db.AiPrompts.FindAsync(id);
        if (prompt != null)
        {
            _db.AiPrompts.Remove(prompt);
            await _db.SaveChangesAsync();
            TempData["ToastrSuccess"] = $"Prompt '{prompt.Name}' deleted.";
        }
        return RedirectToPage();
    }
}
