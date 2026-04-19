using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using BDOhehe.Particles;

namespace BDOhehe.Projectiles
{
    // Starfall skill projectile. Five of these are summoned above the player's head,
    // each pointing at the initial cursor position. After a short delay they launch
    // toward that fixed initial cursor position. If the skill is cancelled by another
    // skill, they fade out in place (continuing any motion they already had).
    public class StarfallCone : ModProjectile
    {
        private const int DelayFrames = 6;
        private const float LaunchSpeed = 22f;
        private const int FadeFrames = 18;
        private const float AboveHeadDistance = 90f;
        private const float ConeSpacing = 26f;
        private const int MaxTravelFramesAfterLaunch = 90;
        // Radius (px) of the AoE damage burst when the cone explodes on tile contact.
        // Tuned to match the outer extent of SpawnImpactBurst's visible cloud
        // (mid/outer layers with drag-decayed travel + ~half particle width)
        // so the damage circle matches what the player actually sees.
        private const int ExplosionRadius = 96;
        // Length of the visual cone; used for tile sampling + line-segment collision.
        private const float ConeHalfLength = 18f;
        private const float ConeHitThickness = 10f;

        // Star's Call only: a second, larger AoE detonates partway through
        // the fade (< FadeFrames so total animation time is unchanged). It
        // applies its own damage tick (localNPC immunity is reset) so enemies
        // are hit twice. Radius is sized to the visible extent of
        // SpawnSecondaryImpactBurst (bigger particle scales + faster halo).
        private const int SecondaryExplosionDelayFrames = 12;
        private const int SecondaryExplosionRadius = 200;
        private const float SecondaryDamageMultiplier = 1.4f;

        public Vector2 TargetPosition;
        public int ConeIndex; // 0..4
        public bool Fading;

        // Star's Call variant: cones spawn in a crown above the cursor and
        // strike down. When true, we skip the per-frame "hover above player"
        // repositioning and just respect the spawn position given by the caller.
        public bool StrikeDownMode;

        // When > 0, overrides the built-in DelayFrames so other skills (e.g.
        // Star's Call) can use a different delay length.
        public int DelayOverride;

        // For Star's Call only: wait this many delay frames before the cone
        // becomes visible / emits trails. Lets the crown fill in left-to-right
        // across the wind-up instead of appearing all at once. Unused for
        // regular Starfall (stays 0 = immediate).
        public int RevealFrame;

        private int delayCounter;
        private int fadeCounter;
        private int launchedFrames;
        private bool launched;
        // Star's Call secondary explosion bookkeeping.
        private bool secondaryPending;
        private int secondaryCounter;
        // True only for the single AI tick the secondary's Projectile.Damage()
        // call is running. Lets CanHitNPC return the default (allow) even
        // though the cone is otherwise in the Fading state.
        private bool secondaryDetonating;
        private Vector2 launchDirection;
        private Vector2 launchOrigin;
        private float targetTravelDistance;
        // Previous AI tick's center. Used by EmitTrail to fill the gap
        // between frames when the cone is moving at launch speed (22f/frame)
        // so the trail reads as continuous rather than as discrete puffs.
        private Vector2 lastCenter;
        private bool hasLastCenter;

        // Autoload wants a valid texture path. We never actually draw the item sprite
        // because PreDraw returns false and paints a custom slim cone instead.
        public override string Texture => "BDOhehe/Items/Weapons/Awaken/Sting";

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            // Collides with tiles only after launch; during the hover phase we set
            // position directly (velocity = 0) so no move step runs and spawning
            // near/inside ceiling tiles won't cause an instant collision.
            Projectile.tileCollide = true;
            // The cone explodes on NPC impact (see OnHitNPC) and on tile impact,
            // so penetrate is set high and explosion-on-hit ends the projectile.
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 300;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];

            if (Fading)
            {
                fadeCounter++;
                EmitTrail(0.35f);

                // Secondary Star's Call explosion -- larger radius, bonus
                // damage tick. Scheduled inside the existing fade window so
                // total animation time is unchanged.
                if (secondaryPending)
                {
                    secondaryCounter--;
                    if (secondaryCounter <= 0)
                    {
                        SecondaryExplode();
                        secondaryPending = false;
                    }
                }

                // If launched, the engine continues moving by Projectile.velocity on its own.
                if (fadeCounter >= FadeFrames)
                {
                    Projectile.Kill();
                    return;
                }
                Lighting.AddLight(Projectile.Center, 0.5f, 0.15f, 0.7f);
                return;
            }

