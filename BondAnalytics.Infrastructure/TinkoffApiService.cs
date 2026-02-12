using Domain;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace Infrastructure
{
    public class TinkoffApiService : IPortfolioService
    {
        private readonly InvestApiClient _client;

        public TinkoffApiService(ITokenProvider tokenProvider)
        {
            var token = tokenProvider.GetTokenAsync().Result;

            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("Токен пустой. Проверь переменную окружения.");

            _client = InvestApiClientFactory.Create(token);
        }

        private static decimal ToDecimal(Quotation q)
        {
            return q.Units + q.Nano / 1_000_000_000m;
        }

        private static decimal ToDecimal(MoneyValue m)
        {
            return m.Units + m.Nano / 1_000_000_000m;
        }


        public async Task<IReadOnlyList<PortfolioItem>> GetPortfolioAsync()
        {
            var accounts = await _client.Users.GetAccountsAsync();
            var account = accounts.Accounts.FirstOrDefault();

            if (account == null)
                throw new Exception("Нет доступных счетов.");

            var accountId = account.Id;

            var portfolio = await _client.Operations.GetPortfolioAsync(new PortfolioRequest
            {
                AccountId = accountId
            });

            var result = new List<PortfolioItem>();

            foreach (var pos in portfolio.Positions)
            {
                var ticker = pos.Ticker;          // SU26243RMFS4
                var uid = pos.InstrumentUid;      // UUID

                // Запрос названия
                var instrument = await _client.Instruments.GetInstrumentByAsync(new InstrumentRequest
                {
                    Id = uid,
                    IdType = InstrumentIdType.Uid
                });

                //decimal nominal = await GetNominalAsync(uid, instrument);

                var name = instrument.Instrument.Name; // ОФЗ 26243

                var qty = ToDecimal(pos.Quantity);

                var avg = pos.AveragePositionPrice != null
                    ? ToDecimal(pos.AveragePositionPrice)
                    : 0;

                var current = pos.CurrentPrice != null
                    ? ToDecimal(pos.CurrentPrice)
                    : avg;
                var nkd = pos.CurrentNkd != null
                    ? ToDecimal(pos.CurrentNkd)
                    : 0;

                var lot = instrument.Instrument.Lot;

                var analytics = await GetBondAsync(uid, instrument);


                result.Add(new PortfolioItem(
                    ticker,
                    name,
                    uid,
                    lot,
                    qty,
                    avg,
                    current,
                    nkd,
                    analytics?.Nominal ?? 1m,
                    analytics?.Coupon ?? 0m, 
                    analytics?.CouponsPerYear ?? 0, 
                    analytics?.CurrentYield ?? 0m, 
                    analytics?.NextCouponDate
                ));

            }

            return result;
        }

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
        private async Task<BondItem> GetBondAsync(string uid, InstrumentResponse inst)
        {
            // Если это не облигация — возвращаем null
            if (!string.Equals(inst.Instrument.InstrumentType, "BOND", StringComparison.OrdinalIgnoreCase))
                return null;

            var call = _client.Instruments.BondByAsync(new InstrumentRequest
            {
                Id = uid,
                IdType = InstrumentIdType.Uid
            });

            var bond = await call.ResponseAsync;

            decimal nominal = ToDecimal(bond.Instrument.Nominal);

            var coupons = await _client.Instruments.GetBondCouponsAsync(new GetBondCouponsRequest
            {
                InstrumentId = uid,
                From = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                To = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(2))
            });

            var nextCoupon = coupons.Events
                .Where(c => c.CouponDate.ToDateTime() > DateTime.Now)
                .OrderBy(c => c.CouponDate.ToDateTime())
                .FirstOrDefault();

            if (nextCoupon == null)
                return null;

            decimal coupon = ToDecimal(nextCoupon.PayOneBond);

            int couponsPerYear = (int)Math.Round(365.0 / nextCoupon.CouponPeriod);

            decimal annualCoupon = coupon * couponsPerYear;
            decimal currentYield = annualCoupon / nominal;

            double yearsToMaturity = (bond.Instrument.MaturityDate.ToDateTime() - DateTime.Now).TotalDays / 365.0;


            return new BondItem
            {
                Nominal = nominal,
                Coupon = coupon,
                CouponsPerYear = couponsPerYear,
                NextCouponDate = nextCoupon.CouponDate.ToDateTime(),
                CurrentYield = currentYield
            };
        }


        public async IAsyncEnumerable<MarketDataResponse> SubscribePricesAsync(IEnumerable<string> uids)
        {
            var stream = _client.MarketDataStream.MarketDataStream();

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

            await stream.RequestStream.WriteAsync(request);

            await foreach (var response in stream.ResponseStream.ReadAllAsync())
            {
                yield return response;
            }
        }





    }
}
