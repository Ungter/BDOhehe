using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace BDOhehe.Particles
{
    // Soft cloud/smoke puff. Unlike GlowParticle, there is no white hot core
    // and no shrink-out; the particle EXPANDS as it fades, giving the
    // billowing feel of a dissipating smoke cloud. Intended for explosions,
    // AoE clouds, and impact bursts where we don't want a rigid "starburst"
    // silhouette.
    public class SmokeParticle : BaseParticle
    {
        private readonly float initialScale;
        // Scale multiplier at life = 1. >1 grows, <1 shrinks.
        public float GrowthAt1 = 2.2f;
        // Random swirl applied each frame so clouds wobble instead of drifting in a straight line.
        public float Swirl = 0.4f;
        // Rotation drift speed (radians/frame).
        public float Spin;

        public SmokeParticle(Vector2 position, Vector2 velocity, Color color, float scale, int lifetime)
        {
            Position = position;
            Velocity = velocity;
            Color = color;
            Scale = scale;
            Lifetime = lifetime;
            initialScale = scale;
            Drag = 0.9f;
            Rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            Spin = Main.rand.NextFloat(-0.04f, 0.04f);
        }

        public override void Update()
        {
            // Per-frame swirl so the cloud doesn't fly in a dead-straight line.
            Velocity += new Vector2(
                Main.rand.NextFloat(-Swirl, Swirl),
                Main.rand.NextFloat(-Swirl, Swirl));
            Rotation += Spin;
            base.Update();
        }

        public override void Draw(SpriteBatch sb)
        {
            Texture2D tex = ParticleSystem.GlowOrb;
            if (tex == null) return;

            float life = LifeRatio;
            // Fade in over the first 12%, then fade out with an ease curve.
            float fadeIn = MathHelper.Clamp(life / 0.12f, 0f, 1f);
            float fadeOut = 1f - life;
            fadeOut = fadeOut * fadeOut;
            float alpha = fadeIn * fadeOut;
            if (alpha <= 0f) return;

            // Grow over life (1 -> GrowthAt1). This plus the additive blend
            // gives overlapping clouds the "puffing outward" look.
            float growth = MathHelper.Lerp(1f, GrowthAt1, life);
            float size = initialScale * growth;

            // GlowOrb is 128px; map size=1 to a roughly 32px cloud puff.
            float spriteScale = size * (16f / 128f);

            Vector2 drawPos = Position - Main.screenPosition;
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);

            // Single soft draw, NO white-hot core - clouds stay their tint.
            sb.Draw(tex, drawPos, null, Color * alpha, Rotation, origin,
                spriteScale, SpriteEffects.None, 0f);
        }
    }
}
