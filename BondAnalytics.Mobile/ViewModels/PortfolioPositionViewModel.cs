using Domain;
using Microsoft.Maui.Graphics;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BondAnalytics.Mobile.ViewModels;

public sealed class PortfolioPositionViewModel : INotifyPropertyChanged
{
    private decimal _currentPrice;

    public PortfolioItem Item { get; }
    public string Ticker => Item.Ticker;
    public string Name => Item.Name;
    public string Group => Item.InstrumentType.Equals("bond", StringComparison.OrdinalIgnoreCase)
        ? Item.Ticker.StartsWith("SU", StringComparison.OrdinalIgnoreCase) ? "ОФЗ" : "Корпоративные"
        : "Фонды и прочее";
    public decimal Quantity => Item.Quantity;
    public decimal AveragePrice => Item.AveragePrice;
    public decimal CurrentPrice => _currentPrice;
    public decimal CurrentYield => Coupon > 0 && CouponsPerYear > 0
        ? CouponYieldCalculator.CalculateAnnualYield(Coupon * CouponsPerYear, CurrentPrice)
        : Item.CurrentYield;
    public decimal ReinvestedYield =>
        CouponYieldCalculator.CalculateReinvestedAnnualYield(CurrentYield, CouponsPerYear);
    public decimal Coupon => Item.Coupon;
    public int CouponsPerYear => Item.CouponsPerYear;
    public decimal TotalValue => Quantity * CurrentPrice;
    public decimal TotalAccruedInterest => Quantity * Item.AccruedInterestPerBond;
    public decimal FullValue => TotalValue + TotalAccruedInterest;
    public decimal Profit => (CurrentPrice - AveragePrice) * Quantity;
    public decimal ProfitPercent => AveragePrice > 0 ? Profit / (AveragePrice * Quantity) : 0m;
    public Color ProfitColor => Profit < 0
        ? Color.FromArgb("#D14343")
        : Profit > 0 ? Color.FromArgb("#16805D") : Color.FromArgb("#687786");
    public string Uid => Item.Uid;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PortfolioPositionViewModel(PortfolioItem item)
    {
        Item = item;
        _currentPrice = item.CurrentPrice;
    }

    public bool UpdatePrice(decimal price)
    {
        if (_currentPrice == price)
            return false;

        _currentPrice = price;
        OnPropertyChanged(nameof(CurrentPrice));
        OnPropertyChanged(nameof(TotalValue));
        OnPropertyChanged(nameof(FullValue));
        OnPropertyChanged(nameof(CurrentYield));
        OnPropertyChanged(nameof(ReinvestedYield));
        OnPropertyChanged(nameof(Profit));
        OnPropertyChanged(nameof(ProfitPercent));
        OnPropertyChanged(nameof(ProfitColor));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ChartPoint(
    string Label,
    decimal Value,
    string? Detail = null,
    bool IsPercent = false,
    string? Ticker = null,
    decimal? Quantity = null,
    decimal? InstrumentValue = null)
{
    public string ValueText => IsPercent
        ? Value.ToString("P2", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
        : Value.ToString("#,0 ₽", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}

public sealed record ChartSeries(
    string Name,
    Color Color,
    IReadOnlyList<decimal> Values,
    IReadOnlyList<ChartPoint?>? Details = null);
