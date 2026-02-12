using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tinkoff.InvestApi.V1;

namespace Domain
{
    public interface IPortfolioService
    {
        Task<IReadOnlyList<PortfolioItem>> GetPortfolioAsync();
        IAsyncEnumerable<MarketDataResponse> SubscribePricesAsync(IEnumerable<string> uids);

    }
}

