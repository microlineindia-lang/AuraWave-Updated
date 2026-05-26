using AuraWave.Core.Interfaces;
using AuraWave.Core.Models;
using Newtonsoft.Json;

namespace AuraWave.Core.Services;

public sealed class ProjectService : IProjectService
{
    private readonly ILogService _log;

    public AuraWaveProject? CurrentProject { get; private set; }
    public string? CurrentFilePath { get; private set; }
    public event EventHandler<AuraWaveProject>? ProjectChanged;

    public ProjectService(ILogService log) => _log = log;

    public void NewProject(string name)
    {
        CurrentProject = new AuraWaveProject
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        CurrentFilePath = null;
        _log.Info("PROJECT", $"New project: {name}");
        ProjectChanged?.Invoke(this, CurrentProject);
    }

    public async Task<bool> OpenProjectAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            CurrentProject = JsonConvert.DeserializeObject<AuraWaveProject>(json);
            if (CurrentProject is null) return false;
            CurrentFilePath = filePath;
            _log.Info("PROJECT", $"Opened {filePath}");
            ProjectChanged?.Invoke(this, CurrentProject);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("PROJECT", $"Open failed: {ex.Message}");
            return false;
        }
    }

    public async Task SaveProjectAsync(string? filePath = null)
    {
        if (CurrentProject is null) return;
        filePath ??= CurrentFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AuraWave",
            $"{CurrentProject.Name}.aurawave");

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        CurrentProject.UpdatedAt = DateTime.UtcNow;
        var json = JsonConvert.SerializeObject(CurrentProject, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json);
        CurrentFilePath = filePath;
        _log.Info("PROJECT", $"Saved {filePath}");
    }
}
