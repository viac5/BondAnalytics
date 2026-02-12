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
            //OnPropertyChanged(nameof(CurrentYield));   // доходность меняется при изменении цены
        }
    }

    public decimal AveragePrice { get; }
    public decimal Quantity { get; }
    public decimal AccruedInterest { get; }

    public decimal TotalValue => Quantity * CurrentPrice;
    public decimal TotalAccruedInterest => Quantity * AccruedInterest;
    public decimal FullValue => TotalValue + TotalAccruedInterest;

    public decimal Profit => (CurrentPrice - AveragePrice) * Quantity;

    public bool IsProfitPositive => Profit > 0;
    public bool IsProfitNegative => Profit < 0;

    public string Group => Ticker.StartsWith("SU") ? "ОФЗ" : "Корпоративные";

    public string Ticker { get; }
    public string Name { get; }
    public int Lot { get; }
    public decimal Nominal { get; }

    public decimal Coupon { get; }
    public int CouponsPerYear { get; }
    public DateTime? NextCouponDate { get; }

    public decimal CurrentYield => Nominal == 0 ? 0 : (Coupon * CouponsPerYear) / Nominal;

    public PortfolioItemViewModel(PortfolioItem item)
    {
        Ticker = item.Ticker;
        Name = item.Name;
        Uid = item.Uid;
        Lot = item.Lot;
        Nominal = item.Nominal;
        Quantity = item.Quantity;
        AveragePrice = item.AveragePrice;
        AccruedInterest = item.AccruedInterest;

        _currentPrice = item.CurrentPrice;

        Coupon = item.Coupon;
        CouponsPerYear = item.CouponsPerYear;
        NextCouponDate = item.NextCouponDate;
    }
}
