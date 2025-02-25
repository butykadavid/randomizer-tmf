﻿using GBX.NET.Engines.Game;
using Microsoft.Extensions.Logging;
using RandomizerTMF.Logic.Services;
using System.IO.Abstractions;
using TmEssentials;
using YamlDotNet.Serialization;

namespace RandomizerTMF.Logic;

public class SessionData
{
    private readonly IRandomizerConfig config;
    private readonly ILogger? logger;
    private readonly IFileSystem? fileSystem;

    public string? Version { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public TimeSpan OriginalTimeLimit { get; set; }
    public double AuthorRate { get; set; }
    public RandomizerRules Rules { get; set; }

    [YamlIgnore]
    public string StartedAtText => StartedAt.ToString("yyyy-MM-dd HH_mm_ss");
    
    [YamlIgnore]
    public string DirectoryPath { get; }

    public List<SessionDataMap> Maps { get; set; } = new();

    public SessionData() : this(version: null, // will be overwriten by deserialization
                                DateTimeOffset.Now, // will be overwriten by deserialization
                                new RandomizerConfig(), // unused in read-only state
                                logger: null, // unused in read-only state
                                fileSystem: null) // unused in read-only state
    {
        // This is legit only for read-only use cases and for YAML deserialization!
    }

    private SessionData(string? version,
                        DateTimeOffset startedAt,
                        IRandomizerConfig config,
                        ILogger? logger,
                        IFileSystem? fileSystem)
    {
        Version = version;
        StartedAt = startedAt;
        OriginalTimeLimit = config.Rules.TimeLimit;
        
        this.config = config;
        this.logger = logger;
        this.fileSystem = fileSystem;
        
        Rules = config.Rules;

        DirectoryPath = Path.Combine(FilePathManager.SessionsDirectoryPath, StartedAtText);
    }

    internal static SessionData Initialize(DateTimeOffset startedAt, IRandomizerConfig config, ILogger logger, IFileSystem fileSystem)
    {
        var data = new SessionData(RandomizerEngine.Version, startedAt, config, logger, fileSystem);

        fileSystem.Directory.CreateDirectory(data.DirectoryPath);

        data.Save();

        return data;
    }

    public static SessionData Initialize(IRandomizerConfig config, ILogger logger, IFileSystem fileSystem)
    {
        return Initialize(DateTimeOffset.Now, config, logger, fileSystem);
    }

    public void SetMapResult(SessionMap map, string result)
    {
        var dataMap = Maps.First(x => x.Uid == map.MapUid);

        dataMap.Result = result;
        dataMap.LastTimestamp = map.LastTimestamp;

        Save();
    }

    public void Save()
    {
        logger?.LogInformation("Saving the session data into file...");

        fileSystem?.File.WriteAllText(Path.Combine(DirectoryPath, Constants.SessionYml), Yaml.Serializer.Serialize(this));

        logger?.LogInformation("Session data saved.");
    }

    internal void InternalSetReadOnlySessionYml()
    {
        var sessionYmlFile = Path.Combine(DirectoryPath, Constants.SessionYml);
        fileSystem?.File.SetAttributes(sessionYmlFile, fileSystem.File.GetAttributes(sessionYmlFile) | FileAttributes.ReadOnly);
    }

    public void SetReadOnlySessionYml()
    {
        try
        {
            InternalSetReadOnlySessionYml();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set Session.yml as read-only.");
        }
    }

    public void UpdateFromAutosave(string fullPath, SessionMap map, CGameCtnReplayRecord replay, TimeSpan elapsed)
    {
        var score = map.Map.Mode switch
        {
            CGameCtnChallenge.PlayMode.Stunts => replay.GetGhosts(alsoInClips: false).First().StuntScore + "_",
            CGameCtnChallenge.PlayMode.Platform => replay.GetGhosts(alsoInClips: false).First().Respawns + "_",
            _ => ""
        } + replay.Time.ToTmString(useHundredths: true, useApostrophe: true);

        var mapName = CompiledRegex.SpecialCharRegex().Replace(TextFormatter.Deformat(map.Map.MapName).Trim(), "_");

        var replayFileFormat = string.IsNullOrWhiteSpace(config.ReplayFileFormat)
            ? Constants.DefaultReplayFileFormat
            : config.ReplayFileFormat;

        var replayFileName = FilePathManager.ClearFileName(string.Format(replayFileFormat, mapName, score, replay.PlayerLogin));

        var replaysDir = Path.Combine(DirectoryPath, Constants.Replays);
        var replayFilePath = Path.Combine(replaysDir, replayFileName);

        if (fileSystem is not null)
        {
            fileSystem.Directory.CreateDirectory(replaysDir);
            fileSystem.File.Copy(fullPath, replayFilePath, overwrite: true);
        }
        
        Maps.First(x => x.Uid == map.MapUid)?
            .Replays
            .Add(new()
            {
                FileName = replayFileName,
                Timestamp = elapsed
            });

        Save();
    }
}
