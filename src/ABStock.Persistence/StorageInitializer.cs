using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ABStock.Persistence;

internal sealed class StorageInitializer(IDbContextFactory<AbStockDbContext> contextFactory)
{
    private const int CurrentVersion = 1;
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
                    VALUES ({CurrentVersion}, {DateTimeOffset.UtcNow});
                    """);
            }
            else if (!new[] { "Assets", "TradingSessions", "SessionMarkets" }.All(tables.Contains))
            {
                throw new InvalidOperationException("The ABStock database schema is incomplete.");
            }

            transaction.Commit();
            _initialized = true;
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
