using System;

namespace Domain
{
    public enum PortfolioOperationKind
    {
        Deposit,
        Withdrawal,
        Trade,
        Income,
        Other
    }

    public enum PortfolioTradeDirection
    {
        None,
        Buy,
        Sell
    }

    public sealed class PortfolioOperation
    {
        public string Id { get; }
        public DateTimeOffset Date { get; }
        public PortfolioOperationKind Kind { get; }
        public PortfolioTradeDirection TradeDirection { get; }
        public string TypeName { get; }
        public string InstrumentUid { get; }
        public bool IsBond { get; }
        public string InstrumentName { get; }
        public decimal Quantity { get; }
        public decimal Price { get; }
        public decimal Amount { get; }
        public string Currency { get; }

        public PortfolioOperation(
            string id,
            DateTimeOffset date,
            PortfolioOperationKind kind,
            PortfolioTradeDirection tradeDirection,
            string typeName,
            string instrumentName,
            string instrumentUid,
            bool isBond,
            decimal quantity,
            decimal price,
            decimal amount,
            string currency)
        {
            Id = id;
            Date = date;
            Kind = kind;
            TradeDirection = tradeDirection;
            TypeName = typeName;
            InstrumentName = instrumentName;
            InstrumentUid = instrumentUid;
            IsBond = isBond;
            Quantity = quantity;
            Price = price;
            Amount = amount;
            Currency = currency;
        }
    }
}