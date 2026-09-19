using System.Numerics;
using BirdHell.Entities;

namespace BirdHell.Systems;

public sealed record WeaponSettings
{
    public float MuzzleOffset { get; init; } = 0.086f * 4f;
    public float ProjectileSpeed { get; init; } = 20f;
    public int ProjectileDamage { get; init; } = 25;
    public float ProjectileColliderRadius { get; init; } = 0.34f;
    public float ProjectileColliderCenterOffset { get; init; } = 0.34f;
    public float ProjectileColliderVerticalOffset { get; init; } = 0.28f;
    public float? ProjectileLifetime { get; init; }
}

public sealed class WeaponSystem
{
    private readonly WeaponSettings _settings;

    public WeaponSystem(WeaponSettings? settings = null)
    {
        _settings = settings ?? new WeaponSettings();
    }

    public Vector2 GetAimDirection(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new Vector2(player.FacingDirection, 0f);
    }

    public Vector2 GetMuzzlePosition(Player player) => player.Position + GetAimDirection(player) * _settings.MuzzleOffset;

    public Projectile? Fire(WeaponState state, Player player, bool firePressed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(player);
        if (!firePressed || !player.IsAlive) return null;

        var direction = GetAimDirection(player);
        state.ShotsFired++;
        return new Projectile(
            GetMuzzlePosition(player),
            direction,
            _settings.ProjectileSpeed,
            _settings.ProjectileDamage,
            _settings.ProjectileColliderRadius,
            _settings.ProjectileColliderCenterOffset,
            _settings.ProjectileColliderVerticalOffset,
            _settings.ProjectileLifetime);
    }
}
