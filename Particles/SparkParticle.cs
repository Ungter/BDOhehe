using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace BDOhehe.Particles
{
    // Elongated streak particle that auto-aligns with its velocity. Used for
    // fast-moving sparks (sword swing tip, dash trail highlights, explosion
    // shockwaves). Uses the streak texture so overlapping sparks read as
    // sharp, directional energy rather than round glow.
    public class SparkParticle : BaseParticle
    {
        public float LengthScale;
        public float ThicknessScale;
        // If true, Rotation stays at whatever the caller set and is NOT
        // auto-updated from velocity. Used for "trail streaks" that point
        // along the parent projectile's travel direction rather than along
        // the particle's own tiny jitter velocity.
        public bool LockRotation;

        public SparkParticle(Vector2 position, Vector2 velocity, Color color,
            float length, float thickness, int lifetime)
        {
            Position = position;
            Velocity = velocity;
            Color = color;
            Lifetime = lifetime;
            LengthScale = length;
            ThicknessScale = thickness;
            Drag = 0.9f;
        }

        public override void Update()
        {
            base.Update();
            if (!LockRotation && Velocity.LengthSquared() > 0.0001f)
                Rotation = Velocity.ToRotation();
        }

        public override void Draw(SpriteBatch sb)
        {
            Texture2D tex = ParticleSystem.GlowStreak;
            if (tex == null) return;

            float life = LifeRatio;
            float fade = MathHelper.Clamp(1f - life, 0f, 1f);
            // Sparks elongate as they slow (head stretches relative to tail).
            float lenMul = 1f + life * 0.4f;

            Vector2 drawPos = Position - Main.screenPosition;
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);

            // Texture is 128x32, so scale.X == LengthScale gives a total length
            // of LengthScale * 128 world pixels unless we renormalize.
            Vector2 scale = new Vector2(
                LengthScale * lenMul / tex.Width * 64f,
                ThicknessScale / tex.Height * 16f);

            Color draw = Color * fade;
            sb.Draw(tex, drawPos, null, draw, Rotation, origin, scale, SpriteEffects.None, 0f);

            // Lighter core, but keep most of the purple tint -- the old
            // 70% white lerp washed everything out to near-white under
            // additive blending.
            Color core = Color.Lerp(Color, Color.White, 0.2f) * fade * 0.7f;
            sb.Draw(tex, drawPos, null, core, Rotation, origin, scale * new Vector2(0.6f, 0.5f),
                SpriteEffects.None, 0f);
        }
    }
}
