using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ManufacturingTimeTracking.Data;

namespace ManufacturingTimeTracking.Pages.AI.Prompts;

[Authorize(Roles = "Admin")]
public class PromptsEditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public PromptsEditModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public int PromptId { get; set; }
    public string OriginalName { get; set; } = "";

    public class InputModel
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string PromptText { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var prompt = await _db.AiPrompts.FindAsync(id);
        if (prompt == null) return NotFound();

        PromptId = id;
        OriginalName = prompt.Name;
        Input = new InputModel
        {
            Name = prompt.Name,
            Description = prompt.Description,
            PromptText = prompt.PromptText
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var prompt = await _db.AiPrompts.FindAsync(id);
        if (prompt == null) return NotFound();

        if (string.IsNullOrWhiteSpace(Input.Name) || string.IsNullOrWhiteSpace(Input.PromptText))
        {
            TempData["ToastrError"] = "Name and Prompt Text are required.";
            PromptId = id;
            OriginalName = prompt.Name;
            return Page();
        }

        prompt.Name = Input.Name.Trim();
        prompt.Description = Input.Description?.Trim();
        prompt.PromptText = Input.PromptText.Trim();
        prompt.LastUpdate = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["ToastrSuccess"] = $"Prompt '{prompt.Name}' updated.";
        return RedirectToPage("./Index");
    }
}
