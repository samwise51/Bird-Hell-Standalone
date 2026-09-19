namespace BirdHell.Entities;

public sealed class WeaponState
{
    public int ShotsFired { get; internal set; }

    public void Reset() => ShotsFired = 0;
}
