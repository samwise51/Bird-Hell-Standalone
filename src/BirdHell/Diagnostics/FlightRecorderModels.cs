using System.Numerics;

namespace BirdHell.Diagnostics;

public readonly record struct FlightVector(float X, float Y)
{
    public static FlightVector From(Vector2 value) => new(value.X, value.Y);
}

public sealed record FlightRecorderMetadata(
    int SchemaVersion,
    string SessionId,
    DateTimeOffset StartedUtc,
    string Application,
    string? Version,
    float FixedSimulationStepSeconds,
    string CoordinateSystem,
    string CoordinateUnits,
    string InitialSeed,
    string CaveProfile,
    object GenerationParameters,
    object RetentionLimits);

public sealed record FlightRecorderStepBefore(
    FlightVector PlayerPosition,
    int PlayerHealth,
    int ShotsFired,
    int ProjectileCount,
    IReadOnlyDictionary<int, BirdBefore> Birds);

public sealed record BirdBefore(
    FlightVector Center,
    int Health,
    bool Alive,
    int PathIndex,
    int PathCount,
    int SuccessfulRepaths,
    int FailedRepaths,
    FlightVector? Waypoint);

public sealed record BirdMovementSample(
    long Tick,
    double GameTimeSeconds,
    int Run,
    int BirdId,
    FlightVector Position,
    FlightVector ColliderCenter,
    FlightVector Velocity,
    FlightVector IntendedMovement,
    FlightVector ActualMovement,
    FlightVector? CurrentWaypoint,
    FlightVector TargetPlayerPosition,
    float PlayerDistance,
    float ProgressTowardPlayer,
    float StallDurationSeconds,
    int NavigationComponent,
    bool TerrainCollision,
    int TerrainCollisionCount,
    int NearbyBirdCount,
    int PathIndex,
    int PathLength,
    int SuccessfulRepaths,
    int FailedRepaths);

public sealed record FlightIncident(
    int SchemaVersion,
    long IncidentId,
    int Run,
    int BirdId,
    string Trigger,
    long TriggerTick,
    double TriggerGameTimeSeconds,
    FlightVector TriggerPosition,
    bool CompletePostWindow,
    IReadOnlyList<BirdMovementSample> Samples);
