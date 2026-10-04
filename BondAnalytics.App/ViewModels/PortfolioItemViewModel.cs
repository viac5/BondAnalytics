using Domain;
using System;
using System.ComponentModel;

public class PortfolioItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Uid { get; }

    private decimal _currentPrice;
    public decimal CurrentPrice
    {
        get => _currentPrice;
        set
        {
            if (_currentPrice == value)
                return;

            _currentPrice = value;

            OnPropertyChanged(nameof(CurrentPrice));
            OnPropertyChanged(nameof(Profit));
            OnPropertyChanged(nameof(TotalValue));
            OnPropertyChanged(nameof(FullValue));
            OnPropertyChanged(nameof(CurrentYield));
            OnPropertyChanged(nameof(IsProfitPositive));
            OnPropertyChanged(nameof(IsProfitNegative));
        }
    }

    public decimal AveragePrice { get; }
    public decimal Quantity { get; }
    public decimal AccruedInterestPerBond { get; }

    public decimal TotalValue => Quantity * CurrentPrice;
    public decimal TotalAccruedInterest => Quantity * AccruedInterestPerBond;
    public decimal FullValue => TotalValue + TotalAccruedInterest;

    public decimal Profit => (CurrentPrice - AveragePrice) * Quantity;

    public bool IsProfitPositive => Profit > 0;
    public bool IsProfitNegative => Profit < 0;

    public string Group => !IsBond ? "Фонды и прочее" : Ticker.StartsWith("SU") ? "ОФЗ" : "Корпоративные";

    public string Ticker { get; }
    public string Name { get; }
    public string InstrumentType { get; }
    public bool IsBond => string.Equals(InstrumentType, "bond", StringComparison.OrdinalIgnoreCase);
    public int Lot { get; }
    public decimal Nominal { get; }

    public decimal Coupon { get; }
    public int CouponsPerYear { get; }
    public DateTime? NextCouponDate { get; }

    public decimal CurrentYield => !IsBond || CurrentPrice == 0 ? 0 : (Coupon * CouponsPerYear) / CurrentPrice;

    public PortfolioItemViewModel(PortfolioItem item)
    {
        Ticker = item.Ticker;
        Name = item.Name;
        InstrumentType = item.InstrumentType;
        Uid = item.Uid;
        Lot = item.Lot;
        Nominal = item.Nominal;
        Quantity = item.Quantity;
        AveragePrice = item.AveragePrice;
        AccruedInterestPerBond = item.AccruedInterestPerBond;

        _currentPrice = item.CurrentPrice;

        Coupon = item.Coupon;
        CouponsPerYear = item.CouponsPerYear;
        NextCouponDate = item.NextCouponDate;
    }
}
