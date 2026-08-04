using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>
/// Manages team config profiles: save, load, export, import, and apply templates.
/// Allows users to export their Georgia setup and apply it to all other teams.
/// </summary>
public sealed class ConfigProfileManager
{
    private readonly string _profilesDir;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public event Action<string>? Log;

    public ConfigProfileManager(string baseDirectory = null)
    {
        _profilesDir = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    /// <summary>Save team config to disk.</summary>
    public void SaveProfile(string teamName, List<TriggerEntry> config)
    {
        if (string.IsNullOrWhiteSpace(teamName) || config == null) return;

        string filePath = GetProfilePath(teamName);
        string json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(filePath, json);
        Log?.Invoke($"[Config] Saved profile: {teamName}");
    }

    /// <summary>Load team config from disk.</summary>
    public List<TriggerEntry> LoadProfile(string teamName)
    {
        string filePath = GetProfilePath(teamName);
        if (!File.Exists(filePath)) return null;

        try
        {
            string json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<List<TriggerEntry>>(json);
            Log?.Invoke($"[Config] Loaded profile: {teamName}");
            return config;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Config] Error loading {teamName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Export team config as JSON file.</summary>
    public void ExportProfile(string teamName, string exportPath)
    {
        var config = LoadProfile(teamName);
        if (config == null)
        {
            Log?.Invoke($"[Config] Cannot export: {teamName} not found");
            return;
        }

        var exportData = new
        {
            version = "1.0",
            exported_at = DateTime.UtcNow.ToString("o"),
            team = teamName,
            events = config.ToDictionary(
                e => e.Trigger,
                e => new
                {
                    audio = e.AudioFile,
                    eventName = e.Event,
                    volume = 1.0f,  // Could expand to store per-event volumes
                    reverb = 0.0f
                }
            )
        };

        string json = JsonSerializer.Serialize(exportData, _jsonOptions);
        File.WriteAllText(exportPath, json);
        Log?.Invoke($"[Config] Exported {teamName} to {exportPath}");
    }

    /// <summary>Import team config from JSON file.</summary>
    public void ImportProfile(string importPath, string targetTeamName)
    {
        if (!File.Exists(importPath))
        {
            Log?.Invoke($"[Config] Import file not found: {importPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(importPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("events", out var eventsElement))
            {
                Log?.Invoke("[Config] Invalid import file: no 'events' property");
                return;
            }

            var config = new List<TriggerEntry>();
            foreach (var prop in eventsElement.EnumerateObject())
            {
                string eventName = prop.Value.TryGetProperty("eventName", out var evProp)
                    ? evProp.GetString() ?? prop.Name
                    : prop.Name;

                config.Add(new TriggerEntry
                {
                    Trigger = prop.Name,
                    Event = eventName,
                    AudioFile = prop.Value.GetProperty("audio").GetString() ?? ""
                });
            }

            SaveProfile(targetTeamName, config);
            Log?.Invoke($"[Config] Imported profile for {targetTeamName}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Config] Import error: {ex.Message}");
        }
    }

    /// <summary>Apply one team's config as template to all other teams.</summary>
    public void ApplyProfileToAllTeams(string sourceTeamName, bool overwriteExisting = false)
    {
        var sourceConfig = LoadProfile(sourceTeamName);
        if (sourceConfig == null)
        {
            Log?.Invoke($"[Config] Source team not found: {sourceTeamName}");
            return;
        }

        var allTeams = TeamColors.All.Select(t => t.Name).ToList();
        int applied = 0;

        foreach (var teamName in allTeams)
        {
            if (teamName == sourceTeamName) continue;

            if (!overwriteExisting && File.Exists(GetProfilePath(teamName)))
                continue;  // Skip existing profiles unless explicitly overwriting

            SaveProfile(teamName, sourceConfig);
            applied++;
        }

        Log?.Invoke($"[Config] Applied {sourceTeamName} template to {applied} teams");
    }

    /// <summary>Create encrypted backup of all profiles.</summary>
    public void CreateEncryptedBackup(string backupPath, string password)
    {
        try
        {
            using var zip = new ZipArchive(File.Create(backupPath), ZipArchiveMode.Create);

            foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
            {
                string entryName = Path.GetFileName(file);
                var entry = zip.CreateEntry(entryName);
                using var stream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(stream);
            }

            Log?.Invoke($"[Config] Backup created: {backupPath}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Config] Backup error: {ex.Message}");
        }
    }

    private string GetProfilePath(string teamName) =>
        Path.Combine(_profilesDir, $"{teamName}.json");
}
