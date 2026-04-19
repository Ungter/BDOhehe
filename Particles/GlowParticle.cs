using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace BDOhehe.Particles
{
    // Soft HD glow orb. Default "all-purpose" particle - replaces most of the
    // DustID.PurpleTorch spam with a smoother additive bloom. The big 128x128
    // source texture is scaled down via Scale so the final pixels are soft
    // sub-pixel blended rather than stuck on Terraria's 2x2 dust grid.
    public class GlowParticle : BaseParticle
    {
        // Velocity is multiplied by this at spawn-decay; distinct from Drag
        // so callers can set a hard stop timeline on motion.
        public float FadeInFraction = 0.15f;
        // If > 0, scale eases from 0 -> Scale over this many frames.
        public int ScaleInFrames = 4;
        // Final scale is multiplied by (1 - lifeRatio) in addition to fade-in.
        public bool ShrinkOut = true;

        private readonly float initialScale;

        // 0 = no white core tint (color stays pure), 1 = full white-hot center.
        public float CoreWhiteness = 0.25f;
        // Intensity of the secondary bright inner draw. 0 disables it.
        public float CoreIntensity = 0.8f;

        public GlowParticle(Vector2 position, Vector2 velocity, Color color, float scale, int lifetime)
        {
            Position = position;
            Velocity = velocity;
            Color = color;
            Scale = scale;
            Lifetime = lifetime;
            initialScale = scale;
            Drag = 0.94f;
        }

        public override void Draw(SpriteBatch sb)
        {
            Texture2D tex = ParticleSystem.GlowOrb;
            if (tex == null) return;

            float life = LifeRatio;
            float fadeIn = ScaleInFrames > 0
                ? MathHelper.Clamp((float)Time / ScaleInFrames, 0f, 1f)
                : 1f;
            float fadeOut = ShrinkOut ? MathHelper.Clamp(1f - life, 0f, 1f) : 1f;

            float s = initialScale * fadeIn * fadeOut;
            if (s <= 0f) return;

            // 128px source -> 1 world-pixel per ~128 of scale value. Divide
            // so "scale = 1" corresponds roughly to a 24px-diameter glow,
            // matching what a scale-1 Dust looks like.
            float spriteScale = s * (12f / 128f);

            Vector2 drawPos = Position - Main.screenPosition;
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);
            Color draw = Color * fadeOut;

            sb.Draw(tex, drawPos, null, draw, Rotation, origin, spriteScale, SpriteEffects.None, 0f);

            // Optional inner core. Keep it mostly the base color (only a
            // pinch of white lerp) so the particle reads as a tinted glow
            // instead of washing out to white under additive blending.
            if (CoreIntensity > 0f)
            {
                Color core = Color.Lerp(Color, Color.White, CoreWhiteness) * fadeOut * CoreIntensity;
                sb.Draw(tex, drawPos, null, core, Rotation, origin, spriteScale * 0.55f, SpriteEffects.None, 0f);
            }
        }
    }
}