            Vector2 toTarget = (TargetPosition - owner.Center).SafeNormalize(new Vector2(0, -1));

            int effectiveDelay = DelayOverride > 0 ? DelayOverride : DelayFrames;

            if (delayCounter < effectiveDelay)
            {
                if (!StrikeDownMode)
                {
                    // Starfall hover: ride above the player's head in a row.
                    Vector2 aboveHead = owner.Center + new Vector2(0, -AboveHeadDistance);
                    float horizontalOffset = (ConeIndex - 2) * ConeSpacing;
                    Projectile.Center = aboveHead + new Vector2(horizontalOffset, 0f);
                }
                // else: StrikeDownMode keeps the crown position assigned at spawn.

                Projectile.velocity = Vector2.Zero;

                // Point at the fixed target position.
                Vector2 fallback = StrikeDownMode ? Vector2.UnitY : toTarget;
                launchDirection = (TargetPosition - Projectile.Center).SafeNormalize(fallback);
                Projectile.rotation = launchDirection.ToRotation();

                delayCounter++;

                // Star's Call: stay completely silent/invisible until this
                // cone's reveal frame so the crown appears in sequence.
                if (StrikeDownMode && delayCounter <= RevealFrame)
                {
                    // Reset trail anchor so the first visible frame doesn't
                    // interpolate a trail line from off-screen.
                    lastCenter = Projectile.Center;
                    hasLastCenter = true;
                    return;
                }
            }
            else
            {
                if (!launched)
                {
                    Projectile.velocity = launchDirection * LaunchSpeed;
                    launchOrigin = Projectile.Center;
                    // Remember how far we need to fly; comparing the traveled
                    // distance against this is robust against overshoot (the
                    // previous "distance to target < step" check could miss
                    // when the projectile skipped past the target).
                    targetTravelDistance = Vector2.Distance(launchOrigin, TargetPosition);
                    launched = true;
                }

                launchedFrames++;
                if (Projectile.velocity.LengthSquared() > 0.01f)
                    Projectile.rotation = Projectile.velocity.ToRotation();

                // Belt-and-suspenders tile check. OnTileCollide only fires when
                // the engine actually clamps movement; a thin cone can "skate"
                // along a tile surface or shave a corner without triggering it.
                // We sample several points along the cone's visible length and
                // detonate if any of them is inside a solid tile.
                if (IsConeLineTouchingSolidTile())
                {
                    Explode();
                    EmitTrail(1f);
                    Lighting.AddLight(Projectile.Center, 0.6f, 0.2f, 0.85f);
                    return;
                }

                // Destination is fixed to the initial cursor world position.
                // If the cone travels the whole way without hitting an NPC or
                // a tile, it detonates exactly on the activation cursor point.
                //
                // We measure progress as the dot product of the cone's offset
                // from its launch origin with the launch direction. This is a
                // *signed* distance along the cone's flight axis and is robust
                // to deflections (e.g. grazing a tile slightly slows/redirects
                // the projectile -- a plain straight-line distance check can
                // then saturate below targetTravelDistance and never trigger
                // while the cone still visibly moves past the cursor).
                float progressAlongLaunch = Vector2.Dot(
                    Projectile.Center - launchOrigin, launchDirection);
                if (progressAlongLaunch >= targetTravelDistance ||
                    launchedFrames > MaxTravelFramesAfterLaunch)
                {
                    Projectile.Center = TargetPosition;
                    Explode();
                }
            }

