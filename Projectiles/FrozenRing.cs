using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using BDOhehe.Particles;

namespace BDOhehe.Projectiles
{
    // Thrown Sting blade for the Frozen Ring skill.
    // Phase 1 (ai[0] < ThrowFrames): flies outward toward the cursor.
    // Phase 2 (ai[0] >= ThrowFrames): hovers in place and spins rapidly,
    // emitting purple particles ranging from very light to very dark purple.
    //
    // This projectile also owns the player's position-lock and the "any
    // button cancels" logic. Doing it here (instead of in the item) means the
    // lock keeps working even if Terraria's Shift-auto-tool feature swaps the
    // held item off of Sting mid-skill -- ModItem.HoldItem stops being called
    // in that case, but the projectile's AI keeps running.
    public class FrozenRing : ModProjectile
    {
        private const int ThrowFrames = 18;
        // Orbit radius: ~4 blocks (1 block = 16 px).
        private const float OrbitRadius = 200f;
        // Radians per frame the sword travels around the orbit center.
        private const float OrbitAngularSpeed = 0.40f;
        // Sprite's natural tip angle (tip is drawn pointing top-right).
        private const float SpriteNaturalAngle = -MathHelper.PiOver4;

        public Vector2 LockPosition;
        public int Grace;
        public HashSet<Keys> StartKeys = new HashSet<Keys>();
        public bool StartMouseLeft;
        public bool StartMouseRight;

        // Wind-up sound slot owned by the Sting item; stopped in OnKill so
        // cancelling the ring (click-cancel, Comet cancel, or new-swing
        // cancel) immediately silences the audio.
        public SlotId SoundSlot;

        // Buffer before mouse-down allows skill cancellation (prevents accidental activation).
        private const int SkillCancelBufferFrames = 6;
        public int SkillCancelBuffer = SkillCancelBufferFrames;

        // Set the frame the throw phase ends; the sword then spins in place.
        private Vector2 orbitCenter;
        private bool orbitStarted;
        private int spinFrameCount;
        private const int TotalSpinFrames = 180; // ~3 seconds at 60fps

        // Interior AoE: periodically damage all enemies inside the ring.
        private const int InteriorDamageInterval = 18;
        private int interiorDamageTimer;

        // Quick visual fade at the end of the spin phase so the blade and
        // ring don't pop out of existence. Triggers automatically during the
        // last FadeOutFrames of Projectile.timeLeft.
        private const int FadeOutFrames = 14;

        public override string Texture
        {
            get
            {
                // During the spin phase (after throw), use Sting2.png
                if (Projectile.ai[0] >= ThrowFrames)
                    return "BDOhehe/Items/Weapons/Awaken/Sting2";
                return "BDOhehe/Items/Weapons/Awaken/Sting";
            }
        }

        public override void SetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.scale = 0.2f;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.tileCollide = false;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.timeLeft = 240; // overridden by the item on spawn
        }

        // Fade factor in [0,1]. 1 during normal play, ramps to 0 across the
        // last FadeOutFrames of the projectile's lifetime.
        private float FadeAlpha =>
            Projectile.timeLeft >= FadeOutFrames
                ? 1f
                : MathHelper.Clamp(Projectile.timeLeft / (float)FadeOutFrames, 0f, 1f);

        public override bool PreDraw(ref Color lightColor)
        {
            // Use Sting2 during spin phase, Sting otherwise
            string texturePath = Projectile.ai[0] >= ThrowFrames
                ? "BDOhehe/Items/Weapons/Awaken/Sting2"
                : "BDOhehe/Items/Weapons/Awaken/Sting";

            Texture2D texture = ModContent.Request<Texture2D>(texturePath).Value;
            Rectangle sourceRect = new Rectangle(0, 0, texture.Width, texture.Height);
            Vector2 drawOrigin = new Vector2(texture.Width / 2, texture.Height / 2);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            float fade = FadeAlpha;

            // Sword sprite fades along with the ring.
            Main.spriteBatch.Draw(texture, drawPos, sourceRect,
                Projectile.GetAlpha(lightColor) * fade,
                Projectile.rotation, drawOrigin, Projectile.scale, SpriteEffects.None, 0f);

            // Draw continuously glowing ring during spin phase, matching the
            // additive-glow treatment used by StarCall / StarfallCone (tangent
            // GlowStreak segments layered into a soft halo, main amethyst
            // body, and a bright orchid inner line).
            if (Projectile.ai[0] >= ThrowFrames && fade > 0f)
            {
                DrawGlowRing(orbitCenter, OrbitRadius, fade);
            }

            return false; // Skip default drawing
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];

