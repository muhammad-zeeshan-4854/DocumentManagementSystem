using System.Data;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using DocumentManagement.Web.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DocumentManagement.Web.Helper;

public interface ISqlHelper
{
    Task<IReadOnlyList<T>> ExecQueryAsync<T>(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken);

    Task<T> ExecQuerySingleAsync<T>(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken);

    Task<T> ExecQueryMultipleAsync<T>(string strProcedureName, DynamicParameters oParam,
        Func<SqlMapper.GridReader, Task<T>> fnRead, CancellationToken oCancellationToken);

    Task ExecNonQueryAsync(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken);
}

public class SqlHelper : ISqlHelper
{
    public const string ConnectionStringName = "DocumentManagement";

    private const int iMaxLoggedValueLength = 100;

    private readonly string strConnectionString;
    private readonly DatabaseSettings oSettings;
    private readonly ILogger<SqlHelper> oLogger;

    public SqlHelper(IConfiguration oConfiguration, IOptions<DatabaseSettings> oSettings, ILogger<SqlHelper> oLogger)
    {
        strConnectionString = oConfiguration.GetConnectionString(ConnectionStringName) is { Length: > 0 } strValue
            ? strValue
            : throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing from configuration.");
        this.oSettings = oSettings.Value;
        this.oLogger = oLogger;
    }

    public async Task<IReadOnlyList<T>> ExecQueryAsync<T>(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken)
    {
        return await ExecAsync(strProcedureName, oParam, oCancellationToken,
            async (oConn, oCommand) => (IReadOnlyList<T>)(await oConn.QueryAsync<T>(oCommand)).AsList());
    }

    public Task<T> ExecQuerySingleAsync<T>(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken)
    {
        return ExecAsync(strProcedureName, oParam, oCancellationToken,
            (oConn, oCommand) => oConn.QuerySingleAsync<T>(oCommand));
    }

    public Task<T> ExecQueryMultipleAsync<T>(string strProcedureName, DynamicParameters oParam,
        Func<SqlMapper.GridReader, Task<T>> fnRead, CancellationToken oCancellationToken)
    {
        return ExecAsync(strProcedureName, oParam, oCancellationToken, async (oConn, oCommand) =>
        {
            await using var oMultiReader = await oConn.QueryMultipleAsync(oCommand);
            return await fnRead(oMultiReader);
        });
    }

    public Task ExecNonQueryAsync(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken)
    {
        return ExecAsync(strProcedureName, oParam, oCancellationToken,
            (oConn, oCommand) => oConn.ExecuteAsync(oCommand));
    }

    private async Task<T> ExecAsync<T>(string strProcedureName, DynamicParameters oParam, CancellationToken oCancellationToken,
        Func<SqlConnection, CommandDefinition, Task<T>> fnCommand)
    {
        var oStopwatch = Stopwatch.StartNew();
        try
        {
            await using var oConn = new SqlConnection(strConnectionString);
            var oCommand = new CommandDefinition(strProcedureName, oParam, commandType: CommandType.StoredProcedure,
                commandTimeout: oSettings.CommandTimeoutSeconds, cancellationToken: oCancellationToken);

            T oResult = await fnCommand(oConn, oCommand);

            oStopwatch.Stop();
            if (oStopwatch.ElapsedMilliseconds >= oSettings.SlowCallWarningMilliseconds)
            {
                oLogger.LogWarning("Slow SP {ProcedureName} took {ElapsedMs} ms (limit {ThresholdMs} ms). Parameters: {Parameters}",
                    strProcedureName, oStopwatch.ElapsedMilliseconds, oSettings.SlowCallWarningMilliseconds, GetParameterText(oParam));
            }
            else
            {
                oLogger.LogDebug("Executed {ProcedureName} in {ElapsedMs} ms", strProcedureName, oStopwatch.ElapsedMilliseconds);
            }
            return oResult;
        }
        catch (SqlException ex) when (BusinessRuleException.IsBusinessErrorNumber(ex.Number))
        {
            oLogger.LogInformation("{ProcedureName} rejected the request with {SqlErrorNumber}: {BusinessMessage} ({ElapsedMs} ms). Parameters: {Parameters}",
                strProcedureName, ex.Number, ex.Message, oStopwatch.ElapsedMilliseconds, GetParameterText(oParam));
            throw new BusinessRuleException(ex.Number, ex.Message, ex);
        }
        catch (Exception ex) when (oCancellationToken.IsCancellationRequested)
        {
            // SqlClient reports an aborted request as a SqlException ("Operation cancelled by user").
            oLogger.LogInformation("{ProcedureName} was cancelled after {ElapsedMs} ms because the request was aborted",
                strProcedureName, oStopwatch.ElapsedMilliseconds);
            throw new OperationCanceledException($"{strProcedureName} was cancelled.", ex, oCancellationToken);
        }
        catch (Exception ex)
        {
            // GlobalExceptionHandler logs it.
            throw new DataAccessException(strProcedureName, GetParameterText(oParam), oStopwatch.ElapsedMilliseconds,
                (ex as SqlException)?.Number, ex);
        }
    }

    private static string GetParameterText(DynamicParameters oParam)
    {
        var lstParameters = new List<string>();
        foreach (string strName in oParam.ParameterNames)
        {
            object? oValue = oParam.Get<object?>(strName);
            string strValue = oValue switch
            {
                null => "NULL",
                string strText when strText.Length > iMaxLoggedValueLength => $"\"{strText[..iMaxLoggedValueLength]}...\" (length {strText.Length})",
                string strText => $"\"{strText}\"",
                byte[] aryBytes => "0x" + Convert.ToHexString(aryBytes),
                _ => Convert.ToString(oValue, CultureInfo.InvariantCulture) ?? "NULL"
            };
            lstParameters.Add($"@{strName}={strValue}");
        }
        return lstParameters.Count == 0 ? "(none)" : string.Join(", ", lstParameters);
    }
}
