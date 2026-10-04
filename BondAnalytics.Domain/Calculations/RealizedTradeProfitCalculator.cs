using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain
{
    public sealed class RealizedTradeResult
    {
        public string SaleOperationId { get; }
        public decimal? ProfitByPrice { get; }
        public decimal? CouponIncome { get; }
        public decimal? ProfitIncludingCoupons { get; }
        public string? UnavailableReason { get; }

        public RealizedTradeResult(
            string saleOperationId,
            decimal? profitByPrice,
            decimal? couponIncome,
            decimal? profitIncludingCoupons,
            string? unavailableReason)
        {
            SaleOperationId = saleOperationId;
            ProfitByPrice = profitByPrice;
            CouponIncome = couponIncome;
            ProfitIncludingCoupons = profitIncludingCoupons;
            UnavailableReason = unavailableReason;
        }
    }

    public static class RealizedTradeProfitCalculator
    {
        public static IReadOnlyDictionary<string, RealizedTradeResult> Calculate(
            IReadOnlyList<PortfolioOperation> operations)
        {
            var openLots = new Dictionary<string, Queue<PurchaseLot>>(StringComparer.Ordinal);
            var couponIncomeByInstrument = new Dictionary<string, List<CouponIncomePerUnit>>(StringComparer.Ordinal);
            var results = new Dictionary<string, RealizedTradeResult>(StringComparer.Ordinal);

            foreach (var operation in operations.OrderBy(operation => operation.Date).ThenBy(operation => operation.Id, StringComparer.Ordinal))
            {
                if (operation.TypeName == "Купон" && !string.IsNullOrWhiteSpace(operation.InstrumentUid))
                {
                    var heldQuantity = openLots.TryGetValue(operation.InstrumentUid, out var heldLots)
                        ? heldLots.Sum(lot => lot.RemainingQuantity)
                        : 0m;
                    var couponQuantity = operation.Quantity > 0 ? operation.Quantity : heldQuantity;
                    if (couponQuantity > 0)
                    {
                        if (!couponIncomeByInstrument.TryGetValue(operation.InstrumentUid, out var incomes))
                        {
                            incomes = new List<CouponIncomePerUnit>();
                            couponIncomeByInstrument.Add(operation.InstrumentUid, incomes);
                        }

                        incomes.Add(new CouponIncomePerUnit(
                            operation.Date,
                            operation.Amount / couponQuantity,
                            NormalizeCurrency(operation.Currency)));
                    }

                    continue;
                }

                if (operation.TradeDirection == PortfolioTradeDirection.None ||
                    string.IsNullOrWhiteSpace(operation.InstrumentUid) ||
                    operation.Quantity <= 0)
                {
                    continue;
                }

                if (operation.TradeDirection == PortfolioTradeDirection.Buy)
                {
                    if (!openLots.TryGetValue(operation.InstrumentUid, out var lots))
                    {
                        lots = new Queue<PurchaseLot>();
                        openLots.Add(operation.InstrumentUid, lots);
                    }

                    if (operation.Price > 0)
                        lots.Enqueue(new PurchaseLot(operation.Date, operation.Quantity, operation.Price, NormalizeCurrency(operation.Currency)));
                    continue;
                }

                results[operation.Id] = CalculateSale(operation, openLots, couponIncomeByInstrument);
            }

            return results;
        }

        private static RealizedTradeResult CalculateSale(
            PortfolioOperation sale,
            IReadOnlyDictionary<string, Queue<PurchaseLot>> openLots,
            IReadOnlyDictionary<string, List<CouponIncomePerUnit>> couponIncomeByInstrument)
        {
            if (!openLots.TryGetValue(sale.InstrumentUid, out var lots))
                return Unavailable(sale, "Не найдена история покупок для расчёта себестоимости.");

            var currency = NormalizeCurrency(sale.Currency);
            var remaining = sale.Quantity;
            var priceProfit = 0m;
            var couponIncome = 0m;
            var couponsAreComplete = true;

            while (remaining > 0 && lots.Count > 0)
            {
                var lot = lots.Peek();
                if (lot.Currency != currency)
                    return Unavailable(sale, "Валюта цены покупки не совпадает с валютой продажи.");

                var matchedQuantity = Math.Min(remaining, lot.RemainingQuantity);
                priceProfit += (sale.Price - lot.UnitPrice) * matchedQuantity;

                if (sale.IsBond && couponIncomeByInstrument.TryGetValue(sale.InstrumentUid, out var coupons))
                {
                    foreach (var coupon in coupons.Where(item => item.Date > lot.PurchasedAt && item.Date <= sale.Date))
                    {
                        if (coupon.Currency != currency)
                        {
                            couponsAreComplete = false;
                            continue;
                        }

                        couponIncome += coupon.IncomePerUnit * matchedQuantity;
                    }
                }

                lot.RemainingQuantity -= matchedQuantity;
                remaining -= matchedQuantity;
                if (lot.RemainingQuantity == 0)
                    lots.Dequeue();
            }

            if (remaining > 0)
                return Unavailable(sale, "Не найдены все покупки, соответствующие объёму продажи.");

            if (!sale.IsBond)
                return new RealizedTradeResult(sale.Id, priceProfit, 0m, priceProfit, null);

            return new RealizedTradeResult(
                sale.Id,
                priceProfit,
                couponsAreComplete ? couponIncome : null,
                couponsAreComplete ? priceProfit + couponIncome : null,
                couponsAreComplete ? null : "Цена рассчитана, но купонные выплаты нельзя полностью сопоставить по валюте или количеству.");
        }

        private static RealizedTradeResult Unavailable(PortfolioOperation sale, string reason) =>
            new(sale.Id, null, null, null, reason);

        private static string NormalizeCurrency(string currency) =>
            string.Equals(currency, "RUR", StringComparison.OrdinalIgnoreCase) ? "RUB" : currency.ToUpperInvariant();

        private sealed record CouponIncomePerUnit(DateTimeOffset Date, decimal IncomePerUnit, string Currency);

        private sealed class PurchaseLot
        {
            public DateTimeOffset PurchasedAt { get; }
            public decimal UnitPrice { get; }
            public string Currency { get; }
            public decimal RemainingQuantity { get; set; }

            public PurchaseLot(DateTimeOffset purchasedAt, decimal quantity, decimal unitPrice, string currency)
            {
                PurchasedAt = purchasedAt;
                RemainingQuantity = quantity;
                UnitPrice = unitPrice;
                Currency = currency;
            }
        }
    }
}
