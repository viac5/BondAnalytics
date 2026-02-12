using Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Tinkoff.InvestApi.V1;
using App.Views;

namespace App.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IPortfolioService _portfolioService;

        private List<PortfolioItemViewModel> _allItems = new();
        private bool _streamStarted;


        public ObservableCollection<PortfolioItemViewModel> Portfolio { get; }
            = new ObservableCollection<PortfolioItemViewModel>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        


        public ICommand RefreshCommand { get; }
        public ICommand ShowChartsCommand { get; }


        public MainViewModel(IPortfolioService portfolioService)
        {
            _portfolioService = portfolioService;

            RefreshCommand = new RelayCommand(async _ => await LoadPortfolio());
            ShowChartsCommand = new RelayCommand(_ => ShowCharts());

            _ = LoadPortfolio();

        }

        // Итоговая сумма
        public decimal TotalPortfolioValue => Portfolio.Sum(x => x.TotalValue);
        public decimal TotalNkd => Portfolio.Sum(x => x.TotalAccruedInterest);
        public decimal TotalFullValue => Portfolio.Sum(x => x.FullValue);



        // Фильтры
        public List<string> Filters { get; } = new() { "Все", "ОФЗ", "Корпоративные" };

        private string _selectedFilter = "Все";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter == value)
                    return;

                _selectedFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        private void ShowCharts()
        {
            var wnd = new ChartsWindow
            {
                DataContext = new ChartsViewModel(_allItems)
            };
            wnd.Show();
        }


        private void ApplyFilter()
        {
            IEnumerable<PortfolioItemViewModel> items = _allItems;

            if (SelectedFilter == "ОФЗ")
                items = items.Where(x => x.Group == "ОФЗ");

            if (SelectedFilter == "Корпоративные")
                items = items.Where(x => x.Group == "Корпоративные");

            Portfolio.Clear();
            foreach (var i in items)
                Portfolio.Add(i);

            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalNkd));
            OnPropertyChanged(nameof(TotalFullValue));

        }


        private async Task LoadPortfolio()
        {
            try
            {
                var items = await _portfolioService.GetPortfolioAsync();

                _allItems = items
                    .Select(x => new PortfolioItemViewModel(x))
                    .ToList();

                ApplyFilter();

                if (!_streamStarted)
                {
                    _streamStarted = true;
                    _ = StartPriceStream();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("ERROR:\n" + ex.ToString());
            }
        }

        private void UpdatePrice(LastPrice last)
        {
            var item = Portfolio.FirstOrDefault(x => x.Uid == last.InstrumentUid);
            if (item == null)
                return;

            // Цена в процентах
            var q = last.Price;
            var percent = q.Units + q.Nano / 1_000_000_000m;

            // Цена одной облигации в рублях
            var nominal = item.Nominal; 
            var pricePerBond = (percent / 100m) * nominal;

            // Цена за лот
            var newPrice = pricePerBond * item.Lot;

            item.CurrentPrice = newPrice;

            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalFullValue));
        }





        private async Task StartPriceStream()
        {
            var uids = _allItems.Select(x => x.Uid).ToList();

            await foreach (var msg in _portfolioService.SubscribePricesAsync(uids))
            {
                if (msg.LastPrice != null)
                {
                    UpdatePrice(msg.LastPrice);
                }
            }
        }

    }
}