            // Lock the owner in place every frame the projectile is alive.
            // Runs regardless of what item the player is currently holding.
            if (owner.active && !owner.dead)
            {
                owner.position = LockPosition;
                owner.velocity = Vector2.Zero;
                owner.fallStart = (int)(owner.position.Y / 16f);
                owner.fallStart2 = owner.fallStart;
                owner.gfxOffY = 0f;
                owner.itemAnimation = 0;
                owner.itemTime = 0;
            }

            if (Projectile.ai[0] < ThrowFrames)
            {
                // Still flying outward -- orient the blade along travel direction.
                Projectile.ai[0] += 1f;
                if (Projectile.velocity.LengthSquared() > 0.01f)
                {
                    Projectile.rotation = Projectile.velocity.ToRotation() - SpriteNaturalAngle;
                }
            }
            else
            {
                // On the first frame of the spin phase, lock in the center point.
                if (!orbitStarted)
                {
                    Vector2 throwDir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
                    orbitCenter = Projectile.Center + throwDir * OrbitRadius;
                    orbitStarted = true;
                }

                // Keep the projectile at the center and spin the sprite itself
                Projectile.Center = orbitCenter;
                Projectile.velocity = Vector2.Zero;

                // Increment spin frame counter
                spinFrameCount++;
                Projectile.ai[0] += 1f;

                // Spin the sprite while also scaling it up (100% increase over full duration)
                float spinProgress = (float)spinFrameCount / TotalSpinFrames;
                float scaleMultiplier = 2.5f + spinProgress; // 1.0x to 2.0x

                // Continuous rotation at high speed
                Projectile.rotation += OrbitAngularSpeed * 1.2f;

                // Apply scaled size
                Projectile.scale = 0.2f * scaleMultiplier;

                // During the fade-out window, stop spawning fresh interior
                // bursts / damage ticks and dim the emitted light so the ring
                // visibly quiets down in lockstep with the visual fade.
                bool fading = Projectile.timeLeft < FadeOutFrames;
                if (!fading)
                {
                    SpawnInteriorExplosions();

                    if (--interiorDamageTimer <= 0)
                    {
                        DamageInteriorNPCs(owner);
                        interiorDamageTimer = InteriorDamageInterval;
                    }
                }
                float lightFade = FadeAlpha;
                Lighting.AddLight(orbitCenter, 0.9f * lightFade, 0.35f * lightFade, 1.15f * lightFade);
            }

            // Any-button cancel. Only the local player reads its own input.
            if (Projectile.owner == Main.myPlayer)
            {
                if (Grace > 0)
                {
                    Grace--;
                }
                else if (DetectCancelInput())
                {
                    Projectile.Kill();
                    return;
                }
            }

            // Halt the wispy blade aura once the fade has begun so puffs
            // don't linger visibly after the projectile dies.
            if (Projectile.timeLeft >= FadeOutFrames)
                EmitPurpleParticles();

