using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using MediaModel = ManufacturingTimeTracking.Models.Templates.Media;

namespace ManufacturingTimeTracking.Pages.Media;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public IList<MediaModel> MediaList { get; set; } = default!;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _context.Media.OrderByDescending(m => m.UploadedAt);
        var totalItems = await query.CountAsync();
        MediaList = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };
    }
}
