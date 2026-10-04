using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domain
{
    public interface IPortfolioHistoryRepository
    {
        Task SaveSnapshotAsync(PortfolioSnapshot snapshot, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PortfolioSnapshot>> GetSnapshotsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default);
    }
}