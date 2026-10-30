using Microsoft.Xna.Framework;

namespace BirdHell.Presentation;

public static class HitFeedbackPresentation
{
    public static readonly Color NormalTint = Color.White;
    // The host draws this exact white tint in a short additive overlay, leaving source textures intact.
    public static readonly Color DamageTint = Color.White;

    public static Color TintFor(float damageFlashTime) =>
        damageFlashTime > 0f ? DamageTint : NormalTint;
}
