using System.Collections.Generic;

namespace Domain
{
    public sealed class PortfolioData
    {
        public IReadOnlyList<PortfolioItem> Positions { get; }
        public decimal TotalValueRub { get; }

        public PortfolioData(IReadOnlyList<PortfolioItem> positions, decimal totalValueRub)
        {
            Positions = positions;
            TotalValueRub = totalValueRub;
        }
    }
}