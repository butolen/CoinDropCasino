using WebApp.services.dtos;

namespace CoinDrop.services.interfaces;


public interface IAdminDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(TimeRange range, CancellationToken ct = default);

    Task<PagedResultAdmin<LogItemDto>> GetLogsAsync(
        TimeRange range,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);
}