            // Decrement skill cancel buffer
            if (SkillCancelBuffer > 0)
                SkillCancelBuffer--;
        }

        public override void OnKill(int timeLeft)
        {
            if (SoundSlot.IsValid && SoundEngine.TryGetActiveSound(SoundSlot, out var activeSound))
            {
                activeSound?.Stop();
            }
        }

        // Cancel only on a new mouse click. Key presses (WASD, Space, chat,
        // hotbar swaps, etc.) don't kill the Frozen Ring, which makes the cancel
        // intent an explicit click rather than any incidental input.
        private bool DetectCancelInput()
        {
            if (Main.mouseLeft && !StartMouseLeft) return true;
            if (Main.mouseRight && !StartMouseRight) return true;
            return false;
        }

        // Smoky purple cloud puffs rolling around inside the orbit ring.
        // Replaces the old starburst explosion pattern -- this reads as
        // a roiling storm of cloud rather than discrete firework pops.
        private void SpawnInteriorExplosions()
        {
            int burstCount = 3;
            for (int b = 0; b < burstCount; b++)
            {
                float r = Main.rand.NextFloat() * Main.rand.NextFloat() * (OrbitRadius - 18f);
                float a = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 burstCenter = orbitCenter + new Vector2(
                    (float)System.Math.Cos(a) * r,
                    (float)System.Math.Sin(a) * r);

                // A few small cloud puffs drifting outward from each burst point.
                int puffCount = 5;
                for (int i = 0; i < puffCount; i++)
                {
                    float ang = Main.rand.NextFloat(MathHelper.TwoPi);
                    Vector2 dir = ang.ToRotationVector2();
                    float speed = Main.rand.NextFloat(0.6f, 2.8f);
                    var p = new SmokeParticle(
                        burstCenter + Main.rand.NextVector2Circular(4f, 4f),
                        dir * speed,
                        PurplePalette.RandomCloud(),
                        1.6f + Main.rand.NextFloat() * 1.2f,
                        44 + Main.rand.Next(16));
                    p.GrowthAt1 = 2.0f + Main.rand.NextFloat() * 0.6f;
                    ParticleSystem.Spawn(p);
                }

                // A couple of dark "ink" puffs for depth -- bias the cloud
                // body darker so overlapping additive puffs don't all average
                // to the bright end.
                for (int i = 0; i < 2; i++)
                {
                    var p = new SmokeParticle(
                        burstCenter + Main.rand.NextVector2Circular(6f, 6f),
                        Main.rand.NextVector2Circular(0.8f, 0.8f),
                        PurplePalette.RandomDeep(),
                        2.0f + Main.rand.NextFloat() * 0.8f,
                        48 + Main.rand.Next(16));
                    p.GrowthAt1 = 1.6f;
                    ParticleSystem.Spawn(p);
                }

                Lighting.AddLight(burstCenter, 0.6f, 0.18f, 0.9f);
            }
        }

        // True only for the one tick the interior AoE's Projectile.Damage()
        // call is running, so the orbiting blade's normal sprite hits aren't
        // affected by the temporary hitbox expansion below.
        private bool interiorDetonating;

        // Apply a damage tick to every enemy NPC currently inside the orbit
        // circle. Uses the canonical "expand hitbox + Projectile.Damage()"
        // pattern instead of Player.ApplyDamageToNPC so the hit flows through
        // the normal projectile damage pipeline (tModLoader modifiers, on-hit
        // callbacks, etc.) and reliably lands on every valid target.
        private void DamageInteriorNPCs(Player owner)
        {
            int origW = Projectile.width;
            int origH = Projectile.height;
            Vector2 origPos = Projectile.position;
            int origPenetrate = Projectile.penetrate;

            int side = (int)(OrbitRadius * 2f);
            Projectile.position = orbitCenter - new Vector2(OrbitRadius);
            Projectile.width = side;
            Projectile.height = side;
            Projectile.penetrate = -1;

            // Let every NPC be re-hit by each interior tick regardless of the
            // usesLocalNPCImmunity cooldown set for the blade's sprite hits.
            for (int i = 0; i < Projectile.localNPCImmunity.Length; i++)
                Projectile.localNPCImmunity[i] = 0;

            interiorDetonating = true;
            Projectile.Damage();
            interiorDetonating = false;

            Projectile.position = origPos;
            Projectile.width = origW;
            Projectile.height = origH;
            Projectile.penetrate = origPenetrate;
        }

        // During the interior detonation tick we want the engine's default
        // rect-vs-rect test to use the temporarily expanded hitbox. Outside
        // that window, fall back to the default (null) behaviour so the
        // orbiting blade's sprite hits still work normally.
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (interiorDetonating)
            {
                // Extra precision: clip targets outside the orbit circle so
                // the AoE reads as a ring rather than a square box.
                Vector2 closest = Vector2.Clamp(orbitCenter, targetHitbox.TopLeft(), targetHitbox.BottomRight());
                float radiusSq = OrbitRadius * OrbitRadius;
                return Vector2.DistanceSquared(closest, orbitCenter) <= radiusSq;
            }
            return null;
        }

        // Continuously glowing full ring built from tangent GlowStreak
        // segments under additive blending. Three layered passes (soft deep
        // violet halo, amethyst main body, bright orchid inner line) give the
        // same "energy beam" feel as StarCall / StarfallCone's cone bodies,
        // and a subtle sine pulse keeps it breathing while the ring spins.
        private void DrawGlowRing(Vector2 center, float radius, float fade)
        {
            Texture2D glow = ParticleSystem.GlowStreak;
            Texture2D orb = ParticleSystem.GlowOrb;
            if (glow == null || orb == null) return;

            SpriteBatch sb = Main.spriteBatch;

            // Switch to additive blending so overlapping glows brighten like
            // energy instead of tinting like paint. Restore the projectile
            // layer's normal AlphaBlend batch afterward.
            sb.End();
            sb.Begin(
                SpriteSortMode.Deferred, BlendState.Additive,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null,
                Main.GameViewMatrix.TransformationMatrix);

            Vector2 glowOrigin = new Vector2(glow.Width * 0.5f, glow.Height * 0.5f);
            Vector2 drawCenter = center - Main.screenPosition;

            // Segment count scales with circumference so the streaks stay
            // overlapped even at larger radii. Each streak is oriented
            // tangent to the ring and sized to cover the arc between
            // neighbours (with generous overlap) so the ring reads as a
            // continuous band rather than discrete pips.
            int segments = 72;
            float arcPx = MathHelper.TwoPi * radius / segments;
            float streakLength = arcPx * 1.8f;

            // Real-time traveling glow: each segment has its own brightness
            // driven by a phase based on its angular position around the
            // ring. The phase sweeps over time, so a bright "wave" (actually
            // two waves, 180 degrees apart) chases itself around the circle
            // continuously -- no two segments share the same brightness at
            // the same instant, producing a live gradient rather than a
            // uniform pulse.
            float t = Main.GlobalTimeWrappedHourly;
            // Global flicker still applies on top for organic variation.
            float flicker = 0.92f + 0.08f * (float)System.Math.Sin(t * 17f);
            // Speed of the traveling wave around the ring (rad/sec) and a
            // multiplier on the angle to get two crests per lap.
            const float WaveSpeed = 2.4f;
            const int WaveLobes = 2;

            float baseStreakScaleX = streakLength / glow.Width;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * MathHelper.TwoPi;
                Vector2 dir = new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle));
                Vector2 pos = drawCenter + dir * radius;
                float tangent = angle + MathHelper.PiOver2;

                // Per-segment breath: 0 at the wave's trough, 1 at its crest.
                float wave = 0.5f + 0.5f * (float)System.Math.Sin(angle * WaveLobes - t * WaveSpeed);
                // Second, slower, offset wave shifts the minimum so no single
                // spot sits in the dark for long.
                float wave2 = 0.5f + 0.5f * (float)System.Math.Sin(angle * 3f + t * 1.3f);
                float breath = MathHelper.Clamp(wave * 0.75f + wave2 * 0.35f, 0f, 1f);

                float pulse = MathHelper.Lerp(0.25f, 1.25f, breath) * flicker;
                float thick = MathHelper.Lerp(0.8f, 1.25f, breath);

                Vector2 haloScale = new Vector2(baseStreakScaleX, (28f * thick) / glow.Height);
                Vector2 bodyScale = new Vector2(baseStreakScaleX, (14f * thick) / glow.Height);
                Vector2 innerScale = new Vector2(baseStreakScaleX, (5f * thick) / glow.Height);

                // Outer soft halo -- breathes the most so the aura swells.
                sb.Draw(glow, pos, null, PurplePalette.DeepViolet * 0.55f * pulse * fade,
                    tangent, glowOrigin, haloScale, SpriteEffects.None, 0f);

                // Main amethyst ring body.
                sb.Draw(glow, pos, null, PurplePalette.Amethyst * 0.75f * pulse * fade,
                    tangent, glowOrigin, bodyScale, SpriteEffects.None, 0f);

                // Bright orchid inner line -- never fully fades so the ring
                // silhouette stays readable even at the wave's trough.
                float innerAlpha = MathHelper.Lerp(0.45f, 1.0f, breath) * flicker;
                sb.Draw(glow, pos, null, PurplePalette.Orchid * innerAlpha * fade,
                    tangent, glowOrigin, innerScale, SpriteEffects.None, 0f);
            }

            sb.End();
            sb.Begin(
                SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null,
                Main.GameViewMatrix.TransformationMatrix);
        }

        private void EmitPurpleParticles()
        {
            for (int i = 0; i < 3; i++)
            {
                Vector2 offset = Main.rand.NextVector2Circular(38f, 38f);
                float scale = 1.0f + Main.rand.NextFloat() * 1.0f;

                Color color = Main.rand.NextBool(4)
                    ? PurplePalette.RandomHighlight()
                    : PurplePalette.RandomCloud();

                var p = new SmokeParticle(
                    Projectile.Center + offset,
                    offset.SafeNormalize(Vector2.Zero) * Main.rand.NextFloat(0.4f, 2.2f),
                    color,
                    scale,
                    28 + Main.rand.Next(10));
                p.GrowthAt1 = 1.7f;
                ParticleSystem.Spawn(p);
            }
        }
    }
}
