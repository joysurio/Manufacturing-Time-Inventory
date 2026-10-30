using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Tools;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public IList<Models.Templates.Tool> Tools { get; set; } = default!;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _context.Tools.OrderBy(t => t.Name);
        var totalItems = await query.CountAsync();
        Tools = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };
    }
}
