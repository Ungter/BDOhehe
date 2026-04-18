using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Collections.Generic;
using BDOhehe.Items.Armour.HeveArmor;
using BDOhehe.Buffs;
using BDOhehe.Projectiles;
using rail;

namespace BDOhehe.Items.Weapons.Awaken
{

    public class Sting : ModItem
    {
        private int comboStep = 0;
        private int currentSwing = 0;

        // Comet skill: dash toward the cursor while holding Shift during an attack.
        // The 4th swing uses a propel velocity of 8f, so this skill uses 3x = 24f.
        private const float ShiftDashVelocity = 24f;
        private const int ShiftDashDuration = 20;    // frames the skill animation runs
        private const int ShiftDashCooldownTicks = 240; // 4 seconds * 60 ticks/sec
        // Retain only 40% of velocity when the dash ends (60% momentum reduction).
        private const float ShiftDashMomentumRetention = 0.4f;
        private int shiftDashTimer = 0;
        private Vector2 shiftDashDirection = Vector2.UnitX;
        // Input buffer so Frozen Ring isn't misread as a Comet. We require Shift to
        // have been continuously held for at least this many ticks (60ms @ 60
        // ticks/sec ≈ 4 ticks) before a Comet can fire.
        private const int ShiftQBufferFrames = 2;
        private int shiftHeldFrames = 0;

