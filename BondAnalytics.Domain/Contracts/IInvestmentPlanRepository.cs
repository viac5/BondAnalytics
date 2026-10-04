using System.Threading;
using System.Threading.Tasks;

namespace Domain
{
    public interface IInvestmentPlanRepository
    {
        Task<InvestmentPlan?> GetActivePlanAsync(CancellationToken cancellationToken = default);
        Task SaveActivePlanAsync(InvestmentPlan plan, CancellationToken cancellationToken = default);
    }
}