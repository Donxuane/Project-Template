using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;
using TradingBot.Percistance.Repositories;
using Xunit;

namespace TradingBot.Application.Tests;

public class BalanceRepositoryHistoryLogicTests
{
    [Fact]
    public void ShouldInsertHistory_FirstSync_Inserts()
    {
        var incoming = CreateSnapshot(1.0m, 0.0m);

        var shouldInsert = BalanceRepository.ShouldInsertHistory(
            existingLatest: null,
            incoming,
            hasHistory: false,
            forceSnapshot: false);

        Assert.True(shouldInsert);
    }

    [Fact]
    public void ShouldInsertHistory_ChangedBalance_Inserts()
    {
        var existing = CreateSnapshot(1.0m, 0.1m);
        var incoming = CreateSnapshot(1.2m, 0.1m);

        var shouldInsert = BalanceRepository.ShouldInsertHistory(
            existing,
            incoming,
            hasHistory: true,
            forceSnapshot: false);

        Assert.True(shouldInsert);
    }

    [Fact]
    public void ShouldInsertHistory_UnchangedBalance_Skips()
    {
        var existing = CreateSnapshot(1.0m, 0.1m);
        var incoming = CreateSnapshot(1.0m, 0.1m);

        var shouldInsert = BalanceRepository.ShouldInsertHistory(
            existing,
            incoming,
            hasHistory: true,
            forceSnapshot: false);

        Assert.False(shouldInsert);
    }

    [Fact]
    public void ShouldInsertHistory_ForceSnapshot_Inserts()
    {
        var existing = CreateSnapshot(1.0m, 0.1m);
        var incoming = CreateSnapshot(1.0m, 0.1m);

        var shouldInsert = BalanceRepository.ShouldInsertHistory(
            existing,
            incoming,
            hasHistory: true,
            forceSnapshot: true);

        Assert.True(shouldInsert);
    }

    [Fact]
    public void IsSnapshotStale_UsesCoalescedUpdatedAtOrCreatedAt()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var stale = CreateSnapshot(1.0m, 0m);
        stale.UpdatedAt = DateTime.UtcNow.AddMinutes(-30);

        var fresh = CreateSnapshot(1.0m, 0m);
        fresh.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);

        var recentCreatedWithMissingUpdatedAt = CreateSnapshot(1.0m, 0m);
        recentCreatedWithMissingUpdatedAt.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        recentCreatedWithMissingUpdatedAt.UpdatedAt = default;

        var oldCreatedWithMissingUpdatedAt = CreateSnapshot(1.0m, 0m);
        oldCreatedWithMissingUpdatedAt.CreatedAt = DateTime.UtcNow.AddMinutes(-40);
        oldCreatedWithMissingUpdatedAt.UpdatedAt = default;

        Assert.True(BalanceRepository.IsSnapshotStale(stale, cutoff));
        Assert.False(BalanceRepository.IsSnapshotStale(fresh, cutoff));
        Assert.False(BalanceRepository.IsSnapshotStale(recentCreatedWithMissingUpdatedAt, cutoff));
        Assert.True(BalanceRepository.IsSnapshotStale(oldCreatedWithMissingUpdatedAt, cutoff));
    }

    [Fact]
    public void BalanceRepositoryInterface_ExposesAssetIdApi_AndKeepsLegacyWrapperObsolete()
    {
        var methods = typeof(IBalanceRepository).GetMethods();
        var assetIdApi = methods.FirstOrDefault(m =>
            m.Name == "GetLatestByAssetAsync"
            && m.GetParameters().Length >= 2
            && m.GetParameters()[1].ParameterType == typeof(Assets));

        Assert.NotNull(assetIdApi);

        var legacyApi = methods.FirstOrDefault(m =>
            m.Name == "GetLatestAsync"
            && m.GetParameters().Length >= 2
            && m.GetParameters()[1].ParameterType == typeof(TradingSymbol));

        Assert.NotNull(legacyApi);
        Assert.NotNull(legacyApi!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
    }

    [Fact]
    public void BalanceSnapshot_LegacySymbolAlias_MapsToAssetId()
    {
        var snapshot = new BalanceSnapshot
        {
            AssetId = Assets.BNB
        };

#pragma warning disable CS0618
        Assert.Equal(Assets.BNB, snapshot.Symbol);
        snapshot.Symbol = Assets.USDT;
#pragma warning restore CS0618

        Assert.Equal(Assets.USDT, snapshot.AssetId);
    }

    [Fact]
    public async Task BeginTransactionWithOpenConnectionAsync_OpensClosedConnectionBeforeBeginningTransaction()
    {
        var fakeConnection = new FakeDbConnection();
        var repository = new BalanceRepository(fakeConnection);
        var method = typeof(BalanceRepository).GetMethod(
            "BeginTransactionWithOpenConnectionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task<IDbTransaction>)method!.Invoke(repository, [CancellationToken.None])!;
        await using var transaction = (DbTransaction)await task;

        Assert.True(fakeConnection.OpenAsyncCalled);
        Assert.True(fakeConnection.BeginTransactionAsyncCalled);
        Assert.Equal(ConnectionState.Open, fakeConnection.State);
        Assert.NotNull(transaction);
    }

    private static BalanceSnapshot CreateSnapshot(decimal free, decimal locked)
    {
        return new BalanceSnapshot
        {
            Asset = "BNB",
            AssetId = Assets.BNB,
            Side = OrderSide.BUY,
            Free = free,
            Locked = locked,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private string connectionString = string.Empty;
        private ConnectionState state = ConnectionState.Closed;

        public bool OpenAsyncCalled { get; private set; }
        public bool BeginTransactionAsyncCalled { get; private set; }

        [AllowNull]
        public override string ConnectionString
        {
            get => connectionString;
            set => connectionString = value ?? string.Empty;
        }
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close()
        {
            state = ConnectionState.Closed;
        }

        public override void Open()
        {
            state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            OpenAsyncCalled = true;
            state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginTransactionAsyncCalled = true;
            return new FakeDbTransaction(this);
        }

        protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            BeginTransactionAsyncCalled = true;
            return ValueTask.FromResult<DbTransaction>(new FakeDbTransaction(this));
        }

        protected override DbCommand CreateDbCommand()
            => throw new NotSupportedException();
    }

    private sealed class FakeDbTransaction(FakeDbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }
}
