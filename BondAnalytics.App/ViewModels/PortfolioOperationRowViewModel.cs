using Domain;
using System.Windows.Media;

namespace App.ViewModels
{
    public sealed class PortfolioOperationRowViewModel
    {
        private readonly RealizedTradeResult? _realizedTrade;

        public PortfolioOperation Operation { get; }
        public string IconGlyph { get; }
        public Brush IconBrush { get; }
        public string ProfitByPriceText { get; }
        public string ProfitIncludingCouponsText { get; }
        public bool HasRealizedSale => Operation.TradeDirection == PortfolioTradeDirection.Sell;
        public string? ProfitTooltip => _realizedTrade?.UnavailableReason;

        public PortfolioOperationRowViewModel(PortfolioOperation operation, RealizedTradeResult? realizedTrade)
        {
            Operation = operation;
            _realizedTrade = realizedTrade;
            (IconGlyph, IconBrush) = GetIcon(operation);

            ProfitByPriceText = !HasRealizedSale
                ? string.Empty
                : realizedTrade?.ProfitByPrice is decimal priceProfit
                    ? FormatProfit(priceProfit, operation.Currency)
                    : "н/д";

            ProfitIncludingCouponsText = !HasRealizedSale
                ? string.Empty
                : realizedTrade?.ProfitIncludingCoupons is decimal totalProfit
                    ? FormatProfit(totalProfit, operation.Currency)
                    : Operation.IsBond ? "н/д" : ProfitByPriceText;
        }

        private static string FormatProfit(decimal value, string currency)
        {
            var symbol = currency.ToUpperInvariant() switch
            {
                "RUB" or "RUR" => "₽",
                "USD" => "$",
                "EUR" => "€",
                _ => currency.ToUpperInvariant()
            };
            return $"{value:+#,0.00;-#,0.00;0.00} {symbol}";
        }

        private static (string Glyph, Brush Color) GetIcon(PortfolioOperation operation)
        {
            if (operation.Kind == PortfolioOperationKind.Deposit)
                return ("↓", Brushes.SeaGreen);
            if (operation.Kind == PortfolioOperationKind.Withdrawal)
                return ("↑", Brushes.IndianRed);
            if (operation.TradeDirection == PortfolioTradeDirection.Buy)
                return ("+", Brushes.SteelBlue);
            if (operation.TradeDirection == PortfolioTradeDirection.Sell)
                return ("−", Brushes.DarkOrange);
            if (operation.TypeName == "Купон")
                return ("₽", Brushes.SeaGreen);
            if (operation.Kind == PortfolioOperationKind.Income)
                return ("◆", Brushes.DarkCyan);
            return ("•", Brushes.Gray);
        }
    }
}
