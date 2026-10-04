using Domain;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Legends;
using OxyPlot.Series;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace App.ViewModels
{
    public sealed record ContributionFrequencyOption(InvestmentContributionFrequency Value, string DisplayName);

    public sealed class InvestmentPlanPeriodRowViewModel
    {
        public DateTime StartDate { get; }
        public DateTime EndDate { get; }
        public decimal PlannedAmountRub { get; }
        public decimal ActualAmountRub { get; }
        public decimal DifferenceRub { get; }
        public bool IsFuture { get; }
        public bool IsCurrent { get; }
        public string StatusText { get; }

        public InvestmentPlanPeriodRowViewModel(InvestmentPlanPeriod period)
        {
            StartDate = period.StartDate;
            EndDate = period.EndDate;
            PlannedAmountRub = period.PlannedAmountRub;
            ActualAmountRub = period.ActualAmountRub;
            DifferenceRub = period.DifferenceRub;
            IsFuture = period.IsFuture;
            IsCurrent = period.IsCurrent;
            StatusText = IsFuture
                ? "Ожидается"
                : IsCurrent
                    ? DifferenceRub <= 0 ? "Взнос выполнен" : $"Идёт · не хватает {DifferenceRub:N0} ₽"
                : DifferenceRub <= 0
                    ? "План выполнен"
                    : $"Не хватает {DifferenceRub:N0} ₽";
        }
    }

    public sealed class InvestmentPlanViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IInvestmentPlanRepository _planRepository;
        private readonly IPortfolioService _portfolioService;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private InvestmentPlan _plan = new();
        private InvestmentPlanEvaluation? _evaluation;
        private IReadOnlyList<PortfolioOperation> _actualOperations = Array.Empty<PortfolioOperation>();
        private CancellationTokenSource? _loadCancellation;
        private decimal? _currentPortfolioValueRub;
        private bool _hasSavedPlan;
        private bool _initialCapitalSeeded;
        private string _statusMessage = "Настройте сценарий и сохраните план.";
        private string _projectedValueText = "—";
        private string _projectedWealthText = "—";
        private string _contributionsText = "—";
        private string _paidCouponsText = "—";
        private string _realValueText = "—";
        private string _nextContributionText = "—";
        private string _goalText = "—";
        private bool _isLoading;

        public ObservableCollection<InvestmentPlanPeriodRowViewModel> ContributionPeriods { get; } = new();
        public IReadOnlyList<ContributionFrequencyOption> ContributionFrequencyOptions { get; } = new[]
        {
            new ContributionFrequencyOption(InvestmentContributionFrequency.Weekly, "Еженедельно"),
            new ContributionFrequencyOption(InvestmentContributionFrequency.Biweekly, "Раз в 2 недели"),
            new ContributionFrequencyOption(InvestmentContributionFrequency.Monthly, "Ежемесячно"),
            new ContributionFrequencyOption(InvestmentContributionFrequency.Quarterly, "Ежеквартально")
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand SaveCommand { get; }
        public ICommand RecalculateCommand { get; }
        public ICommand RefreshActualsCommand { get; }

        public PlotModel ProjectionPlotModel { get; private set; } = CreateEmptyPlot();

        public string PlanName
        {
            get => _plan.Name;
            set => SetPlanValue(_plan.Name, value, newValue => _plan.Name = newValue);
        }

        public DateTime StartDate
        {
            get => _plan.StartDate;
            set => SetPlanValue(_plan.StartDate, value.Date, newValue => _plan.StartDate = newValue);
        }

        public int DurationYears
        {
            get => _plan.DurationYears;
            set => SetPlanValue(_plan.DurationYears, value, newValue => _plan.DurationYears = newValue);
        }

        public decimal InitialCapitalRub
        {
            get => _plan.InitialCapitalRub;
            set => SetPlanValue(_plan.InitialCapitalRub, value, newValue => _plan.InitialCapitalRub = newValue);
        }

        public decimal TargetAmountRub
        {
            get => _plan.TargetAmountRub;
            set => SetPlanValue(_plan.TargetAmountRub, value, newValue => _plan.TargetAmountRub = newValue);
        }

        public decimal ContributionAmountRub
        {
            get => _plan.ContributionAmountRub;
            set => SetPlanValue(_plan.ContributionAmountRub, value, newValue => _plan.ContributionAmountRub = newValue);
        }

        public InvestmentContributionFrequency ContributionFrequency
        {
            get => _plan.ContributionFrequency;
            set => SetPlanValue(_plan.ContributionFrequency, value, newValue => _plan.ContributionFrequency = newValue);
        }

        public int ContributionDayOfMonth
        {
            get => _plan.ContributionDayOfMonth;
            set => SetPlanValue(_plan.ContributionDayOfMonth, value, newValue => _plan.ContributionDayOfMonth = newValue);
        }

        public decimal AnnualContributionIncreasePercent
        {
            get => _plan.AnnualContributionIncreasePercent;
            set => SetPlanValue(_plan.AnnualContributionIncreasePercent, value, newValue => _plan.AnnualContributionIncreasePercent = newValue);
        }

        public decimal AnnualPriceGrowthPercent
        {
            get => _plan.AnnualPriceGrowthPercent;
            set => SetPlanValue(_plan.AnnualPriceGrowthPercent, value, newValue => _plan.AnnualPriceGrowthPercent = newValue);
        }

        public decimal AnnualCouponYieldPercent
        {
            get => _plan.AnnualCouponYieldPercent;
            set => SetPlanValue(_plan.AnnualCouponYieldPercent, value, newValue => _plan.AnnualCouponYieldPercent = newValue);
        }

        public bool ReinvestCouponIncome
        {
            get => _plan.ReinvestCouponIncome;
            set => SetPlanValue(_plan.ReinvestCouponIncome, value, newValue => _plan.ReinvestCouponIncome = newValue);
        }

        public decimal CouponTaxPercent
        {
            get => _plan.CouponTaxPercent;
            set => SetPlanValue(_plan.CouponTaxPercent, value, newValue => _plan.CouponTaxPercent = newValue);
        }

        public decimal AnnualFeePercent
        {
            get => _plan.AnnualFeePercent;
            set => SetPlanValue(_plan.AnnualFeePercent, value, newValue => _plan.AnnualFeePercent = newValue);
        }

        public decimal AnnualInflationPercent
        {
            get => _plan.AnnualInflationPercent;
            set => SetPlanValue(_plan.AnnualInflationPercent, value, newValue => _plan.AnnualInflationPercent = newValue);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        public string ProjectedValueText
        {
            get => _projectedValueText;
            private set => SetField(ref _projectedValueText, value);
        }

        public string ProjectedWealthText
        {
            get => _projectedWealthText;
            private set => SetField(ref _projectedWealthText, value);
        }

        public string ContributionsText
        {
            get => _contributionsText;
            private set => SetField(ref _contributionsText, value);
        }

        public string PaidCouponsText
        {
            get => _paidCouponsText;
            private set => SetField(ref _paidCouponsText, value);
        }

        public string RealValueText
        {
            get => _realValueText;
            private set => SetField(ref _realValueText, value);
        }

        public string NextContributionText
        {
            get => _nextContributionText;
            private set => SetField(ref _nextContributionText, value);
        }

        public string GoalText
        {
            get => _goalText;
            private set => SetField(ref _goalText, value);
        }

        public decimal CurrentGoalProgressPercent => _currentPortfolioValueRub.HasValue && _plan.TargetAmountRub > 0
            ? Math.Clamp(_currentPortfolioValueRub.Value / _plan.TargetAmountRub * 100m, 0m, 100m)
            : 0m;

        public string CurrentGoalValueText => _currentPortfolioValueRub.HasValue && _plan.TargetAmountRub > 0
            ? $"Сейчас {_currentPortfolioValueRub.Value:N0} ₽ из {_plan.TargetAmountRub:N0} ₽ · {CurrentGoalProgressPercent:N1}%"
            : "Укажите цель и дождитесь загрузки текущего портфеля";

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetField(ref _isLoading, value);
        }

        public InvestmentPlanViewModel(
            IInvestmentPlanRepository planRepository,
            IPortfolioService portfolioService,
            ILogger logger)
        {
            _planRepository = planRepository;
            _portfolioService = portfolioService;
            _logger = logger.ForContext<InvestmentPlanViewModel>();
            SaveCommand = new RelayCommand(async _ => await SavePlanAsync());
            RecalculateCommand = new RelayCommand(_ => Recalculate());
            RefreshActualsCommand = new RelayCommand(async _ => await RefreshActualContributionsAsync());
            Recalculate();
        }

        public void SetCurrentPortfolioValue(decimal valueRub)
        {
            _currentPortfolioValueRub = valueRub;
            OnPropertyChanged(nameof(CurrentGoalProgressPercent));
            OnPropertyChanged(nameof(CurrentGoalValueText));
            if (!_hasSavedPlan && !_initialCapitalSeeded && _plan.InitialCapitalRub == 0)
            {
                InitialCapitalRub = valueRub;
                _initialCapitalSeeded = true;
            }
        }

        public async Task LoadAsync()
        {
            if (!await _loadLock.WaitAsync(0))
                return;

            try
            {
                IsLoading = true;
                var savedPlan = await _planRepository.GetActivePlanAsync();
                if (savedPlan != null)
                {
                    _plan = savedPlan;
                    _hasSavedPlan = true;
                    _initialCapitalSeeded = true;
                    NotifyPlanProperties();
                    Recalculate();
                    StatusMessage = "Сохранённый план загружен.";
                }
                else if (!_initialCapitalSeeded && _currentPortfolioValueRub.HasValue)
                {
                    InitialCapitalRub = _currentPortfolioValueRub.Value;
                    _initialCapitalSeeded = true;
                }

                await RefreshActualContributionsAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load saved investment plan");
                StatusMessage = "Не удалось загрузить план из локальной базы. Изменения можно рассчитать и попробовать сохранить снова.";
            }
            finally
            {
                IsLoading = false;
                _loadLock.Release();
            }
        }

        public async Task SavePlanAsync()
        {
            try
            {
                if (!Recalculate())
                    return;
                await _planRepository.SaveActivePlanAsync(_plan);
                _hasSavedPlan = true;
                StatusMessage = $"План «{_plan.Name}» сохранён локально.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save investment plan");
                StatusMessage = "Не удалось сохранить план. Проверьте доступность локальной базы данных.";
            }
        }

        public async Task RefreshActualContributionsAsync()
        {
            var previous = _loadCancellation;
            var current = new CancellationTokenSource();
            _loadCancellation = current;
            previous?.Cancel();
            IsLoading = true;
            StatusMessage = "Сверяем взносы плана с операциями счёта...";

            try
            {
                _actualOperations = await _portfolioService.GetOperationsAsync(
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UtcNow,
                    current.Token);
                current.Token.ThrowIfCancellationRequested();
                Recalculate();
                StatusMessage = $"Фактические пополнения синхронизированы: {_actualOperations.Count(operation => operation.Kind == PortfolioOperationKind.Deposit)} операций.";
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not synchronize actual plan contributions");
                Recalculate();
                StatusMessage = "План рассчитан, но API не удалось сверить с фактическими пополнениями. Проверьте подключение.";
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, current))
                {
                    _loadCancellation = null;
                    IsLoading = false;
                }
                current.Dispose();
            }
        }

        private bool Recalculate()
        {
            try
            {
                _evaluation = InvestmentPlanCalculator.Calculate(_plan, _actualOperations, DateTime.Today);
                ContributionPeriods.Clear();
                foreach (var period in _evaluation.ContributionPeriods)
                    ContributionPeriods.Add(new InvestmentPlanPeriodRowViewModel(period));

                ProjectedValueText = $"{_evaluation.FinalPortfolioValueRub:N0} ₽";
                ProjectedWealthText = $"{_evaluation.FinalTotalWealthRub:N0} ₽";
                ContributionsText = $"{_evaluation.TotalContributionsRub:N0} ₽";
                PaidCouponsText = $"{_evaluation.FinalPaidCouponsRub:N0} ₽";
                RealValueText = $"{_evaluation.FinalRealWealthRub:N0} ₽";
                NextContributionText = _evaluation.NextContributionDate.HasValue
                    ? $"{_evaluation.NextContributionDate.Value:dd.MM.yyyy} · {_evaluation.NextContributionAmountRub:N0} ₽"
                    : "Взносы завершены";
                GoalText = BuildGoalText(_evaluation, _plan.TargetAmountRub);
                ProjectionPlotModel = CreateProjectionPlot(_evaluation, _plan.TargetAmountRub);
                OnPropertyChanged(nameof(ProjectionPlotModel));
                StatusMessage = _hasSavedPlan ? "Расчёт обновлён. Сохраните изменения, чтобы обновить план на диске." : "Черновик рассчитан. Сохраните план, чтобы он не потерялся.";
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Investment plan parameters are not valid");
                _evaluation = null;
                ContributionPeriods.Clear();
                ProjectedValueText = "—";
                ProjectedWealthText = "—";
                ContributionsText = "—";
                PaidCouponsText = "—";
                RealValueText = "—";
                NextContributionText = "—";
                GoalText = "Проверьте параметры плана.";
                ProjectionPlotModel = CreateEmptyPlot("Исправьте некорректные параметры плана");
                OnPropertyChanged(nameof(ProjectionPlotModel));
                StatusMessage = ex.Message;
                return false;
            }
        }

        private void SetPlanValue<T>(T current, T value, Action<T> setter, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
                return;
            setter(value);
            OnPropertyChanged(propertyName);
            if (propertyName == nameof(TargetAmountRub))
            {
                OnPropertyChanged(nameof(CurrentGoalProgressPercent));
                OnPropertyChanged(nameof(CurrentGoalValueText));
            }
            Recalculate();
        }

        private void NotifyPlanProperties()
        {
            OnPropertyChanged(nameof(PlanName));
            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(DurationYears));
            OnPropertyChanged(nameof(InitialCapitalRub));
            OnPropertyChanged(nameof(TargetAmountRub));
            OnPropertyChanged(nameof(ContributionAmountRub));
            OnPropertyChanged(nameof(ContributionFrequency));
            OnPropertyChanged(nameof(ContributionDayOfMonth));
            OnPropertyChanged(nameof(AnnualContributionIncreasePercent));
            OnPropertyChanged(nameof(AnnualPriceGrowthPercent));
            OnPropertyChanged(nameof(AnnualCouponYieldPercent));
            OnPropertyChanged(nameof(ReinvestCouponIncome));
            OnPropertyChanged(nameof(CouponTaxPercent));
            OnPropertyChanged(nameof(AnnualFeePercent));
            OnPropertyChanged(nameof(AnnualInflationPercent));
        }

        private static string BuildGoalText(InvestmentPlanEvaluation evaluation, decimal targetAmountRub)
        {
            if (targetAmountRub <= 0m)
                return "Целевая сумма не задана.";
            if (evaluation.TargetReachedDate.HasValue)
                return $"Цель достигнута ориентировочно {evaluation.TargetReachedDate.Value:MMMM yyyy}";
            return "Цель не достигается на выбранном горизонте: проверьте взносы и сценарий доходности.";
        }

        private static PlotModel CreateProjectionPlot(InvestmentPlanEvaluation evaluation, decimal targetAmount)
        {
            var model = new PlotModel
            {
                Title = "Сценарий капитала",
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColor.FromRgb(43, 57, 52),
                PlotAreaBorderColor = OxyColor.FromRgb(220, 227, 222)
            };
            model.Axes.Add(new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "MMM yyyy",
                IntervalType = DateTimeIntervalType.Auto,
                AxislineColor = OxyColor.FromRgb(220, 227, 222)
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Капитал, ₽",
                StringFormat = "#,##0",
                MinimumPadding = 0.04,
                MaximumPadding = 0.1,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(232, 237, 233)
            });

            var portfolioSeries = new LineSeries
            {
                Title = "Портфель",
                Color = OxyColor.FromRgb(35, 116, 91),
                StrokeThickness = 2.5,
                TrackerFormatString = "{2:dd.MM.yyyy}\nПортфель: {4:N0} ₽"
            };
            var totalWealthSeries = new LineSeries
            {
                Title = "Вместе с выплаченными купонами",
                Color = OxyColor.FromRgb(68, 125, 155),
                StrokeThickness = 1.5,
                LineStyle = LineStyle.Dash,
                TrackerFormatString = "{2:dd.MM.yyyy}\nКапитал: {4:N0} ₽"
            };
            var contributionsSeries = new LineSeries
            {
                Title = "Внесено",
                Color = OxyColor.FromRgb(197, 138, 66),
                StrokeThickness = 1.5,
                LineStyle = LineStyle.Dot
            };

            foreach (var point in evaluation.Points)
            {
                var date = DateTimeAxis.ToDouble(point.Date);
                portfolioSeries.Points.Add(new DataPoint(date, (double)point.PortfolioValueRub));
                totalWealthSeries.Points.Add(new DataPoint(date, (double)point.TotalWealthRub));
                contributionsSeries.Points.Add(new DataPoint(date, (double)(point.ContributionsRub + 0m)));
            }

            model.Series.Add(portfolioSeries);
            model.Series.Add(totalWealthSeries);
            model.Series.Add(contributionsSeries);
            if (targetAmount > 0)
            {
                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = (double)targetAmount,
                    Color = OxyColor.FromRgb(186, 70, 62),
                    LineStyle = LineStyle.Dash,
                    Text = "Цель",
                    TextColor = OxyColor.FromRgb(186, 70, 62)
                });
            }

            model.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Horizontal
            });
            return model;
        }

        private static PlotModel CreateEmptyPlot(string message = "Рассчитайте план, чтобы увидеть прогноз")
        {
            var model = new PlotModel { Background = OxyColors.White, PlotAreaBackground = OxyColors.White };
            model.Annotations.Add(new TextAnnotation
            {
                Text = message,
                Stroke = OxyColors.Transparent,
                TextPosition = new DataPoint(0.5, 0.5),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
            });
            return model;
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            _loadCancellation?.Cancel();
        }
    }
}
