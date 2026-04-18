using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

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
        private const int ExplosionRadius = 56;
        // Length of the visual cone; used for tile sampling + line-segment collision.
        private const float ConeHalfLength = 18f;
        private const float ConeHitThickness = 10f;

        public Vector2 TargetPosition;
        public int ConeIndex; // 0..4
        public bool Fading;

        private int delayCounter;
        private int fadeCounter;
        private int launchedFrames;
        private bool launched;
        private Vector2 launchDirection;
        private Vector2 launchOrigin;
        private float targetTravelDistance;

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
            float aimAngle = toTarget.ToRotation();

            if (delayCounter < DelayFrames)
            {
                // Hover above the player's head in a horizontal row.
                Vector2 aboveHead = owner.Center + new Vector2(0, -AboveHeadDistance);
                float horizontalOffset = (ConeIndex - 2) * ConeSpacing;
                Projectile.Center = aboveHead + new Vector2(horizontalOffset, 0f);
                Projectile.velocity = Vector2.Zero;

                // Point at the fixed target position.
                launchDirection = (TargetPosition - Projectile.Center).SafeNormalize(toTarget);
                Projectile.rotation = launchDirection.ToRotation();

                delayCounter++;
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
        }

        private void SpawnImpactBurst()
        {
            // Core sparkles.
            for (int i = 0; i < 14; i++)
            {
                Dust dust = Dust.NewDustPerfect(
                    Projectile.Center,
                    DustID.PurpleTorch,
                    Main.rand.NextVector2Circular(3f, 3f),
                    100,
                    new Color(210, 130, 245),
                    1.7f);
                dust.noGravity = true;
                dust.fadeIn = 1.2f;
            }

            // Outward shockwave.
            for (int i = 0; i < 26; i++)
            {
                float angle = MathHelper.TwoPi * i / 26f;
                Vector2 dir = angle.ToRotationVector2();
                Dust dust = Dust.NewDustPerfect(
                    Projectile.Center + dir * 6f,
                    DustID.PurpleTorch,
                    dir * Main.rand.NextFloat(4f, 8f),
                    100,
                    new Color(230, 170, 255),
                    1.9f);
                dust.noGravity = true;
                dust.fadeIn = 1.3f;
            }

            // Dark afterburn in the middle.
            for (int i = 0; i < 10; i++)
            {
                Dust dust = Dust.NewDustPerfect(
                    Projectile.Center + Main.rand.NextVector2Circular(8f, 8f),
                    DustID.PurpleTorch,
                    Main.rand.NextVector2Circular(2f, 2f),
                    100,
                    new Color(80, 20, 140),
                    1.5f);
                dust.noGravity = true;
            }
        }

        private void EmitTrail(float intensity)
        {
            int count = Math.Max(1, (int)Math.Round(3 * intensity));
            for (int i = 0; i < count; i++)
            {
                Vector2 jitter = Main.rand.NextVector2Circular(3f, 3f);
                Vector2 velocity = launched
                    ? -Projectile.velocity * 0.12f + Main.rand.NextVector2Circular(0.6f, 0.6f)
                    : Main.rand.NextVector2Circular(0.3f, 0.3f);

                Dust dust = Dust.NewDustPerfect(
                    Projectile.Center + jitter,
                    DustID.PurpleTorch,
                    velocity,
                    100,
                    new Color(190, 100, 230),
                    1.2f);
                dust.noGravity = true;
                dust.fadeIn = 1.1f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float fade = Fading ? 1f - (float)fadeCounter / FadeFrames : 1f;
            if (fade <= 0f) return false;

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Rectangle src = new Rectangle(0, 0, 1, 1);
            Vector2 origin = new Vector2(0.5f, 0.5f);

            // The rotation points along the "long axis" of the cone (travel direction).
            // scale.X = length, scale.Y = thickness.
            Vector2 coreScale = new Vector2(36f, 4f);

            // Outer glow halos (additive-looking purple).
            for (int i = 0; i < 4; i++)
            {
                float intensity = 0.32f - i * 0.065f;
                if (intensity <= 0f) continue;
                Vector2 glowScale = coreScale + new Vector2(8f + i * 4f, 6f + i * 3f);
                Main.EntitySpriteDraw(
                    pixel, drawPos, src,
                    new Color(160, 60, 220) * intensity * fade,
                    Projectile.rotation, origin, glowScale,
                    SpriteEffects.None, 0);
            }

            // Bright core stripe.
            Main.EntitySpriteDraw(
                pixel, drawPos, src,
                new Color(235, 190, 255) * fade,
                Projectile.rotation, origin, coreScale,
                SpriteEffects.None, 0);

            // Tighter white-hot tip along the leading half for a cone-ish highlight.
            Vector2 tipOffset = new Vector2(coreScale.X * 0.25f, 0f).RotatedBy(Projectile.rotation);
            Main.EntitySpriteDraw(
                pixel, drawPos + tipOffset, src,
                new Color(255, 230, 255) * fade * 0.9f,
                Projectile.rotation, origin,
                new Vector2(coreScale.X * 0.55f, 2.2f),
                SpriteEffects.None, 0);

            return false;
        }

        // Don't hit anything after we've started fading.
        public override bool? CanHitNPC(NPC target) => Fading ? false : (bool?)null;

        public override bool CanHitPvp(Player target) => !Fading;
    }
}
