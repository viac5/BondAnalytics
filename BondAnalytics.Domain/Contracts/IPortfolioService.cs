using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Tinkoff.InvestApi.V1;

namespace Domain
{
    public interface IPortfolioService
    {
        Task<PortfolioData> GetPortfolioAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PortfolioOperation>> GetOperationsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default);
        IAsyncEnumerable<MarketDataResponse> SubscribePricesAsync(
            IEnumerable<string> uids,
            CancellationToken cancellationToken = default);

    }
}
