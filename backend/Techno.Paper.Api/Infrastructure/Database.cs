using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Techno.Paper.Api.Infrastructure;

public sealed class PocOptions
{
    public string ControlConnection { get; set; } = "";
    public string TransactionConnection { get; set; } = "";
    public string Company { get; set; } = "26";
    public string Site { get; set; } = "HO";
    public string PublicOrigin { get; set; } = "http://localhost:3000";
    public string MetadataAdministrator { get; set; } = "";
    public bool DevelopmentWritesApproved { get; set; }
    public bool CrystalSalesOrderSample { get; set; }
}

public sealed class Database(PocOptions options)
{
    public async Task<SqlConnection> OpenAsync(bool control, CancellationToken ct)
    {
        var connection = new SqlConnection(control ? options.ControlConnection : options.TransactionConnection);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 15 };
        foreach (var (name, value) in parameters)
        {
            var parameter = value switch
            {
                int => new SqlParameter(name, SqlDbType.Int),
                long => new SqlParameter(name, SqlDbType.BigInt),
                decimal => new SqlParameter(name, SqlDbType.Decimal) { Precision = 28, Scale = 8 },
                double => new SqlParameter(name, SqlDbType.Float),
                bool => new SqlParameter(name, SqlDbType.Bit),
                DateTimeOffset => new SqlParameter(name, SqlDbType.DateTimeOffset),
                DateTime => new SqlParameter(name, SqlDbType.DateTime2),
                byte[] => new SqlParameter(name, SqlDbType.VarBinary, -1),
                _ => new SqlParameter(name, SqlDbType.NVarChar, -1)
            };
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    public static async Task<List<Dictionary<string, string?>>> QueryAsync(SqlConnection connection,
        SqlTransaction? transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, string?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i) switch
                {
                    DateTime date => date.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                    DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                    byte[] bytes => Convert.ToBase64String(bytes),
                    IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                    var value => value.ToString()
                });
            }
            rows.Add(row);
        }
        return rows;
    }

    public static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction? transaction,
        string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