        // Frozen Ring skill: throw the sword and spin it in the air for 4 seconds
        // while the player stays locked in place. The position-lock and any-button
        // cancel detection both live on the projectile (FrozenRing) so they
        // keep working even if Terraria's Shift auto-torch swap briefly changes
        // the held item off Sting mid-skill.
        private const int SpinSkillDurationTicks = 240;     // 4 seconds
        private const int SpinSkillCooldownTicks = 420;     // 7 seconds
        private const int SpinSkillCancelGrace = 3;         // frames before cancel input is read
        private const float SpinSkillThrowSpeed = 10f;
        private bool prevQDown = false;

        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            Item.damage = 50;
            Item.DamageType = DamageClass.Melee;
            Item.width = 60;
            Item.height = 30;
            Item.useTime = 35;
            Item.useAnimation = 35;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 6;
            Item.value = 10000;
            Item.rare = ItemRarityID.LightRed;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
        }



        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ItemID.DirtBlock, 10);
            recipe.AddTile(TileID.WorkBenches);
            recipe.Register();
        }

        // Face the player toward the cursor in WORLD space (the previous logic
        // compared screen-space mouseX to the screen center, which ignores where
        // the player actually is on-screen and only updated on click).
        public override void HoldItem(Player player)
        {
            // Track how long Shift has been continuously held so we can tell
            // a plain Comet apart from a Frozen Ring combo (60ms input buffer).
            bool shiftHeldNow =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            shiftHeldFrames = shiftHeldNow ? shiftHeldFrames + 1 : 0;

            // ---- Comet skill timers / momentum bleed ----
            bool dashJustEnded = false;
            if (shiftDashTimer > 0)
            {
                shiftDashTimer--;
                if (shiftDashTimer == 0) dashJustEnded = true;
            }
            if (dashJustEnded)
            {
                player.velocity *= ShiftDashMomentumRetention;
            }

            // ---- Frozen Ring skill trigger ----
            TryTriggerSpinSkill(player);

            // ---- Comet during cancellable Frozen Ring ----
            TryDashDuringSpin(player);

            // Don't flip facing while any skill is playing.
            if (shiftDashTimer == 0 && !IsSpinSkillActive(player))
            {
                player.direction = Main.MouseWorld.X < player.Center.X ? -1 : 1;
            }
        }

        // Returns true while a Frozen Ring projectile owned by this player is alive.
        // Driving this off the projectile (instead of an item-side timer) means
        // the state survives Terraria briefly swapping the held item (e.g. the
        // Shift auto-torch feature) for the duration of the skill.
        private static bool IsSpinSkillActive(Player player)
        {
            return player.ownedProjectileCounts[ModContent.ProjectileType<FrozenRing>()] > 0;
        }

        // Returns true if a spin-skill is active, mouse 1 is held, and buffer has expired.
        private static bool IsCancellableSpinActive(Player player)
        {
            if (!Main.mouseLeft) return false;
            int spinType = ModContent.ProjectileType<FrozenRing>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == spinType)
                {
                    if (p.ModProjectile is FrozenRing spin && spin.SkillCancelBuffer == 0)
                        return true;
                }
            }
            return false;
        }

        // Allows triggering shift-dash during a cancellable spin skill.
        private void TryDashDuringSpin(Player player)
        {
            if (!IsCancellableSpinActive(player)) return;

            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            bool qHeld = Main.keyState.IsKeyDown(Keys.Q);
            int dashCooldownBuff = ModContent.BuffType<CometCooldown>();
            bool onDashCooldown = player.HasBuff(dashCooldownBuff);
            bool shiftQBufferElapsed = shiftHeldFrames >= ShiftQBufferFrames && !qHeld;

            if (shiftHeld && shiftQBufferElapsed && shiftDashTimer == 0 && !onDashCooldown)
            {
                // Cancel the spin skill first
                int spinType = ModContent.ProjectileType<FrozenRing>();
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.owner == player.whoAmI && p.type == spinType)
                    {
                        p.Kill();
                    }
                }

                // Then trigger the dash
                shiftDashDirection = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
                player.velocity = shiftDashDirection * ShiftDashVelocity;
                player.direction = shiftDashDirection.X < 0 ? -1 : 1;
                shiftDashTimer = ShiftDashDuration;
                player.AddBuff(dashCooldownBuff, ShiftDashCooldownTicks);
            }
        }

        // Detects the Frozen Ring trigger and spawns the spinning sword projectile,
        // which then owns all per-frame logic (player lock, cancel detection).
        private void TryTriggerSpinSkill(Player player)
        {
            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            bool qDown = Main.keyState.IsKeyDown(Keys.Q);
            bool qJustPressed = qDown && !prevQDown;
            prevQDown = qDown;

            int cooldownBuff = ModContent.BuffType<FrozenRingCooldown>();

            if (!qJustPressed || !shiftHeld) return;
            if (IsSpinSkillActive(player)) return;      // can't Frozen Ring-cancel a Frozen Ring
            if (player.HasBuff(cooldownBuff)) return;

            // Skill transition: if a Comet is currently active, cancel it
            // in-place (bleed its momentum) so this Frozen Ring takes over.
            if (shiftDashTimer > 0)
            {
                shiftDashTimer = 0;
                player.velocity *= ShiftDashMomentumRetention;
            }

            // Stop any in-progress swing so the held sword isn't drawn.
            player.itemAnimation = 0;
            player.itemTime = 0;
            player.velocity = Vector2.Zero;

            // Throw the sword toward the cursor.
            Vector2 throwDir = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
            player.direction = throwDir.X < 0 ? -1 : 1;

            int projType = ModContent.ProjectileType<FrozenRing>();
            int idx = Projectile.NewProjectile(
                player.GetSource_ItemUse(Item),
                player.Center,
                throwDir * SpinSkillThrowSpeed,
                projType,
                Item.damage,
                Item.knockBack,
                player.whoAmI);

            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                Projectile proj = Main.projectile[idx];
                proj.timeLeft = SpinSkillDurationTicks;

                if (proj.ModProjectile is FrozenRing spinProj)
                {
                    spinProj.LockPosition = player.position;
                    spinProj.Grace = SpinSkillCancelGrace;
                    spinProj.StartMouseLeft = Main.mouseLeft;
                    spinProj.StartMouseRight = Main.mouseRight;
                    spinProj.StartKeys = new HashSet<Keys>(Main.keyState.GetPressedKeys());
                }
            }

            // Apply the 7-second cooldown buff (visible above the player's head).
            player.AddBuff(cooldownBuff, SpinSkillCooldownTicks);
        }

        // Block Terraria's auto-reuse from re-firing UseItem the instant after
        // a Frozen Ring starts. If mouseLeft was already held when the player pressed
        // Shift+Q, auto-reuse would otherwise call UseItem on the next tick and
        // our own Frozen Ring-cancel logic below would kill the freshly spawned skill
        // (cooldown applied, skill never visible). Forcing CanUseItem=false
        // while mouseLeft is a "stale" click (held through from before the
        // Frozen Ring started) means the player must release and re-click to cancel
        // the Frozen Ring -- which is exactly the intent.
        public override bool CanUseItem(Player player)
        {
            if (!IsSpinSkillActive(player)) return true;

            int spinType = ModContent.ProjectileType<FrozenRing>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == spinType)
                {
                    if (p.ModProjectile is FrozenRing spin && spin.StartMouseLeft)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override bool? UseItem(Player player)
        {
            // Skill transition: starting a new swing interrupts an active Frozen Ring
            // skill. (We don't cancel the Comet here because the Comet is
            // triggered from UseStyle and actually relies on an active swing.)
            // CanUseItem above gates this so only a fresh click gets here --
            // held-through mouseLeft from before the Frozen Ring cannot reach it.
            if (IsSpinSkillActive(player))
            {
                int spinType = ModContent.ProjectileType<FrozenRing>();
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.owner == player.whoAmI && p.type == spinType)
                    {
                        p.Kill();
                    }
                }
            }

            // Lock facing to cursor at the start of each swing so the animation
            // stays consistent even if the cursor drifts across the player mid-swing.
            player.direction = Main.MouseWorld.X < player.Center.X ? -1 : 1;

            // Set current swing for animation BEFORE anything else
            currentSwing = comboStep;

            // Pre-position the sword at the swing's starting pose. Without this,
            // there is a one-frame flash where the vanilla Swing style draws the
            // sprite at a default/stale location before our UseStyle runs.
            UpdateSwordTransform(player, 0f);

            // Dash forward on the 4th attack (forward stab).
            // Skip the velocity override if the Comet is active, otherwise
            // the stab's velocity would overwrite (and cancel) the Comet momentum.
            if (comboStep == 3 && shiftDashTimer == 0)
            {
                Vector2 dashDirection = Main.MouseWorld - player.Center;
                dashDirection.Normalize();
                player.velocity = dashDirection * 8f;

                // Create purple afterimages at player position
                for (int i = 0; i < 8; i++)
                {
                    Vector2 offset = dashDirection * (-i * 8f);
                    Dust dust = Dust.NewDustPerfect(player.Center + offset, DustID.PurpleTorch, Vector2.Zero, 100, Color.Purple, 1.5f);
                    dust.noGravity = true;
                    dust.fadeIn = 1f;
                }
            }

            // Increment for next attack
            comboStep = (comboStep + 1) % 4;
            return true;
        }

        // Combo animation: down, up, down, forward stab with arc motion.
        // The sword sprite's tip naturally points toward the top-right of the
        // texture (angle -pi/4 in screen space). When player.direction == -1
        // Terraria draws the sprite horizontally flipped, so the tip instead
        // naturally points at angle (pi - (-pi/4)) = -3pi/4.
        public override void UseStyle(Player player, Rectangle heldItem)
        {
            // ---- Comet trigger (runs only while a swing is active) ----
            // Comet only arms during an attack animation; holding Shift alone
            // never triggers Comet. Skill-chaining into Comet is still possible
            // because UseItem cancels any active Frozen Ring, which then lets the
            // swing's very next frame run UseStyle and fire Comet.
            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            bool qHeld = Main.keyState.IsKeyDown(Keys.Q);
            int dashCooldownBuff = ModContent.BuffType<CometCooldown>();
            bool onDashCooldown = player.HasBuff(dashCooldownBuff);
            bool shiftQBufferElapsed = shiftHeldFrames >= ShiftQBufferFrames && !qHeld;

            if (shiftHeld && shiftQBufferElapsed && shiftDashTimer == 0 &&
                !onDashCooldown && player.itemAnimation > 0)
            {
                shiftDashDirection = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
                player.velocity = shiftDashDirection * ShiftDashVelocity;
                player.direction = shiftDashDirection.X < 0 ? -1 : 1;
                shiftDashTimer = ShiftDashDuration;
                player.AddBuff(dashCooldownBuff, ShiftDashCooldownTicks);
            }

            if (shiftDashTimer > 0)
            {
                UpdateShiftDashFrame(player);
                return;
            }

            float progress = 1f - (float)player.itemAnimation / player.itemAnimationMax;
            UpdateSwordTransform(player, progress);
        }

        // Simple Comet animation: the player just points the sword at the cursor,
        // while a purple rectangular after-image (full player-height) trails behind.
        private void UpdateShiftDashFrame(Player player)
        {
            const float SPRITE_NATURAL_ANGLE = -MathHelper.PiOver4;

            Vector2 toMouse = Main.MouseWorld - player.Center;
            float baseAngle = toMouse.ToRotation();
            SetSwordWorldAngle(player, baseAngle, SPRITE_NATURAL_ANGLE);

            Vector2 thrustDir = toMouse.SafeNormalize(shiftDashDirection);
            player.itemLocation = player.Center + thrustDir * 20f;
            player.itemLocation.Y += player.gfxOffY;

            // Purple rectangle afterimage with the height (and width) of the player.
            // Dust is spawned filling the player's bounding rect; because it has
            // zero velocity and no gravity, it stays behind as the player dashes,
            // leaving a rectangular purple trail.
            const int dustPerFrame = 14;
            for (int i = 0; i < dustPerFrame; i++)
            {
                Vector2 dustPos = new Vector2(
                    player.position.X + Main.rand.NextFloat(0f, player.width),
                    player.position.Y + Main.rand.NextFloat(0f, player.height));
                Dust dust = Dust.NewDustPerfect(dustPos, DustID.PurpleTorch, Vector2.Zero, 100, Color.Purple, 1.4f);
                dust.noGravity = true;
                dust.fadeIn = 1.1f;
            }
        }

        // Shared per-frame transform + particle logic. Also invoked from UseItem
        // with progress = 0 so the sprite is already in its starting pose on the
        // frame the swing begins (no one-frame flash from the vanilla swing style).
        private void UpdateSwordTransform(Player player, float progress)
        {
            const float SPRITE_NATURAL_ANGLE = -MathHelper.PiOver4;

            // Angle from the player to the cursor -- every swing is oriented
            // around this direction so aiming up/down/left/right all look right.
            Vector2 toMouse = Main.MouseWorld - player.Center;
            float baseAngle = toMouse.ToRotation();

            // Fourth attack: forward stab toward the cursor.
            if (currentSwing == 3)
            {
                float stabProgress = (float)Math.Sin(progress * MathHelper.Pi);

                SetSwordWorldAngle(player, baseAngle, SPRITE_NATURAL_ANGLE);

                float thrustDistance = stabProgress * 25f;
                Vector2 thrustDir = toMouse.SafeNormalize(Vector2.Zero);
                player.itemLocation = player.Center + thrustDir * (10f + thrustDistance);
                player.itemLocation.Y += player.gfxOffY;

                if (progress > 0f && Main.rand.NextBool(2))
                {
                    Dust dust = Dust.NewDustPerfect(player.itemLocation, DustID.PurpleTorch, thrustDir * 2f, 100, Color.Purple, 1.5f);
                    dust.noGravity = true;
                }
                return;
            }

            // Swing offset in radians relative to the cursor direction.
            // Multiplied by player.direction so a facing-left swing mirrors
            // a facing-right swing instead of going backwards.
            float swingOffset = 0f;
            switch (currentSwing)
            {
                case 0: // Downward arc through the cursor
                    swingOffset = MathHelper.Lerp(-1.0f, 1.0f, progress);
                    break;
                case 1: // Upward arc through the cursor
                    swingOffset = MathHelper.Lerp(1.0f, -1.0f, progress);
                    break;
                case 2: // Shorter downward arc
                    swingOffset = MathHelper.Lerp(-0.5f, 1.0f, progress);
                    break;
            }

            float worldAngle = baseAngle + swingOffset * player.direction;

            SetSwordWorldAngle(player, worldAngle, SPRITE_NATURAL_ANGLE);

            // Anchor the hilt near the player's hand along the current blade direction.
            Vector2 handOffset = new Vector2(10f, 0f).RotatedBy(worldAngle);
            player.itemLocation = player.Center + handOffset;

            // Purple trail particles at the blade tip (skip on the pre-positioning
            // call made from UseItem so it doesn't double-spawn dust on frame 0).
            if (progress > 0f && Main.rand.NextBool(2))
            {
                Vector2 bladeTip = player.Center + new Vector2(30f, 0f).RotatedBy(worldAngle);
                Dust dust = Dust.NewDustPerfect(bladeTip, DustID.PurpleTorch, Vector2.Zero, 100, Color.Purple, 1.2f);
                dust.noGravity = true;
                dust.velocity = player.velocity * 0.5f;
            }
        }

        // Sets player.itemRotation so the sword's tip points at the given world
        // angle, correctly compensating for the sprite's natural 45-degree
        // orientation AND the horizontal flip applied when direction == -1.
        private static void SetSwordWorldAngle(Player player, float worldAngle, float spriteNaturalAngle)
        {
            if (player.direction == 1)
            {
                // Unflipped: sprite tip sits at spriteNaturalAngle when itemRotation = 0.
                player.itemRotation = worldAngle - spriteNaturalAngle;
            }
            else
            {
                // Flipped: sprite tip sits at (pi - spriteNaturalAngle) when itemRotation = 0.
                player.itemRotation = worldAngle - (MathHelper.Pi - spriteNaturalAngle);
            }
        }

        // This hook is called whenever the player attacks
        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (isArmorSet(player))
            {
                target.AddBuff(BuffID.OnFire, 180);
            }

            int hitCount = 0;

            // Increment the hit count
            hitCount++;

            // If the hit count is 3, reset it and give the player a buff
            if (hitCount >= 3)
            {
                player.AddBuff(BuffID.Wrath, 600);
            }

            if (hitCount >= 10)
            {
                hitCount = 0;
                player.AddBuff(BuffID.IceQueenPet, 600);
            }
        }


        // give the attack light particles that shoot out 
        public override void MeleeEffects(Player player, Rectangle hitbox)
        {
            if (Main.rand.NextBool(3))
            {
                Dust.NewDust(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height, DustID.BubbleBurst_Purple);
            }
        }

        public Boolean isArmorSet(Player player)
        {
            return (player.armor[0].type == ModContent.ItemType<HeveHelm>() &&
                player.armor[1].type == ModContent.ItemType<HeveBody>() &&
                player.armor[2].type == ModContent.ItemType<HeveShoes>());

        }
    }
}
