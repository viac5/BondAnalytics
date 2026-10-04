using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain
{
    public sealed class CurrencyTotal
    {
        public string Currency { get; }
        public decimal Amount { get; }

        public CurrencyTotal(string currency, decimal amount)
        {
            Currency = currency;
            Amount = amount;
        }
    }

    public sealed class PortfolioPeriodSummary
    {
        public int DepositCount { get; }
        public int WithdrawalCount { get; }
        public int TradeCount { get; }
        public IReadOnlyList<CurrencyTotal> Deposits { get; }
        public IReadOnlyList<CurrencyTotal> Withdrawals { get; }
        public IReadOnlyList<CurrencyTotal> NetCashFlows { get; }
        public decimal? ProfitRub { get; }
        public decimal? Return { get; }
        public string? ReturnUnavailableReason { get; }

        public PortfolioPeriodSummary(
            int depositCount,
            int withdrawalCount,
            int tradeCount,
            IReadOnlyList<CurrencyTotal> deposits,
            IReadOnlyList<CurrencyTotal> withdrawals,
            IReadOnlyList<CurrencyTotal> netCashFlows,
            decimal? profitRub,
            decimal? periodReturn,
            string? returnUnavailableReason)
        {
            DepositCount = depositCount;
            WithdrawalCount = withdrawalCount;
            TradeCount = tradeCount;
            Deposits = deposits;
            Withdrawals = withdrawals;
            NetCashFlows = netCashFlows;
            ProfitRub = profitRub;
            Return = periodReturn;
            ReturnUnavailableReason = returnUnavailableReason;
        }
    }

    public static class PortfolioPeriodCalculator
    {
        public static PortfolioPeriodSummary Calculate(
            IReadOnlyList<PortfolioOperation> operations,
            IReadOnlyList<PortfolioSnapshot> snapshots,
            DateTimeOffset from,
            DateTimeOffset to)
        {
            if (from > to)
                throw new ArgumentException("Начало периода позже его окончания.", nameof(from));

            var periodOperations = operations
                .Where(operation => operation.Date >= from && operation.Date <= to)
                .ToList();
            var deposits = periodOperations.Where(operation => operation.Kind == PortfolioOperationKind.Deposit).ToList();
            var withdrawals = periodOperations.Where(operation => operation.Kind == PortfolioOperationKind.Withdrawal).ToList();
            var externalFlows = deposits.Concat(withdrawals).ToList();
            var depositTotals = AggregateByCurrency(deposits, operation => operation.Amount);
            var withdrawalTotals = AggregateByCurrency(withdrawals, operation => operation.Amount);
            var netTotals = AggregateByCurrency(externalFlows, operation =>
                operation.Kind == PortfolioOperationKind.Deposit ? operation.Amount : -operation.Amount);

            if (externalFlows.Any(operation => NormalizeCurrency(operation.Currency) != "RUB"))
            {
                return CreateUnavailableSummary(
                    deposits.Count,
                    withdrawals.Count,
                    periodOperations.Count(operation => operation.Kind == PortfolioOperationKind.Trade),
                    depositTotals,
                    withdrawalTotals,
                    netTotals,
                    "Доходность не рассчитана: в периоде есть потоки не в рублях.");
            }

            var startSnapshot = snapshots
                .Where(snapshot => snapshot.CapturedAt <= from)
                .MaxBy(snapshot => snapshot.CapturedAt);
            var endSnapshot = snapshots
                .Where(snapshot => snapshot.CapturedAt <= to)
                .MaxBy(snapshot => snapshot.CapturedAt);

            if (startSnapshot == null || endSnapshot == null)
            {
                return CreateUnavailableSummary(
                    deposits.Count,
                    withdrawals.Count,
                    periodOperations.Count(operation => operation.Kind == PortfolioOperationKind.Trade),
                    depositTotals,
                    withdrawalTotals,
                    netTotals,
                    "Доходность появится после накопления снимков стоимости к началу и концу периода.");
            }

            var periodSeconds = Math.Max(1d, (to - from).TotalSeconds);
            var netFlows = 0m;
            var weightedFlows = 0m;
            foreach (var flow in externalFlows)
            {
                var signedAmount = flow.Kind == PortfolioOperationKind.Deposit ? flow.Amount : -flow.Amount;
                netFlows += signedAmount;
                var remainingPeriodWeight = Math.Clamp((decimal)((to - flow.Date).TotalSeconds / periodSeconds), 0m, 1m);
                weightedFlows += signedAmount * remainingPeriodWeight;
            }

            var profit = endSnapshot.TotalValueRub - startSnapshot.TotalValueRub - netFlows;
            var investedCapital = startSnapshot.TotalValueRub + weightedFlows;
            if (investedCapital <= 0)
            {
                return CreateUnavailableSummary(
                    deposits.Count,
                    withdrawals.Count,
                    periodOperations.Count(operation => operation.Kind == PortfolioOperationKind.Trade),
                    depositTotals,
                    withdrawalTotals,
                    netTotals,
                    "Доходность не рассчитана: вложенный капитал за период неположительный.",
                    profit);
            }

            return new PortfolioPeriodSummary(
                deposits.Count,
                withdrawals.Count,
                periodOperations.Count(operation => operation.Kind == PortfolioOperationKind.Trade),
                depositTotals,
                withdrawalTotals,
                netTotals,
                profit,
                profit / investedCapital,
                null);
        }

        private static IReadOnlyList<CurrencyTotal> AggregateByCurrency(
            IEnumerable<PortfolioOperation> operations,
            Func<PortfolioOperation, decimal> selector) => operations
                .GroupBy(operation => NormalizeCurrency(operation.Currency))
                .Select(group => new CurrencyTotal(group.Key, group.Sum(selector)))
                .OrderBy(total => total.Currency)
                .ToList();

        private static PortfolioPeriodSummary CreateUnavailableSummary(
            int depositCount,
            int withdrawalCount,
            int tradeCount,
            IReadOnlyList<CurrencyTotal> deposits,
            IReadOnlyList<CurrencyTotal> withdrawals,
            IReadOnlyList<CurrencyTotal> netFlows,
            string reason,
            decimal? profit = null) => new(
                depositCount,
                withdrawalCount,
                tradeCount,
                deposits,
                withdrawals,
                netFlows,
                profit,
                null,
                reason);

        private static string NormalizeCurrency(string currency) =>
            string.Equals(currency, "RUR", StringComparison.OrdinalIgnoreCase) ? "RUB" : currency.ToUpperInvariant();
    }
}
