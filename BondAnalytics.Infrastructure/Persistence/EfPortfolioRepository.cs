using Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;

namespace Infrastructure
{
    public sealed class PortfolioAnalyticsDbContext : DbContext
    {
        public DbSet<PortfolioSnapshotEntity> PortfolioSnapshots => Set<PortfolioSnapshotEntity>();
        public DbSet<InvestmentPlanEntity> InvestmentPlans => Set<InvestmentPlanEntity>();
        public DbSet<CachedPortfolioEntity> CachedPortfolios => Set<CachedPortfolioEntity>();

        public PortfolioAnalyticsDbContext(DbContextOptions<PortfolioAnalyticsDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PortfolioSnapshotEntity>(entity =>
            {
                entity.ToTable("portfolio_snapshots");
                entity.HasKey(snapshot => snapshot.Id);
                entity.Property(snapshot => snapshot.Id).HasColumnName("id");
                entity.Property(snapshot => snapshot.CapturedAtUnixMs).HasColumnName("captured_at_unix_ms");
                entity.Property(snapshot => snapshot.TotalValueRub)
                    .HasColumnName("total_value_rub")
                    .HasColumnType("TEXT");
                entity.HasIndex(snapshot => snapshot.CapturedAtUnixMs)
                    .HasDatabaseName("ix_portfolio_snapshots_captured_at");
            });

            modelBuilder.Entity<InvestmentPlanEntity>(entity =>
            {
                entity.ToTable("investment_plans");
                entity.HasKey(plan => plan.PlanId);
                entity.Property(plan => plan.PlanId).HasColumnName("plan_id");
                entity.Property(plan => plan.PayloadJson).HasColumnName("payload_json");
                entity.Property(plan => plan.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
            });

            modelBuilder.Entity<CachedPortfolioEntity>(entity =>
            {
                entity.ToTable("cached_portfolio");
                entity.HasKey(cache => cache.Id);
                entity.Property(cache => cache.Id).HasColumnName("id");
                entity.Property(cache => cache.PayloadJson).HasColumnName("payload_json");
                entity.Property(cache => cache.CapturedAtUnixMs).HasColumnName("captured_at_unix_ms");
            });
        }
    }

    public sealed class PortfolioSnapshotEntity
    {
        public long Id { get; set; }
        public long CapturedAtUnixMs { get; set; }
        public decimal TotalValueRub { get; set; }
    }

    public sealed class InvestmentPlanEntity
    {
        public int PlanId { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public long UpdatedAtUnixMs { get; set; }
    }

    public sealed class CachedPortfolioEntity
    {
        public int Id { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public long CapturedAtUnixMs { get; set; }
    }

    public sealed class EfPortfolioRepository : IPortfolioHistoryRepository, IInvestmentPlanRepository
    {
        private readonly IDbContextFactory<PortfolioAnalyticsDbContext> _contextFactory;
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private bool _initialized;

        public EfPortfolioRepository()
            : this(GetDefaultDatabasePath())
        {
        }

        public EfPortfolioRepository(string databasePath)
        {
            var fullDatabasePath = Path.GetFullPath(databasePath);
            var directory = Path.GetDirectoryName(fullDatabasePath)
                ?? throw new ArgumentException("Путь базы данных должен содержать каталог.", nameof(databasePath));
            Directory.CreateDirectory(directory);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = fullDatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<PortfolioAnalyticsDbContext>()
                .UseSqlite(connectionString)
                .Options;
            _contextFactory = new PooledDbContextFactory<PortfolioAnalyticsDbContext>(options);
        }

        public async Task SaveSnapshotAsync(PortfolioSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            context.PortfolioSnapshots.Add(new PortfolioSnapshotEntity
            {
                CapturedAtUnixMs = snapshot.CapturedAt.ToUnixTimeMilliseconds(),
                TotalValueRub = snapshot.TotalValueRub
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<PortfolioSnapshot>> GetSnapshotsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            if (from > to)
                throw new ArgumentException("Начало периода позже его окончания.", nameof(from));

            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var fromUnixMs = from.ToUnixTimeMilliseconds();
            var toUnixMs = to.ToUnixTimeMilliseconds();
            var snapshots = new List<PortfolioSnapshot>();

            var baseline = await context.PortfolioSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.CapturedAtUnixMs < fromUnixMs)
                .OrderByDescending(snapshot => snapshot.CapturedAtUnixMs)
                .FirstOrDefaultAsync(cancellationToken);
            if (baseline != null)
                snapshots.Add(ToSnapshot(baseline));

            var range = await context.PortfolioSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.CapturedAtUnixMs >= fromUnixMs && snapshot.CapturedAtUnixMs <= toUnixMs)
                .OrderBy(snapshot => snapshot.CapturedAtUnixMs)
                .ToListAsync(cancellationToken);
            snapshots.AddRange(range.Select(ToSnapshot));
            return snapshots;
        }

