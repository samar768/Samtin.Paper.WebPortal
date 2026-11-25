using System.Data;
using System.Diagnostics;
using System.Text;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface ICrystalSalesOrderRenderer
{
    Task<byte[]> ExportAsync(DataSet data, CancellationToken ct);
}

public sealed class CrystalSalesOrderRenderer(ILogger<CrystalSalesOrderRenderer> logger, IHostApplicationLifetime lifetime)
    : ICrystalSalesOrderRenderer, IDisposable
{
    private readonly SemaphoreSlim slot = new(1, 1);
    private Process? worker;
    private int completedExports;

    private Process GetWorker(string executable)
    {
        if (worker is { HasExited: false } && completedExports < 50) return worker;
        StopWorker();

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        start.Environment.Remove("Poc__ControlConnection");
        start.Environment.Remove("Poc__TransactionConnection");
        start.ArgumentList.Add("--worker");
        worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Crystal renderer.");
        worker.BeginErrorReadLine();
        return worker;
    }

    private void StopWorker()
    {
        if (worker is not { } process) return;
        worker = null;
        completedExports = 0;
        using (process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            if (!process.WaitForExit(2000))
                logger.LogWarning("Crystal worker did not exit promptly after termination.");
        }
    }

    public void Dispose() => StopWorker();

    public async Task<byte[]> ExportAsync(DataSet data, CancellationToken ct)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Reports");
        var executable = Path.Combine(assets, "Techno.Paper.CrystalPrint.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(executable) || !File.Exists(Path.Combine(assets, "SaleOrder.rpt")))
            throw new ApiFault(503, "print_unavailable", "The Windows Crystal sample report is not installed.");
        if (!await slot.WaitAsync(0, ct))
            throw new ApiFault(503, "print_busy", "The sample renderer is busy. Please try again.");

        DirectoryInfo? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("PaperSoftCrystal-");
            var input = Path.Combine(directory.FullName, "order.xml");
            var output = Path.Combine(directory.FullName, "order.pdf");
            data.WriteXml(input, XmlWriteMode.WriteSchema);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            string? reply;
            try
            {
                var process = GetWorker(executable);
                await process.StandardInput.WriteLineAsync(input.AsMemory(), timeout.Token);
                await process.StandardInput.WriteLineAsync(output.AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);
                reply = await process.StandardOutput.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                StopWorker();
                ct.ThrowIfCancellationRequested();
                lifetime.ApplicationStopping.ThrowIfCancellationRequested();
                logger.LogWarning("Crystal Sales Order export timed out.");
                throw new ApiFault(503, "print_timeout", "The Crystal sample renderer timed out.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                StopWorker();
                logger.LogWarning(ex, "Crystal Sales Order worker communication failed.");
                throw new ApiFault(503, "print_failed", "The Crystal sample could not render the order. Please try again.");
            }

            var file = new FileInfo(output);
            if (reply == "OK" && file.Exists && file.Length is > 5 and <= 10_000_000)
            {
                completedExports++;
                var pdf = await File.ReadAllBytesAsync(output, ct);
                if (pdf.AsSpan().StartsWith("%PDF-"u8)) return pdf;
            }
            StopWorker();
            logger.LogWarning("Crystal Sales Order export did not produce a valid PDF.");
            throw new ApiFault(503, "print_failed", "The Crystal sample could not render the order. Check the Windows runtime and report installation.");
        }
        finally
        {
            try { directory?.Delete(recursive: true); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not remove the Crystal export temporary directory."); }
            catch (UnauthorizedAccessException ex) { logger.LogWarning(ex, "Could not remove the Crystal export temporary directory."); }
            finally { slot.Release(); }
        }
    }
}
