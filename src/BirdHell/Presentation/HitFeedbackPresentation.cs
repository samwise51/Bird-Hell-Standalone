using Microsoft.Xna.Framework;

namespace BirdHell.Presentation;

public static class HitFeedbackPresentation
{
    public static readonly Color NormalTint = Color.White;
    public static readonly Color DamageTint = new(255, 160, 160);

    public static Color TintFor(float damageFlashTime) =>
        damageFlashTime > 0f ? DamageTint : NormalTint;
}
