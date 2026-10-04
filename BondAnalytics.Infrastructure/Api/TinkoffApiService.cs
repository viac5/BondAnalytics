using Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Serilog;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace Infrastructure
{
    public class TinkoffApiService : IPortfolioService
    {
        private readonly ITokenProvider _tokenProvider;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _accountIdLock = new(1, 1);
        private readonly SemaphoreSlim _operationsLock = new(1, 1);
        private readonly ConcurrentDictionary<string, string> _instrumentNames = new();
        private readonly List<PortfolioOperation> _operationCache = new();
        private string? _accountId;
        private DateTimeOffset? _operationsCachedThrough;
        private InvestApiClient? _client;

        public TinkoffApiService(ITokenProvider tokenProvider, ILogger logger)
            : this(tokenProvider, logger, null)
        {
        }

        public TinkoffApiService(ITokenProvider tokenProvider, ILogger logger, InvestApiClient? client)
        {
            _tokenProvider = tokenProvider;
            _logger = logger.ForContext<TinkoffApiService>();
            _client = client;
        }

        private InvestApiClient GetClient()
        {
            if (_client != null)
                return _client;

            var token = _tokenProvider.GetTokenAsync().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.Warning("T-Invest API token is not configured");
                throw new InvalidOperationException("Не задана пользовательская переменная BOND_ANALYTICS_TOKEN.");
            }

            _client = InvestApiClientFactory.Create(token);
            _logger.Information("T-Invest API client initialized");
            return _client;
        }

        private static decimal ToDecimal(Quotation q)
        {
            return q.Units + q.Nano / 1_000_000_000m;
        }

        private static decimal ToDecimal(MoneyValue m)
        {
            return m.Units + m.Nano / 1_000_000_000m;
        }


        public async Task<PortfolioData> GetPortfolioAsync(CancellationToken cancellationToken = default)
        {
            var client = GetClient();
            _logger.Information("Requesting brokerage accounts");
            var accountId = await GetAccountIdAsync(client, cancellationToken);

            var portfolio = await client.Operations.GetPortfolioAsync(new PortfolioRequest
            {
                AccountId = accountId,
                Currency = PortfolioRequest.Types.CurrencyRequest.Rub
            }, cancellationToken: cancellationToken);
            _logger.Information("Portfolio positions received: {PositionCount}", portfolio.Positions.Count);

            var result = new List<PortfolioItem>();

            foreach (var pos in portfolio.Positions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ticker = pos.Ticker;          // SU26243RMFS4
                var uid = pos.InstrumentUid;      // UUID

                // Запрос названия
                var instrument = await client.Instruments.GetInstrumentByAsync(new InstrumentRequest
                {
                    Id = uid,
                    IdType = InstrumentIdType.Uid
                }, cancellationToken: cancellationToken);

                //decimal nominal = await GetNominalAsync(uid, instrument);

                var name = instrument.Instrument.Name; // ОФЗ 26243
                _instrumentNames.TryAdd(uid, string.IsNullOrWhiteSpace(ticker) ? name : $"{ticker} · {name}");

                var qty = ToDecimal(pos.Quantity);

                var avg = pos.AveragePositionPrice != null
                    ? ToDecimal(pos.AveragePositionPrice)
                    : 0;

                var current = pos.CurrentPrice != null
                    ? ToDecimal(pos.CurrentPrice)
                    : avg;
                var accruedInterestPerBond = pos.CurrentNkd != null
                    ? ToDecimal(pos.CurrentNkd)
                    : 0;

                var lot = instrument.Instrument.Lot;

                var analytics = await GetBondAsync(client, uid, instrument, cancellationToken);


                result.Add(new PortfolioItem(
                    ticker,
                    name,
                    uid,
                    pos.InstrumentType,
                    lot,
                    qty,
                    avg,
                    current,
                    accruedInterestPerBond,
                    analytics?.Nominal ?? 0m,
                    analytics?.Coupon ?? 0m, 
                    analytics?.CouponsPerYear ?? 0, 
                    analytics?.CurrentYield ?? 0m, 
                    analytics?.NextCouponDate
                ));

            }

            var totalValueRub = portfolio.TotalAmountPortfolio != null
                ? ToDecimal(portfolio.TotalAmountPortfolio)
                : result.Sum(item => item.Quantity * item.CurrentPrice + item.Quantity * item.AccruedInterestPerBond);

            return new PortfolioData(result, totalValueRub);
        }

        public async Task<IReadOnlyList<PortfolioOperation>> GetOperationsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            if (from > to)
                throw new ArgumentException("Начало периода позже его окончания.", nameof(from));

            await _operationsLock.WaitAsync(cancellationToken);
            try
            {
                if (_operationsCachedThrough == null || _operationsCachedThrough < to)
                {
                    var fetchFrom = _operationsCachedThrough ?? DateTimeOffset.UnixEpoch;
                    var newOperations = await FetchOperationsAsync(fetchFrom, to, cancellationToken);
                    var existingIds = _operationCache.Select(operation => operation.Id).ToHashSet(StringComparer.Ordinal);
                    _operationCache.AddRange(newOperations.Where(operation => existingIds.Add(operation.Id)));
                    _operationCache.Sort((left, right) => left.Date.CompareTo(right.Date));
                    _operationsCachedThrough = to;
                }

                return _operationCache
                    .Where(operation => operation.Date >= from && operation.Date <= to)
                    .OrderByDescending(operation => operation.Date)
                    .ToList();
            }
            finally
            {
                _operationsLock.Release();
            }
        }

        private async Task<IReadOnlyList<PortfolioOperation>> FetchOperationsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            var client = GetClient();
            var accountId = await GetAccountIdAsync(client, cancellationToken);
            var request = new GetOperationsByCursorRequest
            {
                AccountId = accountId,
                From = Timestamp.FromDateTime(from.UtcDateTime),
                To = Timestamp.FromDateTime(to.UtcDateTime),
                Limit = 1000,
                State = OperationState.Executed,
                WithoutCommissions = false,
                WithoutTrades = false,
                WithoutOvernights = true
            };

            var operations = new List<PortfolioOperation>();
            var pages = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await client.Operations.GetOperationsByCursorAsync(
                    request,
                    cancellationToken: cancellationToken);
                foreach (var operation in response.Items)
                {
                    var kind = GetOperationKind(operation.Type);
                    var instrumentName = await GetInstrumentNameAsync(
                        client,
                        operation.InstrumentUid,
                        operation.Figi,
                        cancellationToken);
                    var payment = operation.Payment;
                    var price = operation.Price;

                    operations.Add(new PortfolioOperation(
                        operation.Id,
                        operation.Date.ToDateTimeOffset().ToLocalTime(),
                        kind,
                        GetTradeDirection(operation.Type),
                        GetOperationTypeName(operation.Type, kind),
                        instrumentName,
                        operation.InstrumentUid,
                        string.Equals(operation.InstrumentType, "bond", StringComparison.OrdinalIgnoreCase),
                        operation.Quantity,
                        price == null ? 0 : ToDecimal(price),
                        payment == null ? 0 : Math.Abs(ToDecimal(payment)),
                        string.IsNullOrWhiteSpace(payment?.Currency) ? "RUB" : payment.Currency.ToUpperInvariant()));
                }

                pages++;
                if (!response.HasNext)
                    break;
                if (pages >= 200)
                    throw new InvalidOperationException("История операций превысила лимит страниц. Невозможно достоверно рассчитать результат продаж.");
                request.Cursor = response.NextCursor;
            }
            while (true);

            _logger.Information("Operations loaded: {OperationCount} records, {PageCount} pages", operations.Count, pages);
            return operations.OrderByDescending(operation => operation.Date).ToList();
        }

        private async Task<string> GetAccountIdAsync(
            InvestApiClient client,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_accountId))
                return _accountId;

            await _accountIdLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_accountId))
                    return _accountId;

                var accounts = await client.Users.GetAccountsAsync(cancellationToken: cancellationToken);
                _accountId = accounts.Accounts.FirstOrDefault()?.Id;
                if (string.IsNullOrWhiteSpace(_accountId))
                    throw new InvalidOperationException("Нет доступных брокерских счетов.");

                return _accountId;
            }
            finally
            {
                _accountIdLock.Release();
            }
        }

        private async Task<string> GetInstrumentNameAsync(
            InvestApiClient client,
            string instrumentUid,
            string fallback,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(instrumentUid))
                return string.IsNullOrWhiteSpace(fallback) ? "Денежная операция" : fallback;

            if (_instrumentNames.TryGetValue(instrumentUid, out var cachedName))
                return cachedName;

            try
            {
                var response = await client.Instruments.GetInstrumentByAsync(new InstrumentRequest
                {
                    Id = instrumentUid,
                    IdType = InstrumentIdType.Uid
                }, cancellationToken: cancellationToken);

                var instrument = response.Instrument;
                var resolvedName = string.IsNullOrWhiteSpace(instrument.Ticker)
                    ? instrument.Name
                    : $"{instrument.Ticker} · {instrument.Name}";
                _instrumentNames.TryAdd(instrumentUid, resolvedName);
                return resolvedName;
            }
            catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warning(ex, "Could not resolve instrument name for {InstrumentUid}", instrumentUid);
                return string.IsNullOrWhiteSpace(fallback) ? instrumentUid : fallback;
            }
        }

        private static PortfolioOperationKind GetOperationKind(OperationType type) => type switch
        {
            OperationType.Input or OperationType.InputAcquiring or OperationType.InputSwift or OperationType.InpMulti
                => PortfolioOperationKind.Deposit,
            OperationType.Output or OperationType.OutputAcquiring or OperationType.OutputSwift or OperationType.OutMulti
                => PortfolioOperationKind.Withdrawal,
            OperationType.Buy or OperationType.Sell or OperationType.BuyCard or OperationType.SellCard or
                OperationType.BuyMargin or OperationType.SellMargin or OperationType.DeliveryBuy or OperationType.DeliverySell
                => PortfolioOperationKind.Trade,
            OperationType.Coupon or OperationType.Dividend or OperationType.BondRepayment or
                OperationType.BondRepaymentFull or OperationType.OverIncome
                => PortfolioOperationKind.Income,
            _ => PortfolioOperationKind.Other
        };

        private static string GetOperationTypeName(OperationType type, PortfolioOperationKind kind)
        {
            if (kind == PortfolioOperationKind.Deposit)
                return "Пополнение";
            if (kind == PortfolioOperationKind.Withdrawal)
                return "Вывод";
            if (kind == PortfolioOperationKind.Trade)
            {
                return type is OperationType.Buy or OperationType.BuyCard or OperationType.BuyMargin or OperationType.DeliveryBuy
                    ? "Покупка"
                    : "Продажа";
            }
            if (type == OperationType.Coupon)
                return "Купон";
            if (type == OperationType.Dividend)
                return "Дивиденд";
            if (type is OperationType.BondRepayment or OperationType.BondRepaymentFull)
                return "Погашение облигации";
            if (kind == PortfolioOperationKind.Income)
                return "Доход";

            return "Прочая операция";
        }

        private static PortfolioTradeDirection GetTradeDirection(OperationType type) => type switch
        {
            OperationType.Buy or OperationType.BuyCard or OperationType.BuyMargin or OperationType.DeliveryBuy
                => PortfolioTradeDirection.Buy,
            OperationType.Sell or OperationType.SellCard or OperationType.SellMargin or OperationType.DeliverySell
                => PortfolioTradeDirection.Sell,
            _ => PortfolioTradeDirection.None
        };

        //private async Task<decimal> GetNominalAsync(string uid, InstrumentResponse inst)
        //{
            
        //    if (!string.Equals(inst.Instrument.InstrumentType, "BOND", StringComparison.OrdinalIgnoreCase))
        //       return 1m;

        //    var call = _client.Instruments.BondByAsync(new InstrumentRequest
        //    {
        //        Id = uid,
        //        IdType = InstrumentIdType.Uid
        //    });

        //    var bond = await call.ResponseAsync;

        //    return ToDecimal(bond.Instrument.Nominal);
        //}
        private async Task<BondItem?> GetBondAsync(
            InvestApiClient client,
            string uid,
            InstrumentResponse inst,
            CancellationToken cancellationToken)
        {
            // Если это не облигация — возвращаем null
            if (!string.Equals(inst.Instrument.InstrumentType, "BOND", StringComparison.OrdinalIgnoreCase))
                return null;

            var call = client.Instruments.BondByAsync(new InstrumentRequest
            {
                Id = uid,
                IdType = InstrumentIdType.Uid
            }, cancellationToken: cancellationToken);

            var bond = await call.ResponseAsync;

            decimal nominal = ToDecimal(bond.Instrument.Nominal);

            var coupons = await client.Instruments.GetBondCouponsAsync(new GetBondCouponsRequest
            {
                InstrumentId = uid,
                From = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                To = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(2))
            }, cancellationToken: cancellationToken);

            var nextCoupon = coupons.Events
                .Where(c => c.CouponDate.ToDateTime() > DateTime.Now)
                .OrderBy(c => c.CouponDate.ToDateTime())
                .FirstOrDefault();

            if (nextCoupon == null)
            {
                return new BondItem
                {
                    Nominal = nominal
                };
            }

            decimal coupon = ToDecimal(nextCoupon.PayOneBond);

            int couponsPerYear = (int)Math.Round(365.0 / nextCoupon.CouponPeriod);

            decimal annualCoupon = coupon * couponsPerYear;
            decimal currentYield = annualCoupon / nominal;

            return new BondItem
            {
                Nominal = nominal,
                Coupon = coupon,
                CouponsPerYear = couponsPerYear,
                NextCouponDate = nextCoupon.CouponDate.ToDateTime(),
                CurrentYield = currentYield
            };
        }


        public async IAsyncEnumerable<MarketDataResponse> SubscribePricesAsync(
            IEnumerable<string> uids,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var stream = GetClient().MarketDataStream.MarketDataStream(cancellationToken: cancellationToken);

            var request = new MarketDataRequest
            {
                SubscribeLastPriceRequest = new SubscribeLastPriceRequest
                {
                    SubscriptionAction = SubscriptionAction.Subscribe
                }
            };

            foreach (var uid in uids)
            {
                request.SubscribeLastPriceRequest.Instruments.Add(
                    new LastPriceInstrument { InstrumentId = uid }
                );
            }

            await stream.RequestStream.WriteAsync(request, cancellationToken);

            await foreach (var response in stream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                yield return response;
            }
        }





    }
}