        public async Task SaveCachedPortfolioAsync(
            CachedPortfolioData portfolio,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(portfolio);
            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var cache = await context.CachedPortfolios.SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
            if (cache is null)
            {
                cache = new CachedPortfolioEntity { Id = 1 };
                context.CachedPortfolios.Add(cache);
            }

            cache.PayloadJson = await Task.Run(
                () => JsonSerializer.Serialize(PortfolioCachePayload.From(portfolio.Portfolio)),
                cancellationToken);
            cache.CapturedAtUnixMs = portfolio.CapturedAt.ToUnixTimeMilliseconds();
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<CachedPortfolioData?> GetCachedPortfolioAsync(
            CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var cache = await context.CachedPortfolios
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
            if (cache is null)
                return null;

            var payload = await Task.Run(
                () => JsonSerializer.Deserialize<PortfolioCachePayload>(cache.PayloadJson)
                    ?? throw new InvalidDataException("Сохранённый кэш портфеля имеет неверный формат."),
                cancellationToken);
            return new CachedPortfolioData(
                payload.ToPortfolio(),
                DateTimeOffset.FromUnixTimeMilliseconds(cache.CapturedAtUnixMs));
        }

        public async Task<InvestmentPlan?> GetActivePlanAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var record = await context.InvestmentPlans
                .AsNoTracking()
                .SingleOrDefaultAsync(plan => plan.PlanId == 1, cancellationToken);
            return record == null ? null : JsonSerializer.Deserialize<InvestmentPlan>(record.PayloadJson);
        }

        public async Task SaveActivePlanAsync(InvestmentPlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            await EnsureInitializedAsync(cancellationToken);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var record = await context.InvestmentPlans.SingleOrDefaultAsync(item => item.PlanId == 1, cancellationToken);
            if (record == null)
            {
                context.InvestmentPlans.Add(new InvestmentPlanEntity
                {
                    PlanId = 1,
                    PayloadJson = JsonSerializer.Serialize(plan),
                    UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            else
            {
                record.PayloadJson = JsonSerializer.Serialize(plan);
                record.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
                return;

            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (_initialized)
                    return;

                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                await context.Database.EnsureCreatedAsync(cancellationToken);
                await context.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE IF NOT EXISTS cached_portfolio (id INTEGER NOT NULL PRIMARY KEY, payload_json TEXT NOT NULL, captured_at_unix_ms INTEGER NOT NULL);",
                    cancellationToken);
                _initialized = true;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private static PortfolioSnapshot ToSnapshot(PortfolioSnapshotEntity entity) => new(
            DateTimeOffset.FromUnixTimeMilliseconds(entity.CapturedAtUnixMs),
            entity.TotalValueRub);

        private static string GetDefaultDatabasePath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BondAnalytics",
            "portfolio-history.db");

        private sealed record PortfolioCachePayload(decimal TotalValueRub, IReadOnlyList<PositionCachePayload> Positions)
        {
            public static PortfolioCachePayload From(PortfolioData portfolio) => new(
                portfolio.TotalValueRub,
                portfolio.Positions.Select(PositionCachePayload.From).ToList());

            public PortfolioData ToPortfolio() => new(
                Positions.Select(position => position.ToPortfolioItem()).ToList(),
                TotalValueRub);
        }

        private sealed record PositionCachePayload(
            string Ticker,
            string Name,
            string Uid,
            string InstrumentType,
            int Lot,
            decimal Quantity,
            decimal AveragePrice,
            decimal CurrentPrice,
            decimal AccruedInterestPerBond,
            decimal Nominal,
            decimal Coupon,
            int CouponsPerYear,
            decimal CurrentYield,
            DateTime? NextCouponDate)
        {
            public static PositionCachePayload From(PortfolioItem item) => new(
                item.Ticker, item.Name, item.Uid, item.InstrumentType, item.Lot, item.Quantity,
                item.AveragePrice, item.CurrentPrice, item.AccruedInterestPerBond, item.Nominal,
                item.Coupon, item.CouponsPerYear, item.CurrentYield, item.NextCouponDate);

            public PortfolioItem ToPortfolioItem() => new(
                Ticker, Name, Uid, InstrumentType, Lot, Quantity, AveragePrice, CurrentPrice,
                AccruedInterestPerBond, Nominal, Coupon, CouponsPerYear, CurrentYield, NextCouponDate);
        }
    }
}
