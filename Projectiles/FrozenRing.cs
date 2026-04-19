using System.Collections.Generic;
using Microsoft.Xna.Framework;
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

        // Set the frame the throw phase ends; the sword then orbits this point.
        private Vector2 orbitCenter;
        private float orbitAngle;
        private bool orbitStarted;

        // Interior AoE: periodically damage all enemies inside the ring.
        private const int InteriorDamageInterval = 18;
        private int interiorDamageTimer;

        public override string Texture => "BDOhehe/Items/Weapons/Awaken/Sting";

        public override void SetDefaults()
        {
            Projectile.width = 60;
            Projectile.height = 30;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.tileCollide = false;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.timeLeft = 240; // overridden by the item on spawn
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
                // On the first frame of the orbit phase, lock in the center point
                // and compute the starting angle from wherever the sword landed.
                if (!orbitStarted)
                {
                    Vector2 throwDir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
                    orbitCenter = Projectile.Center + throwDir * OrbitRadius;
                    orbitAngle = (Projectile.Center - orbitCenter).ToRotation();
                    orbitStarted = true;
                }

                orbitAngle += OrbitAngularSpeed;

                Vector2 radial = orbitAngle.ToRotationVector2();
                Projectile.Center = orbitCenter + radial * OrbitRadius;
                Projectile.velocity = Vector2.Zero;

                // Orient the blade tangent to the circle (tip leading the motion).
                float tangentAngle = orbitAngle + MathHelper.PiOver2;
                Projectile.rotation = tangentAngle - SpriteNaturalAngle;

                // Dense visual explosions inside the ring + AoE damage tick.
                SpawnInteriorExplosions();
                Lighting.AddLight(orbitCenter, 0.9f, 0.35f, 1.15f);

                if (--interiorDamageTimer <= 0)
                {
                    DamageInteriorNPCs(owner);
                    interiorDamageTimer = InteriorDamageInterval;
                }
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

        // Wispy purple aura drifting off the blade. Uses the shared palette
        // so clouds vary across the full violet->orchid range instead of a
        // single near-white tint.
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
