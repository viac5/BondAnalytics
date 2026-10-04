using System;

namespace Domain
{
    public sealed class PortfolioSnapshot
    {
        public DateTimeOffset CapturedAt { get; }
        public decimal TotalValueRub { get; }

        public PortfolioSnapshot(DateTimeOffset capturedAt, decimal totalValueRub)
        {
            CapturedAt = capturedAt;
            TotalValueRub = totalValueRub;
        }
    }
}