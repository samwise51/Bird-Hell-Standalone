using System.Numerics;
using BirdHell.Systems;

namespace BirdHell.Presentation;

public sealed class HitStopController
{
    public const float ImpactDuration = 0.04f;

    public float RemainingTime { get; private set; }
    public bool IsActive => RemainingTime > 0f;

    public void Trigger() => RemainingTime = MathF.Max(RemainingTime, ImpactDuration);

    public bool ConsumeStep(float deltaTime)
    {
        if (!IsActive) return false;
        RemainingTime = deltaTime >= RemainingTime ? 0f : RemainingTime - deltaTime;
        return true;
    }

    public void Reset() => RemainingTime = 0f;
}

public readonly record struct ImpactParticle(
    Vector2 Position,
    Vector2 Velocity,
    float Age,
    float Lifetime,
    int ColorIndex)
{
    public float RemainingFraction => Math.Clamp(1f - Age / Lifetime, 0f, 1f);
}

/// <summary>Presentation-only blood particles with a private RNG that cannot affect gameplay state.</summary>
public sealed class ImpactParticleSystem
{
    public const int ParticlesPerHit = 7;
    public const float MinimumSpeed = 2.8f;
    public const float MaximumSpeed = 5.2f;
    public const float MaximumSpreadRadians = 0.62f;
    public const float MinimumLifetime = 0.18f;
    public const float MaximumLifetime = 0.30f;
    public const float Gravity = 7f;

    private readonly List<ImpactParticle> _particles = [];
    private Random _random = new(0xB17D);

    public IReadOnlyList<ImpactParticle> Particles => _particles;

    public void Spawn(AcceptedHit hit)
    {
        var direction = hit.ForceDirection;
        if (direction.LengthSquared() <= 0.000001f)
            direction = HitContext.DirectionAwayFrom(hit.SourcePosition, hit.ImpactPosition, Vector2.UnitX);
        if (direction == Vector2.Zero) direction = Vector2.UnitX;

        for (var index = 0; index < ParticlesPerHit; index++)
        {
            var angle = Next(-MaximumSpreadRadians, MaximumSpreadRadians);
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);
            var spreadDirection = new Vector2(
                direction.X * cosine - direction.Y * sine,
                direction.X * sine + direction.Y * cosine);
            _particles.Add(new ImpactParticle(
                hit.ImpactPosition,
                spreadDirection * Next(MinimumSpeed, MaximumSpeed),
                0f,
                Next(MinimumLifetime, MaximumLifetime),
                _random.Next(3)));
        }
    }

    public void Advance(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = _particles[index];
            var age = particle.Age + deltaTime;
            if (age >= particle.Lifetime)
            {
                _particles.RemoveAt(index);
                continue;
            }
            _particles[index] = particle with
            {
                Position = particle.Position + particle.Velocity * deltaTime,
                Velocity = particle.Velocity + new Vector2(0f, -Gravity * deltaTime),
                Age = age,
            };
        }
    }

    public void Reset()
    {
        _particles.Clear();
        _random = new Random(0xB17D);
    }

    private float Next(float minimum, float maximum) =>
        minimum + (float)_random.NextDouble() * (maximum - minimum);
}
