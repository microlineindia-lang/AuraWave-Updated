using AuraWave.Core.Models;
using System.Collections.ObjectModel;

namespace AuraWave.Core.Services;

/// <summary>Shared live measurement state for plots and analysis pages.</summary>
public sealed class MeasurementSession
{
    public ObservableCollection<MeasurementPoint> LivePoints { get; } = new();
    public MeasurementResult? ActiveResult { get; set; }
    public SParameterData? LastS21Sweep { get; set; }
    public SParameterData? LastS11Sweep { get; set; }
    public VnaMeasurementSnapshot? VnaSnapshot { get; private set; }

    public event EventHandler? LiveDataChanged;
    public event EventHandler? VnaDataChanged;
    public event EventHandler<SParameterData>? S21SweepUpdated;
    public event EventHandler<SParameterData>? S11SweepUpdated;

    public void Clear()
    {
        LivePoints.Clear();
        ActiveResult = null;
        LastS21Sweep = null;
        LastS11Sweep = null;
        VnaSnapshot = null;
        LiveDataChanged?.Invoke(this, EventArgs.Empty);
        VnaDataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVnaSnapshot(VnaMeasurementSnapshot snapshot)
    {
        VnaSnapshot = snapshot;
        LastS21Sweep = snapshot.Get("S21");
        LastS11Sweep = snapshot.Get("S11");
        if (LastS21Sweep is not null)
            S21SweepUpdated?.Invoke(this, LastS21Sweep);
        if (LastS11Sweep is not null)
            S11SweepUpdated?.Invoke(this, LastS11Sweep);
        VnaDataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddPoint(MeasurementPoint point)
    {
        LivePoints.Add(point);
        LiveDataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetS21Sweep(SParameterData data)
    {
        LastS21Sweep = data;
        S21SweepUpdated?.Invoke(this, data);
    }

    public void SetS11Sweep(SParameterData data)
    {
        LastS11Sweep = data;
        S11SweepUpdated?.Invoke(this, data);
    }
}
