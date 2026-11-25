using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;

namespace Techno.Paper.Api.Services;

internal static class DocumentNumbering
{
    // Table names come only from server-owned aggregate mappings, never request input.
    internal static async Task<DocumentNumbersResponse> NextAsync(SqlConnection connection, SqlTransaction? transaction,
        string table, string site, string voucher, string prefix, CancellationToken ct)
    {
        var identifier = "[" + table.Replace("]", "]]") + "]";
        var locks = transaction is null ? "" : "WITH(UPDLOCK,HOLDLOCK)";
        var rows = await Database.QueryAsync(connection, transaction,
            $"""
            SELECT
              (SELECT ISNULL(MAX(Number),0)+1 FROM (
                SELECT CONVERT(bigint,CASE WHEN NULLIF(LTRIM(RTRIM(V_No)),'') IS NOT NULL
                         AND LTRIM(RTRIM(V_No)) COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9]%'
                         THEN LTRIM(RTRIM(V_No)) ELSE '0' END) AS Number
                FROM dbo.{identifier} {locks} WHERE Site_Code=@site AND V_Prefix=@prefix AND V_Type=@voucher
                UNION ALL
                SELECT CONVERT(bigint,RIGHT(DocId,8)) FROM webpoc.Command {locks}
                  WHERE LEFT(DocId,13)=@docPrefix
              ) IssuedNumbers) AS VoucherNumber,
                   ISNULL(MAX(CONVERT(bigint,CASE WHEN NULLIF(LTRIM(RTRIM(RecId)),'') IS NOT NULL
                       AND LTRIM(RTRIM(RecId)) COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9]%'
                       THEN LTRIM(RTRIM(RecId)) ELSE '0' END)),0)+1 AS Reference
            FROM dbo.{identifier} {locks} WHERE Site_Code=@site AND V_Prefix=@prefix AND V_Type=@voucher
            """, ct, ("@site", site), ("@prefix", prefix), ("@voucher", voucher),
            ("@docPrefix", SalesRules.DocumentId(site, voucher, prefix, 1)[..13]));
        return new DocumentNumbersResponse(rows[0]["Reference"]!, rows[0]["VoucherNumber"]!);
    }
}