            EmitTrail(1f);
            Lighting.AddLight(Projectile.Center, 0.6f, 0.2f, 0.85f);
        }

        // Block contact detonates the cone in-place with an AoE burst.
        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            Explode();
            // Return false so the engine doesn't also kill the projectile; the
            // fade-out handles its removal so the explosion can animate.
            return false;
        }

        // NPC contact also detonates the cone with an AoE burst instead of just
        // dying silently on first-hit penetrate.
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Explode();
        }

        // Use the cone's visible length + thickness as the real hit volume
        // instead of the default 16x16 AABB. Without this, the slim sprite
        // looks like it touches an NPC but no hit is registered.
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Let the engine's default rectangle-vs-rectangle test run during
            // the secondary detonation so the expanded hitbox actually hits.
            if (secondaryDetonating) return null;
            if (Fading) return false;
            if (!launched && delayCounter < DelayFrames) return false;

            Vector2 dir = Projectile.rotation.ToRotationVector2();
            Vector2 start = Projectile.Center - dir * ConeHalfLength;
            Vector2 end = Projectile.Center + dir * ConeHalfLength;
            float _ = 0f;
            return Collision.CheckAABBvLineCollision(
                targetHitbox.TopLeft(), targetHitbox.Size(),
                start, end, ConeHitThickness, ref _);
        }

        // Samples points spaced along the visible cone line and reports a tile
        // touch if any sample is inside an active solid tile.
        private bool IsConeLineTouchingSolidTile()
        {
            // Cheap AABB test first (fast early-out when clearly in open air).
            if (Collision.SolidCollision(Projectile.position, Projectile.width, Projectile.height))
                return true;

            Vector2 dir = Projectile.rotation.ToRotationVector2();
            // Sample 5 points: -half, -half/2, center, +half/2, +half.
            for (int i = -2; i <= 2; i++)
            {
                Vector2 sample = Projectile.Center + dir * (ConeHalfLength * (i / 2f));
                int tx = (int)(sample.X / 16f);
                int ty = (int)(sample.Y / 16f);
                if (tx < 0 || ty < 0 || tx >= Main.maxTilesX || ty >= Main.maxTilesY)
                    continue;
                Tile tile = Main.tile[tx, ty];
                if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    return true;
            }
            return false;
        }

        // Apply AoE damage using an expanded hitbox, spawn a big purple burst,
        // zero out velocity, and kick off the fade.
        private void Explode()
        {
            if (Fading) return;

            int origW = Projectile.width;
            int origH = Projectile.height;
            int origPenetrate = Projectile.penetrate;
            Vector2 origPos = Projectile.position;
            Vector2 center = Projectile.Center;

            // Expand to a square hitbox of side 2*ExplosionRadius around the
            // explosion center, then let Projectile.Damage() hit everything in it.
            Projectile.position = center - new Vector2(ExplosionRadius);
            Projectile.width = ExplosionRadius * 2;
            Projectile.height = ExplosionRadius * 2;
            Projectile.penetrate = -1;
            Projectile.Damage();

            // Restore so the fading visuals match the slim-cone silhouette.
            Projectile.position = origPos;
            Projectile.width = origW;
            Projectile.height = origH;
            Projectile.penetrate = origPenetrate;

            SpawnImpactBurst();

            Projectile.velocity = Vector2.Zero;
            Fading = true;

            // Star's Call: arm a larger secondary detonation that will fire
            // partway through the fade (stays within the existing fade window
            // so overall skill timing is unchanged).
            if (StrikeDownMode)
            {
                secondaryPending = true;
                secondaryCounter = SecondaryExplosionDelayFrames;
            }
        }

        // Bigger follow-up blast for Star's Call. Temporarily expands the
        // hitbox and bumps damage, resets per-NPC immunity so enemies already
        // hit by the primary take another tick, then restores everything.
        private void SecondaryExplode()
        {
            int origW = Projectile.width;
            int origH = Projectile.height;
            int origPenetrate = Projectile.penetrate;
            int origDamage = Projectile.damage;
            Vector2 origPos = Projectile.position;
            Vector2 center = Projectile.Center;

            // Allow this explosion to re-hit NPCs already struck by the primary.
            for (int i = 0; i < Projectile.localNPCImmunity.Length; i++)
                Projectile.localNPCImmunity[i] = 0;

            Projectile.position = center - new Vector2(SecondaryExplosionRadius);
            Projectile.width = SecondaryExplosionRadius * 2;
            Projectile.height = SecondaryExplosionRadius * 2;
            Projectile.penetrate = -1;
            Projectile.damage = (int)(origDamage * SecondaryDamageMultiplier);

            // Open the CanHitNPC / Colliding gates for exactly this call so
            // the fading cone can still deal damage with its secondary burst.
            secondaryDetonating = true;
            Projectile.Damage();
            secondaryDetonating = false;

            Projectile.position = origPos;
            Projectile.width = origW;
            Projectile.height = origH;
            Projectile.penetrate = origPenetrate;
            Projectile.damage = origDamage;

            SpawnSecondaryImpactBurst();
            Lighting.AddLight(center, 0.9f, 0.3f, 1.15f);
        }

        // Larger, denser version of SpawnImpactBurst sized to the secondary
        // radius. Scales particle counts, travel speeds, and starting radii
        // so the burst visibly dwarfs the primary without changing its
        // purple palette or additive feel.
        private void SpawnSecondaryImpactBurst()
        {
            float rMul = SecondaryExplosionRadius / (float)ExplosionRadius; // ~1.85x

            // Dense inner core cloud.
            for (int i = 0; i < 16; i++)
            {
                Vector2 vel = Main.rand.NextVector2Circular(2.2f, 2.2f);
                var p = new SmokeParticle(
                    Projectile.Center + Main.rand.NextVector2Circular(10f, 10f),
                    vel,
                    PurplePalette.RandomDeep(),
                    3.8f + Main.rand.NextFloat() * 1.2f,
                    52 + Main.rand.Next(18));
                p.GrowthAt1 = 1.8f;
                ParticleSystem.Spawn(p);
            }

            // Mid-layer billows pushed outward farther/faster.
            for (int i = 0; i < 22; i++)
            {
                float ang = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 dir = ang.ToRotationVector2();
                float speed = Main.rand.NextFloat(2.8f, 6.5f);
                var p = new SmokeParticle(
                    Projectile.Center + dir * Main.rand.NextFloat(4f, 14f * rMul),
                    dir * speed,
                    PurplePalette.RandomCloud(),
                    3.0f + Main.rand.NextFloat() * 1.2f,
                    64 + Main.rand.Next(24));
                p.GrowthAt1 = 2.6f;
                ParticleSystem.Spawn(p);
            }

            // Outer orchid/magenta halo.
            for (int i = 0; i < 18; i++)
            {
                float ang = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 dir = ang.ToRotationVector2();
                float speed = Main.rand.NextFloat(4.5f, 9f);
                var p = new SmokeParticle(
                    Projectile.Center + dir * Main.rand.NextFloat(6f, 18f * rMul),
                    dir * speed,
                    Color.Lerp(PurplePalette.Orchid, PurplePalette.Magenta, Main.rand.NextFloat()),
                    2.2f + Main.rand.NextFloat() * 0.8f,
                    44 + Main.rand.Next(16));
                p.GrowthAt1 = 3.0f;
                ParticleSystem.Spawn(p);
            }

            // Shockwave streaks radiating from the center for extra punch.
            int streaks = 10;
            for (int i = 0; i < streaks; i++)
            {
                float ang = MathHelper.TwoPi * i / streaks + Main.rand.NextFloat(-0.08f, 0.08f);
                var streak = new SparkParticle(
                    Projectile.Center,
                    Vector2.Zero,
                    Main.rand.NextBool(2) ? PurplePalette.Lavender : PurplePalette.Orchid,
                    1.6f, 0.55f,
                    14 + Main.rand.Next(6));
                streak.Rotation = ang;
                streak.LockRotation = true;
                streak.Drag = 1f;
                ParticleSystem.Spawn(streak);
            }

            // Bigger core flash than the primary.
            var flash = new GlowParticle(
                Projectile.Center, Vector2.Zero,
                PurplePalette.Amethyst, 4.8f, 18);
            flash.CoreWhiteness = 0.2f;
            flash.CoreIntensity = 0.7f;
            ParticleSystem.Spawn(flash);
        }

        // Soft purple cloud burst -- no rigid starburst, no white highlights.
        // Layered from deep core out to lighter orchid haze so the whole
        // thing reads as a roiling puff of purple smoke.
        private void SpawnImpactBurst()
        {
            // Dense inner core cloud: dark tones, slow drift.
            for (int i = 0; i < 10; i++)
            {
                Vector2 vel = Main.rand.NextVector2Circular(1.4f, 1.4f);
                var p = new SmokeParticle(
                    Projectile.Center + Main.rand.NextVector2Circular(6f, 6f),
                    vel,
                    PurplePalette.RandomDeep(),
                    2.8f + Main.rand.NextFloat() * 0.8f,
                    40 + Main.rand.Next(14));
                p.GrowthAt1 = 1.6f;
                ParticleSystem.Spawn(p);
            }

            // Mid-layer billows: royal/amethyst shades expanding outward.
            for (int i = 0; i < 14; i++)
            {
                float ang = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 dir = ang.ToRotationVector2();
                float speed = Main.rand.NextFloat(1.8f, 4.5f);
                var p = new SmokeParticle(
                    Projectile.Center + dir * Main.rand.NextFloat(2f, 10f),
                    dir * speed,
                    PurplePalette.RandomCloud(),
                    2.2f + Main.rand.NextFloat() * 1.0f,
                    50 + Main.rand.Next(20));
                p.GrowthAt1 = 2.4f;
                ParticleSystem.Spawn(p);
            }

            // Outer wispy halo: lighter orchid/magenta, fast and thin.
            for (int i = 0; i < 12; i++)
            {
                float ang = Main.rand.NextFloat(MathHelper.TwoPi);
                Vector2 dir = ang.ToRotationVector2();
                float speed = Main.rand.NextFloat(3f, 6.5f);
                var p = new SmokeParticle(
                    Projectile.Center + dir * Main.rand.NextFloat(4f, 12f),
                    dir * speed,
                    Color.Lerp(PurplePalette.Orchid, PurplePalette.Magenta, Main.rand.NextFloat()),
                    1.6f + Main.rand.NextFloat() * 0.6f,
                    36 + Main.rand.Next(14));
                p.GrowthAt1 = 2.8f;
                ParticleSystem.Spawn(p);
            }

            // A single soft bloom at the epicenter -- not pure white, just
            // a brighter amethyst to sell the detonation flash.
            var flash = new GlowParticle(
                Projectile.Center, Vector2.Zero,
                PurplePalette.Amethyst, 3.2f, 14);
            flash.CoreWhiteness = 0.15f;
            flash.CoreIntensity = 0.5f;
            ParticleSystem.Spawn(flash);
        }

        // Continuous trail. During the launched phase we draw a sharp, bright
        // streak aligned with the cone's travel direction (reads like a
        // laser/comet tail). During delay or fade, we fall back to the
        // softer smoke puff trail so the "charging" phase stays atmospheric.
        // Starfall and Star's Call both share this path via StrikeDownMode.
        private void EmitTrail(float intensity)
        {
            Vector2 from = hasLastCenter ? lastCenter : Projectile.Center;
            Vector2 to = Projectile.Center;
            float dist = Vector2.Distance(from, to);

            // Launched-and-moving = sharp streak mode; otherwise smoke.
            bool sharp = launched && !Fading && Projectile.velocity.LengthSquared() > 0.5f;

            // Sharp streaks want tighter spacing so the line reads as solid;
            // smoke can stagger farther apart.
            float stepPx = (sharp ? 3.5f : 5f) / Math.Max(0.2f, intensity);
            int steps = Math.Max(1, (int)Math.Ceiling(dist / stepPx));
            if (steps > 40)
            {
                steps = 1;
                from = to;
            }

            if (sharp)
            {
                // Travel direction stays constant across all samples this
                // tick; we lock every streak to it so the trail is a single
                // coherent bright line, not a sequence of tiny arrows.
                float travelAngle = Projectile.velocity.ToRotation();

                for (int s = 0; s < steps; s++)
                {
                    float t = steps == 1 ? 0.5f : (s + 0.5f) / steps;
                    Vector2 samplePos = Vector2.Lerp(from, to, t);

                    // Main bright streak: orchid-to-lavender core, narrow
                    // and elongated for that "energy beam" feel.
                    Color tint = Main.rand.NextBool(2)
                        ? PurplePalette.Lavender
                        : PurplePalette.Orchid;
                    var streak = new SparkParticle(
                        samplePos + Main.rand.NextVector2Circular(0.8f, 0.8f),
                        Vector2.Zero,
                        tint,
                        0.9f,          // length (~58 world px per streak)
                        0.45f,         // thickness (~7 world px)
                        10 + Main.rand.Next(4));
                    streak.Rotation = travelAngle;
                    streak.LockRotation = true;
                    streak.Drag = 1f;
                    ParticleSystem.Spawn(streak);

                    // Occasional white-hot hotspot for extra punch - only
                    // every few samples so we don't wash everything out.
                    if (s % 3 == 0)
                    {
                        var hot = new SparkParticle(
                            samplePos,
                            Vector2.Zero,
                            Color.Lerp(PurplePalette.Lavender, Color.White, 0.35f),
                            0.55f,      // shorter hot core
                            0.3f,       // thinner hot core
                            7);
                        hot.Rotation = travelAngle;
                        hot.LockRotation = true;
                        hot.Drag = 1f;
                        ParticleSystem.Spawn(hot);
                    }
                }
            }
            else
            {
                for (int s = 0; s < steps; s++)
                {
                    float t = steps == 1 ? 0.5f : (s + 0.5f) / steps;
                    Vector2 samplePos = Vector2.Lerp(from, to, t);

                    Vector2 jitter = Main.rand.NextVector2Circular(3f, 3f);
                    Vector2 velocity = launched
                        ? -Projectile.velocity * 0.12f + Main.rand.NextVector2Circular(0.6f, 0.6f)
                        : Main.rand.NextVector2Circular(0.3f, 0.3f);

                    Color tint = Main.rand.NextBool(3)
                        ? PurplePalette.RandomHighlight()
                        : PurplePalette.RandomCloud();
                    var p = new SmokeParticle(
                        samplePos + jitter,
                        velocity,
                        tint,
                        1.4f + Main.rand.NextFloat() * 0.5f,
                        22 + Main.rand.Next(8));
                    p.GrowthAt1 = 1.8f;
                    ParticleSystem.Spawn(p);
                }
            }

            // Advance the anchor for next frame. Done here (not in AI) since
            // AI has multiple return paths and EmitTrail is called on each.
            lastCenter = to;
            hasLastCenter = true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Star's Call reveal: hide the cone until its sequenced frame.
            if (StrikeDownMode && !launched && !Fading && delayCounter <= RevealFrame)
                return false;

            float fade = Fading ? 1f - (float)fadeCounter / FadeFrames : 1f;
            if (fade <= 0f) return false;

            Texture2D glow = ParticleSystem.GlowStreak;
            Texture2D orb = ParticleSystem.GlowOrb;
            if (glow == null || orb == null) return false;

            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Vector2 glowOrigin = new Vector2(glow.Width * 0.5f, glow.Height * 0.5f);
            Vector2 orbOrigin = new Vector2(orb.Width * 0.5f, orb.Height * 0.5f);

            // Switch to additive blending so overlapping glows brighten like
            // energy instead of tinting like paint. Restore the projectile
            // layer's normal AlphaBlend batch afterward.
            SpriteBatch sb = Main.spriteBatch;
            sb.End();
            sb.Begin(
                SpriteSortMode.Deferred, BlendState.Additive,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null,
                Main.GameViewMatrix.TransformationMatrix);

            // Outer halo: soft deep violet glow behind the cone.
            sb.Draw(orb, drawPos, null,
                PurplePalette.DeepViolet * 0.7f * fade,
                0f, orbOrigin, 0.75f, SpriteEffects.None, 0f);

            // Main cone body: amethyst streak along travel direction.
            Vector2 coreScale = new Vector2(48f / glow.Width, 14f / glow.Height);
            sb.Draw(glow, drawPos, null,
                PurplePalette.Amethyst * fade,
                Projectile.rotation, glowOrigin, coreScale,
                SpriteEffects.None, 0f);

            // Brighter orchid inner streak (not white -- keeps the tint
            // from washing out under additive blending).
            sb.Draw(glow, drawPos, null,
                PurplePalette.Orchid * fade,
                Projectile.rotation, glowOrigin,
                coreScale * new Vector2(0.7f, 0.45f),
                SpriteEffects.None, 0f);

            // Leading-tip highlight: lavender (lightest in the palette) so
            // it reads as a bright point without becoming pure white.
            Vector2 tipOffset = new Vector2(14f, 0f).RotatedBy(Projectile.rotation);
            sb.Draw(glow, drawPos + tipOffset, null,
                PurplePalette.Lavender * fade * 0.8f,
                Projectile.rotation, glowOrigin,
                coreScale * new Vector2(0.4f, 0.3f),
                SpriteEffects.None, 0f);

            sb.End();
            sb.Begin(
                SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null,
                Main.GameViewMatrix.TransformationMatrix);

            return false;
        }

        // Don't hit anything after we've started fading, except for the
        // single tick the Star's Call secondary explosion is firing.
        public override bool? CanHitNPC(NPC target) =>
            (Fading && !secondaryDetonating) ? false : (bool?)null;

        public override bool CanHitPvp(Player target) => !Fading || secondaryDetonating;
    }
}
