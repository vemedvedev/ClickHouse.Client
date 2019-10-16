﻿using System;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClickHouse.Client
{
    internal class ClickHouseCommand : DbCommand
    {
        private readonly ClickHouseConnection dbConnection;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public ClickHouseCommand(ClickHouseConnection connection)
        {
            dbConnection = connection;
        }

        public override string CommandText { get; set; }

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }
        public override ISite Site { get => base.Site; set => base.Site = value; }
        protected override DbConnection DbConnection
        {
            get => dbConnection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection { get; }

        protected override DbTransaction DbTransaction { get; set; }

        protected override bool CanRaiseEvents => base.CanRaiseEvents;

        public override void Cancel() => cts.Cancel();
        public override ValueTask DisposeAsync() => base.DisposeAsync();
        public override int ExecuteNonQuery() => throw new NotImplementedException();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => base.ExecuteNonQueryAsync(cancellationToken);

        public override object ExecuteScalar() => ExecuteScalarAsync(cts.Token).GetAwaiter().GetResult();

        public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            using var reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken);
            if (reader.HasRows)
            {
                reader.Read();
                return reader[0];
            }
            else
            {
                throw new InvalidOperationException("No data returned from query");
            }
        }

        public override void Prepare() { /* ClickHouse has no notion of prepared statements */ }

        public override Task PrepareAsync(CancellationToken cancellationToken = default) => base.PrepareAsync(cancellationToken);
        protected override DbParameter CreateDbParameter() => throw new NotImplementedException();
        protected override void Dispose(bool disposing) => base.Dispose(disposing);

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteDbDataReaderAsync(behavior, cts.Token).GetAwaiter().GetResult();

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var sqlBuilder = new StringBuilder(CommandText);
            var driver = dbConnection.Driver;
            switch (behavior)
            {
                case CommandBehavior.SingleRow:
                case CommandBehavior.SingleResult:
                    sqlBuilder.Append("\nLIMIT 1");
                    break;
                case CommandBehavior.SchemaOnly:
                    if (driver == ClickHouseConnectionDriver.JSON)
                        throw new NotSupportedException("JSON driver does not support fetching schema");
                    sqlBuilder.Append("\nLIMIT 0");
                    break;
                case CommandBehavior.CloseConnection:
                case CommandBehavior.Default:
                case CommandBehavior.KeyInfo:
                case CommandBehavior.SequentialAccess:
                    break;
            }
            switch (driver)
            {
                case ClickHouseConnectionDriver.Binary:
                    sqlBuilder.Append("\nFORMAT RowBinaryWithNamesAndTypes");
                    break;
                case ClickHouseConnectionDriver.JSON:
                    sqlBuilder.Append("\nFORMAT JSONEachRow");
                    break;
                case ClickHouseConnectionDriver.TSV:
                    sqlBuilder.Append("\nFORMAT TSVWithNamesAndTypes");
                    break;
            }

            var result = await dbConnection.PostSqlQueryAsync(sqlBuilder.ToString(), cts.Token);
            ClickHouseDataReader reader = driver switch
            {
                ClickHouseConnectionDriver.Binary => new ClickHouseBinaryReader(result),
                ClickHouseConnectionDriver.JSON => new ClickHouseJsonReader(result),
                ClickHouseConnectionDriver.TSV => new ClickHouseTsvReader(result),
                _ => throw new NotSupportedException("Unknown driver: " + driver.ToString()),
            };
            return reader;

        }

        protected override object GetService(Type service) => base.GetService(service);
    }
}