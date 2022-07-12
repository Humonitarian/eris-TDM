// Added by https://github.com/HentaiStorm

using System.Linq;

using Content.Server.Chat.Managers;
using Content.Shared.MobState;
using Content.Server.Mind.Components;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Configurations;
using Content.Server.Spawners.Components;
using Robust.Shared.Random;


namespace Content.Server.Eris.TDM.KillCounter;

/// <summary>
/// GameRule managed team deathmatch between all possible jobs (manage job in map config).
/// Setup `private const DeathToRestart` to define max deaths in only one job, for declare game end.
/// </summary>
public sealed class KillCounterRule : GameRuleSystem
{
    public override string Prototype => "TeamDeathMatch";

    // Connect various systems and managers into class
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    private const int DeathToRestart = 100;

    /// <summary>
    /// Dictionary to store death counter by every job.
    /// </summary>
    private Dictionary<string, int>? _deathStats = new Dictionary<string, int>();

    public override void Initialize()
    {
        base.Initialize();

        // Connect various events to handle this
        SubscribeLocalEvent<StationInitializedEvent>(OnStationInitialized);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChange);
        SubscribeLocalEvent<PlayerSpawningEvent>(OnSpawnPlayer);

        // Log console about TDM
        Logger.InfoS("gamepreset", "Selected TeamDeathMatch!");
    }

    /// <summary>
    /// Handle player spawn event, and create player across standard (not suitable) on any job spawnpoint.
    /// </summary>
    private void OnSpawnPlayer(PlayerSpawningEvent ev)
    {
        var points = EntityQuery<SpawnPointComponent>().ToList();
        _random.Shuffle(points);
        foreach (var spawnPoint in points)
        {
            var xform = Transform(spawnPoint.Owner);
            if (ev.Station != null && _stationSystem.GetOwningStation(spawnPoint.Owner, xform) != ev.Station)
                continue;

            if (ev.Job == null || spawnPoint.Job?.ID == ev.Job.Prototype.ID)
            {
                ev.Handled = true;
                ev.SpawnResult = _stationSpawning.SpawnPlayerMob(
                    xform.Coordinates,
                    ev.Job,
                    ev.HumanoidCharacterProfile,
                    ev.Station);

                return;
            }
        }
    }

    /// <summary>
    /// Handle station initialized event, for populate _deathStats zero score. 
    /// </summary>
    private void OnStationInitialized(StationInitializedEvent ev)
    {
        var stationData = Comp<StationDataComponent>(ev.Station);

        _deathStats = stationData.StationConfig?.AvailableJobs.ToDictionary(x => x.Key, x => 0);
    }

    /// <summary>
    /// Handle mob state change event, for increment job deaths stat and declare game end by overflow death limit.
    /// </summary>
    private void OnMobStateChange(MobStateChangedEvent ev)
    {
        if (ev.CurrentMobState == DamageState.Dead && TryComp<MindComponent>(ev.Entity, out var mindComp))
        {
            var mindJob = mindComp.Mind?.CurrentJob?.Prototype.ID ?? "none";

            if (mindComp.Mind != null && _deathStats != null && _deathStats.TryGetValue(mindJob, out int deathStat))
            {
                deathStat++;
                _deathStats[mindJob] = deathStat;

                if (deathStat == DeathToRestart)
                {
                    _chatManager.DispatchServerAnnouncement(
                        "Game ended!\n" + string.Join("\n", _deathStats.OrderBy(x => x.Value).Select(
                            (items, index) => $"{index + 1}. {items.Key}: {items.Value} deaths")));

                    GameTicker.EndRound();
                    GameTicker.RestartRound();
                }
                else
                {
                    _chatManager.DispatchServerAnnouncement($"{mindJob} has lost warrior! {DeathToRestart - deathStat} to restart!");
                }
            }
        }
    }

    /// <summary>
    /// Handle ghostize event, for instant player respawn.
    /// </summary>
    private void OnGhostAttempt(GhostAttemptHandleEvent ev)
    {
        if (ev.Mind?.Session != null)
        {
            GameTicker.Respawn(ev.Mind.Session);
        }
    }

    public override void Started(GameRuleConfiguration configuration) { }

    public override void Ended(GameRuleConfiguration configuration) { }
}
