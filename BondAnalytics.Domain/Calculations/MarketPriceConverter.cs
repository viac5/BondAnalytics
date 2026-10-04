using System;

namespace Domain
{
    public static class MarketPriceConverter
    {
        public static decimal ToUnitPrice(string instrumentType, decimal marketQuote, decimal nominal)
        {
            if (marketQuote < 0)
                throw new ArgumentOutOfRangeException(nameof(marketQuote), "Цена не может быть отрицательной.");

            if (!string.Equals(instrumentType, "bond", StringComparison.OrdinalIgnoreCase))
                return marketQuote;

            if (nominal <= 0)
                throw new ArgumentOutOfRangeException(nameof(nominal), "Номинал облигации должен быть больше нуля.");

            return marketQuote * nominal / 100m;
        }
    }
}