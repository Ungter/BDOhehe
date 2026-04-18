using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

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

        // Cancel only on a new mouse click. Key presses (WASD, Space, chat,
        // hotbar swaps, etc.) don't kill the Frozen Ring, which makes the cancel
        // intent an explicit click rather than any incidental input.
        private bool DetectCancelInput()
        {
            if (Main.mouseLeft && !StartMouseLeft) return true;
            if (Main.mouseRight && !StartMouseRight) return true;
            return false;
        }

        // Spawn several dense burst clusters per frame at random points inside
        // the orbit circle so the ring's interior looks like it's being
        // continuously detonated with purple energy.
        private void SpawnInteriorExplosions()
        {
            Color veryLight = new Color(235, 215, 255);
            Color midPurple = new Color(180, 80, 230);
            Color veryDark = new Color(40, 5, 70);

            // Number of explosion centers per frame.
            int burstCount = 3;
            for (int b = 0; b < burstCount; b++)
            {
                // Random point strictly inside the orbit (bias toward the middle).
                float r = Main.rand.NextFloat() * Main.rand.NextFloat() * (OrbitRadius - 18f);
                float a = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 burstCenter = orbitCenter + new Vector2(
                    (float)System.Math.Cos(a) * r,
                    (float)System.Math.Sin(a) * r);

                // Core burst: fast outward purple sparks.
                int core = 10;
                for (int i = 0; i < core; i++)
                {
                    float ang = MathHelper.TwoPi * i / core + Main.rand.NextFloat(-0.2f, 0.2f);
                    Vector2 dir = ang.ToRotationVector2();
                    Color col = Color.Lerp(veryLight, midPurple, Main.rand.NextFloat());
                    Dust d = Dust.NewDustPerfect(
                        burstCenter,
                        DustID.PurpleTorch,
                        dir * Main.rand.NextFloat(2.5f, 6f),
                        100,
                        col,
                        1.5f + Main.rand.NextFloat() * 0.6f);
                    d.noGravity = true;
                    d.fadeIn = 1.1f;
                }

                // Dark afterburn at the center.
                for (int i = 0; i < 4; i++)
                {
                    Dust d = Dust.NewDustPerfect(
                        burstCenter + Main.rand.NextVector2Circular(6f, 6f),
                        DustID.PurpleTorch,
                        Main.rand.NextVector2Circular(1.5f, 1.5f),
                        100,
                        Color.Lerp(midPurple, veryDark, Main.rand.NextFloat()),
                        1.4f);
                    d.noGravity = true;
                }

                // Brief point light at the burst center for a flash effect.
                Lighting.AddLight(burstCenter, 0.8f, 0.25f, 1.0f);
            }
        }

        // Apply a damage tick to every enemy NPC currently inside the orbit
        // circle. Mirrors the sword projectile's damage number so interior
        // damage feels equivalent to a direct blade hit.
        private void DamageInteriorNPCs(Player owner)
        {
            float radiusSq = OrbitRadius * OrbitRadius;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.friendly || npc.dontTakeDamage ||
                    npc.immortal || npc.townNPC || npc.CountsAsACritter)
                    continue;

                // Any part of the NPC hitbox inside the circle counts.
                Vector2 closest = Vector2.Clamp(orbitCenter, npc.TopLeft, npc.BottomRight);
                if (Vector2.DistanceSquared(closest, orbitCenter) > radiusSq)
                    continue;

                int hitDir = npc.Center.X > orbitCenter.X ? 1 : -1;
                owner.ApplyDamageToNPC(
                    npc,
                    Projectile.damage,
                    Projectile.knockBack * 0.5f,
                    hitDir,
                    false,
                    DamageClass.Melee);
            }
        }

        private void EmitPurpleParticles()
        {
            // Very light purple -> very dark purple gradient
            Color veryLight = new Color(235, 215, 255);
            Color veryDark = new Color(30, 0, 50);

            for (int i = 0; i < 3; i++)
            {
                float t = Main.rand.NextFloat();
                Color dustColor = Color.Lerp(veryLight, veryDark, t);

                Vector2 offset = Main.rand.NextVector2Circular(38f, 38f);
                float scale = 1.0f + Main.rand.NextFloat() * 1.0f;

                Dust dust = Dust.NewDustPerfect(
                    Projectile.Center + offset,
                    DustID.PurpleTorch,
                    offset.SafeNormalize(Vector2.Zero) * Main.rand.NextFloat(0.4f, 2.2f),
                    100,
                    dustColor,
                    scale);
                dust.noGravity = true;
                dust.fadeIn = 0.8f + Main.rand.NextFloat() * 0.6f;
            }
        }
    }
}
