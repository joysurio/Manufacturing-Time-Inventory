namespace ManufacturingTimeTracking.Models;

public class PaginationInfo
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalItems { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 1;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public static readonly int[] PageSizeOptions = { 25, 50, 100 };
}
