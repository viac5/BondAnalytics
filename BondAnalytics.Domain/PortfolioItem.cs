namespace Domain
{
    public class PortfolioItem
    {
        public string Ticker { get; }
        public string Name { get; }
        public string Uid { get; }
        public int Lot { get; }
        public decimal Quantity { get; }
        public decimal AveragePrice { get; }
        public decimal CurrentPrice { get; }
        public decimal AccruedInterest { get; }
        public decimal Nominal { get; }
        public decimal Coupon { get; }
        public int CouponsPerYear { get; }
        public DateTime? NextCouponDate { get; }
        public decimal CurrentYield { get; }

        public PortfolioItem(
            string ticker,
            string name,
            string uid,
            int lot,
            decimal quantity,
            decimal averagePrice,
            decimal currentPrice,
            decimal accruedInterest,
            decimal nominal,
            decimal coupon,
            int couponsPerYear,
            decimal currentYield,
            DateTime? nextCouponDate
        )
        {
            Ticker = ticker;
            Name = name;
            Uid = uid;
            Lot = lot;
            Quantity = quantity;
            AveragePrice = averagePrice;
            CurrentPrice = currentPrice;
            AccruedInterest = accruedInterest;

            Nominal = nominal;
            Coupon = coupon;
            CouponsPerYear = couponsPerYear;
            CurrentYield = currentYield;
            NextCouponDate = nextCouponDate;
        }
    }
}
