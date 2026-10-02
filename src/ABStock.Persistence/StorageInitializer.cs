using System.Data.Common;
using ABStock.Application.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ABStock.Persistence;

internal sealed class StorageInitializer(IDbContextFactory<AbStockDbContext> contextFactory)
{
    private const int CurrentVersion = 2;
    private readonly object _sync = new();
    private bool _initialized;

    public void EnsureInitialized()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            using var db = contextFactory.CreateDbContext();
            db.Database.EnsureCreated();
            db.Database.OpenConnection();
            using var transaction = db.Database.BeginTransaction();
            var tables = ReadTables(db);
            if (!new[] { "SimulationRuns", "MarketTicks", "Trades" }.All(tables.Contains))
            {
                throw new InvalidOperationException("The database does not contain a supported ABStock history schema.");
            }

            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "StorageSchemaVersions" (
                    "Version" INTEGER NOT NULL PRIMARY KEY,
                    "AppliedAt" TEXT NOT NULL
                );
                """);
            using var command = CreateCommand(db, """SELECT COALESCE(MAX("Version"), 0) FROM "StorageSchemaVersions";""");
            var version = Convert.ToInt32(command.ExecuteScalar());
            if (version > CurrentVersion)
            {
                throw new InvalidOperationException("This database was updated by a newer ABStock version.");
            }

            if (version == 0)
            {
                // EnsureCreated does not add tables to an existing database; the legacy upgrade is additive.
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "Assets" (
                        "Id" TEXT NOT NULL PRIMARY KEY,
                        "ProfileJson" TEXT NOT NULL,
                        "StartPrice" TEXT NOT NULL,
                        "CreatedAt" TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS "TradingSessions" (
                        "Id" TEXT NOT NULL PRIMARY KEY,
                        "StartedAt" TEXT NOT NULL,
                        "EndedAt" TEXT NULL,
                        "TickIntervalTicks" INTEGER NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS "SessionMarkets" (
                        "RunId" TEXT NOT NULL PRIMARY KEY,
                        "SessionId" TEXT NOT NULL,
                        "AssetId" TEXT NOT NULL,
                        FOREIGN KEY ("RunId") REFERENCES "SimulationRuns" ("Id") ON DELETE CASCADE,
                        FOREIGN KEY ("SessionId") REFERENCES "TradingSessions" ("Id") ON DELETE RESTRICT,
                        FOREIGN KEY ("AssetId") REFERENCES "Assets" ("Id") ON DELETE RESTRICT
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_SessionMarkets_SessionId_AssetId"
                        ON "SessionMarkets" ("SessionId", "AssetId");
                    CREATE INDEX IF NOT EXISTS "IX_SessionMarkets_AssetId" ON "SessionMarkets" ("AssetId");
                    """);
                db.Database.ExecuteSqlInterpolated($"""
                    INSERT INTO "StorageSchemaVersions" ("Version", "AppliedAt")
                    VALUES ({1}, {DateTimeOffset.UtcNow});
                    """);
                version = 1;
            }
            else if (!new[] { "Assets", "TradingSessions", "SessionMarkets" }.All(tables.Contains))
            {
                throw new InvalidOperationException("The ABStock database schema is incomplete.");
            }

            if (version < 2)
            {
                AddColumnIfMissing(db, "Assets", "Ticker", "TEXT COLLATE NOCASE NOT NULL DEFAULT ''");
                AddColumnIfMissing(db, "Assets", "Industry", "TEXT NOT NULL DEFAULT ''");
                AddColumnIfMissing(db, "Assets", "IncludeGovernmentSupport", "INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing(db, "Assets", "GrowthPotential", "INTEGER NULL");
                AddColumnIfMissing(db, "Assets", "UpdatedAt", "TEXT NULL");
                AddColumnIfMissing(db, "Assets", "ArchivedAt", "TEXT NULL");
                AddColumnIfMissing(db, "MarketTicks", "TotalTradeCount", "INTEGER NULL");
                FillMissingTickers(db);
                db.Database.ExecuteSqlRaw("""
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Assets_Ticker" ON "Assets" ("Ticker" COLLATE NOCASE);
                    """);
                db.Database.ExecuteSqlInterpolated($"""
                    INSERT INTO "StorageSchemaVersions" ("Version", "AppliedAt") VALUES ({2}, {DateTimeOffset.UtcNow});
                    """);
            }

            var assetColumns = ReadColumns(db, "Assets");
            if (!new[] { "Ticker", "Industry", "IncludeGovernmentSupport", "GrowthPotential", "UpdatedAt", "ArchivedAt" }
                    .All(assetColumns.Contains) || !ReadColumns(db, "MarketTicks").Contains("TotalTradeCount"))
            {
                throw new InvalidOperationException("The ABStock asset and market metric schema is incomplete.");
            }

            transaction.Commit();
            _initialized = true;
        }
    }

    private static void AddColumnIfMissing(AbStockDbContext db, string table, string column, string definition)
    {
        if (!ReadColumns(db, table).Contains(column))
        {
            // These identifiers and definitions are migration constants, not user input.
            using var command = CreateCommand(db, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");
            command.ExecuteNonQuery();
        }
    }

    private static HashSet<string> ReadColumns(AbStockDbContext db, string table)
    {
        using var command = CreateCommand(db, $"PRAGMA table_info(\"{table}\");");
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static void FillMissingTickers(AbStockDbContext db)
    {
        var assetIds = new List<string>();
        using (var command = CreateCommand(db, "SELECT \"Id\" FROM \"Assets\" WHERE \"Ticker\" = '';"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                assetIds.Add(reader.GetString(0));
            }
        }

        foreach (var assetId in assetIds)
        {
            var ticker = AssetFactory.GenerateTicker(Guid.Parse(assetId));
            db.Database.ExecuteSqlInterpolated($"UPDATE \"Assets\" SET \"Ticker\" = {ticker} WHERE \"Id\" = {assetId};");
        }
    }

    private static HashSet<string> ReadTables(AbStockDbContext db)
    {
        using var command = CreateCommand(db, "SELECT name FROM sqlite_master WHERE type = 'table';");
        using var reader = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static DbCommand CreateCommand(AbStockDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }
}
