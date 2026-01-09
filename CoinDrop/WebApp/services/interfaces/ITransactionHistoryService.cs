using WebApp.services.dtos;

namespace CoinDrop.services.interfaces;

using System.Threading;
using System.Threading.Tasks;



public interface ITransactionHistoryService
{
    Task<PagedResultHistory<TransactionHistoryRowDto>> GetForUserAsync(
        int userId,
        TransactionHistoryQuery query,
        CancellationToken ct = default);
}