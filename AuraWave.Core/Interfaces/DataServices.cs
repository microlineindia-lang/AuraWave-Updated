using AuraWave.Core.Configuration;
using AuraWave.Core.Models;

namespace AuraWave.Core.Interfaces;

public interface IDataExportService
{
    string DefaultExportDirectory { get; }

    Task<string> ExportPatternCsvAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default);
    Task<string> ExportPatternCsvAsync(IReadOnlyList<MeasurementPoint> points, ScanConfiguration? config, string fileName, string? directory = null, CancellationToken ct = default);
    Task<string> ExportHtmlReportAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default);
    Task<string> ExportTouchstoneS2PAsync(MeasurementResult result, string? directory = null, CancellationToken ct = default);

    Task<MeasurementResult?> ImportPatternCsvAsync(string filePath, CancellationToken ct = default);
    Task<SParameterData?> ImportTouchstoneAsync(string filePath, CancellationToken ct = default);
    Task<VnaMeasurementSnapshot?> ImportVnaCsvAsync(string filePath, CancellationToken ct = default);
}

public interface ISettingsPersistenceService
{
    Task SaveAsync(HardwareConfiguration hardware, ApplicationSettings application, CancellationToken ct = default);
}

public interface IHardwareReadinessService
{
    bool IsReadyForScan(SystemHardwareState state, bool requireRfSwitch, out string blockingReason);
}
