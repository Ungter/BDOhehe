using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
using System;
using System.Collections.Generic;
using BDOhehe.Items.Armour.HeveArmor;
using BDOhehe.Buffs;
using BDOhehe.Particles;
using BDOhehe.Projectiles;
using rail;
using ReLogic.Utilities;

namespace BDOhehe.Items.Weapons.Awaken
{

    public class Sting : ModItem
    {
        private int comboStep = 0;
        private int currentSwing = 0;

        // Comet skill: dash toward the cursor while holding F during an attack.
        // The 4th swing uses a propel velocity of 8f, so this skill uses 3x = 24f.
        private const float ShiftDashVelocity = 24f;
        private const int ShiftDashDuration = 20;    // frames the skill animation runs
        private const int ShiftDashCooldownTicks = 240; // 4 seconds * 60 ticks/sec
        // Retain only 40% of velocity when the dash ends (60% momentum reduction).
        private const float ShiftDashMomentumRetention = 0.4f;
        private int shiftDashTimer = 0;
        private Vector2 shiftDashDirection = Vector2.UnitX;
        // Previous-frame player center during a Comet dash. Used to
        // interpolate afterimage particle spawns along the travel line so the
        // trail stays continuous at 24f/frame instead of showing frame gaps.
        private Vector2 lastDashCenter;
        // Small input buffer so a stray 1-frame F tap doesn't fire a Comet.
        private const int CometBufferFrames = 2;
        private int cometKeyHeldFrames = 0;
        // Frozen Ring skill: throw the sword and spin it in the air for 1 second
        // while the player stays locked in place. The position-lock and any-button
        // cancel detection both live on the projectile (FrozenRing) so they
        // keep working even if Terraria's Shift auto-torch swap briefly changes
        // the held item off Sting mid-skill.
        private const int SpinSkillDurationTicks = 120;      // 1 second (reduced from 4s)
        private const int SpinSkillCooldownTicks = 420;     // 7 seconds
        private const int SpinSkillCancelGrace = 3;         // frames before cancel input is read
        private const float SpinSkillThrowSpeed = 10f;
        private bool prevQDown = false;

        // Starfall skill: Shift + right-click summons 5 slim purple cones above the
        // player's head that, after a short delay, launch toward the initial cursor
        // position. Cancellable by other skills (projectiles fade out instead of
        // being killed instantly).
        private const int StarfallConeCount = 5;
        private const int StarfallDelayFrames = 9;         // 50% slower than the original 6-frame cast
        private const int StarfallDelayFramesQuick = 6;    // quick-chain variant when another skill was cast recently
        private const int StarfallQuickChainThreshold = 60; // 1 second @ 60fps
        private const int StarfallCooldownTicks = 360; // 6 seconds
        private bool prevMouseRightDown = false;

        // Star's Call skill: Shift + left-click summons a crown of 6 cones above
        // the cursor; after a short locked delay they strike down and explode at
        // the cursor position. Only Comet can cancel it.
        private const int StarCallConeCount = 6;
        private const int StarCallLockDelayFrames = 40;     // base wind-up
        private const int StarCallLockDelayFramesQuick = 6;  // same as Starfall when chained
        private const int StarCallQuickChainThreshold = 60;  // 1 second @ 60fps
        private const float StarCallMaxRange = 520f;         // ~32 blocks from the player
        private const float StarCallCrownSpan = 180f;        // horizontal span of the crown
        private const float StarCallCrownHeight = 110f;      // how high above the cursor the crown sits
        private const float StarCallCrownArc = 28f;          // vertical arc amount for the crown shape
        private const int StarCallCooldownTicks = 420;       // 7 seconds
        private bool prevMouseLeftDown = false;

        // Frame counter since the player last cast any of the "other" Sting
        // skills (Comet, Frozen Ring, Starfall). Used to shorten Star's Call's
        // windup when the player chains quickly off another skill.
        private int ticksSinceAnySkill = 10000;

        // Sound trigger gates to prevent multiple plays per frame
        private bool swingSoundPlayed = false;
        private bool starfallSoundPlayed = false;
        private bool cometSoundPlayed = false;

        // Active-sound slots tracked so a cancelled skill can immediately
        // stop its sound effect instead of letting it play out at the
        // activation position.
        private SlotId cometSoundSlot;
        private SlotId starfallSoundSlot;

        // Helper: stop the sound owned by the given slot (if still active)
        // and invalidate the slot. Safe to call on an already-finished or
        // never-set slot.
        private static void StopSound(ref SlotId slot)
        {
            if (slot.IsValid && SoundEngine.TryGetActiveSound(slot, out var activeSound))
            {
                activeSound?.Stop();
            }
            slot = SlotId.Invalid;
        }

        public override void SetStaticDefaults()
        {
        }

        // Scale factor to render high-res sprites at reasonable in-game size
        // 1024x1024 texture / 40x40 original = 25.6x, so we scale down by ~25x
        private const float SpriteDrawScale = 0.1f;

        public override void SetDefaults()
        {
            Item.damage = 50;
            Item.DamageType = DamageClass.Melee;
            Item.width = 40;
            Item.height = 40;
            Item.scale = SpriteDrawScale;
            Item.useTime = 35;
            Item.useAnimation = 35;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 6;
            Item.value = 10000;
            Item.rare = ItemRarityID.LightRed;
            Item.UseSound = null;
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
            // ---- Block Terraria's Shift-based auto tool selection ----
            // The vanilla smart-cursor/auto-equip feature swaps the held item
            // to a torch/rope/pickaxe/etc. when Shift is held near a matching
            // surface. Because every skill on Sting keys off Shift, that swap
            // eats our inputs and interrupts skills mid-cast. Clearing the
            // smart-select flags every frame the player holds Sting prevents
            // the swap from ever happening while the item is in hand.
            player.controlSmart = false;
            player.nonTorch = -1;

            // Cap the counter so we never overflow; threshold is ~60.
            if (ticksSinceAnySkill < 100000) ticksSinceAnySkill++;

            // Track how long the Comet key (F) has been continuously held so a
            // one-frame tap doesn't accidentally fire a Comet.
            bool cometKeyHeldNow = Main.keyState.IsKeyDown(Keys.F);
            cometKeyHeldFrames = cometKeyHeldNow ? cometKeyHeldFrames + 1 : 0;

            // ---- Comet skill timers / momentum bleed ----
            bool dashJustEnded = false;
            if (shiftDashTimer > 0)
            {
                shiftDashTimer--;
                player.noKnockback = true;
                if (shiftDashTimer == 0) dashJustEnded = true;
            }
            else
            {
                // Keep the afterimage anchor fresh while idle so the very
                // first dash frame doesn't interpolate from a stale point.
                lastDashCenter = player.Center;
            }
            if (dashJustEnded)
            {
                player.velocity *= ShiftDashMomentumRetention;
                player.noKnockback = false;
            }

            // ---- Frozen Ring skill trigger ----
            TryTriggerSpinSkill(player);

            // ---- Comet during cancellable Frozen Ring ----
            TryDashDuringSpin(player);

            // ---- Starfall skill trigger (Shift + right-click) ----
            TryTriggerStarfall(player);

            // ---- Star's Call skill trigger (Shift + left-click) ----
            TryTriggerStarCall(player);

            // ---- Comet cancels Star's Call during its lock phase ----
            TryDashDuringStarCall(player);

            // Don't flip facing while the player is locked by a skill.
            if (shiftDashTimer == 0 && !IsSpinSkillActive(player) && !IsStarCallLocking(player))
            {
                player.direction = Main.MouseWorld.X < player.Center.X ? -1 : 1;
            }

            // Reset sound gates every frame (sounds should only play once per trigger)
            swingSoundPlayed = false;
            starfallSoundPlayed = false;
            cometSoundPlayed = false;
        }

        // True while the Star's Call parent projectile is alive -- i.e. during
        // the player-lock / windup phase. Used for the Comet-cancel path and
        // the facing lock.
        private static bool IsStarCallLocking(Player player)
        {
            return player.ownedProjectileCounts[ModContent.ProjectileType<StarCall>()] > 0;
        }

        // True while any part of Star's Call is still executing -- either the
        // parent lock OR the strike-down cones in the air. Used to block
        // basic-attack swings (and other skills) from cancelling the skill.
        private static bool IsStarCallActive(Player player)
        {
            if (IsStarCallLocking(player)) return true;

            int type = ModContent.ProjectileType<StarfallCone>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == type &&
                    p.ModProjectile is StarfallCone cone && cone.StrikeDownMode && !cone.Fading)
                    return true;
            }
            return false;
        }

        // Fade only the Star's Call cones (StrikeDownMode = true), leaving any
        // regular Starfall cones alone.
        private static void FadeStarCallCones(Player player)
        {
            int type = ModContent.ProjectileType<StarfallCone>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == type &&
                    p.ModProjectile is StarfallCone cone && cone.StrikeDownMode && !cone.Fading)
                {
                    cone.Fading = true;
                }
            }
        }

        // Kill the Star's Call parent projectile and fade its cones. Used by
        // the Comet cancel path. Flags the projectile as cancelled so its
        // OnKill silences the wind-up sound (natural expiration lets it ring).
        private static void CancelStarCall(Player player)
        {
            int parentType = ModContent.ProjectileType<StarCall>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == parentType)
                {
                    if (p.ModProjectile is StarCall sc)
                        sc.Cancelled = true;
                    p.Kill();
                }
            }
            FadeStarCallCones(player);
        }

        private void TryTriggerStarCall(Player player)
        {
            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            bool mouseLeftDown = Main.mouseLeft;
            bool mouseLeftJustPressed = mouseLeftDown && !prevMouseLeftDown;
            prevMouseLeftDown = mouseLeftDown;

            if (!mouseLeftJustPressed || !shiftHeld) return;

            // Don't overlap with other active skills.
            if (shiftDashTimer > 0) return;
            if (IsSpinSkillActive(player)) return;
            if (IsStarfallActive(player)) return;
            if (IsStarCallActive(player)) return;

            int cooldownBuff = ModContent.BuffType<StarCallCooldown>();
            if (player.HasBuff(cooldownBuff)) return;

            // Clamp the cast target to the max range so an out-of-range cursor
            // still produces a cast at the edge of the player's reach.
            Vector2 targetPos = Main.MouseWorld;
            Vector2 toCursor = targetPos - player.Center;
            if (toCursor.LengthSquared() > StarCallMaxRange * StarCallMaxRange)
            {
                targetPos = player.Center + toCursor.SafeNormalize(new Vector2(0, -1)) * StarCallMaxRange;
            }

            // Quick-chain: if another skill was cast within the last second,
            // shorten the windup to Starfall's delay instead of the normal one.
            int lockDelay = ticksSinceAnySkill <= StarCallQuickChainThreshold
                ? StarCallLockDelayFramesQuick
                : StarCallLockDelayFrames;

            // Play the wind-up sound and hand its slot to the parent projectile
            // so OnKill on StarCall can stop it if the skill is cancelled.
            // Quick-chain cast shortens the wind-up; pitch up the sound by the
            // same speed ratio (clamped to the engine's +1 octave max = ~2x)
            // so the audio finishes along with the compressed animation.
            float castSpeedRatio = (float)StarCallLockDelayFrames / lockDelay;
            float callPitch = MathHelper.Clamp((float)System.Math.Log(castSpeedRatio, 2), 0f, 1f);
            SlotId callSlot = SoundEngine.PlaySound(
                new SoundStyle("BDOhehe/Sound/StingStarsCall") { Volume = 0.8f, Pitch = callPitch },
                player.Center);

            // Spawn the parent projectile that locks the player in place.
            int parentType = ModContent.ProjectileType<StarCall>();
            int pidx = Projectile.NewProjectile(
                player.GetSource_ItemUse(Item),
                player.Center,
                Vector2.Zero,
                parentType,
                0, 0, player.whoAmI);
            if (pidx >= 0 && pidx < Main.maxProjectiles)
            {
                Projectile parent = Main.projectile[pidx];
                parent.timeLeft = lockDelay;
                if (parent.ModProjectile is StarCall starCall)
                {
                    starCall.LockPosition = player.position;
                    starCall.SoundSlot = callSlot;
                }
            }

            // Spawn the crown of 6 cones above the target, fanned left-to-right
            // on a gentle arc and all aiming at the strike point. Each cone
            // stays hidden until its RevealFrame so the crown fills in
            // left-to-right across the wind-up instead of appearing all at once.
            int coneType = ModContent.ProjectileType<StarfallCone>();
            float halfCount = (StarCallConeCount - 1) / 2f;
            for (int i = 0; i < StarCallConeCount; i++)
            {
                float t = (i - halfCount) / halfCount;               // -1..+1
                float x = t * (StarCallCrownSpan / 2f);
                float y = -StarCallCrownHeight + t * t * StarCallCrownArc; // parabolic arc
                Vector2 spawnPos = targetPos + new Vector2(x, y);

                int cidx = Projectile.NewProjectile(
                    player.GetSource_ItemUse(Item),
                    spawnPos,
                    Vector2.Zero,
                    coneType,
                    Item.damage,
                    Item.knockBack,
                    player.whoAmI);
                if (cidx >= 0 && cidx < Main.maxProjectiles &&
                    Main.projectile[cidx].ModProjectile is StarfallCone cone)
                {
                    cone.TargetPosition = targetPos;
                    cone.ConeIndex = i;
                    cone.StrikeDownMode = true;
                    cone.DelayOverride = lockDelay;
                    cone.RevealFrame = (i * lockDelay) / StarCallConeCount;
                }
            }

            player.direction = targetPos.X < player.Center.X ? -1 : 1;

            player.AddBuff(cooldownBuff, StarCallCooldownTicks);
        }

        // F during the Star's Call lock cancels it and immediately fires Comet.
        // Only valid during the lock phase -- once the cones launch, they're
        // no longer interruptible.
        private void TryDashDuringStarCall(Player player)
        {
            if (!IsStarCallLocking(player)) return;

            bool cometKeyHeld = Main.keyState.IsKeyDown(Keys.F);
            if (!cometKeyHeld) return;

            int dashCooldownBuff = ModContent.BuffType<CometCooldown>();
            if (player.HasBuff(dashCooldownBuff)) return;
            if (shiftDashTimer > 0) return;
            if (cometKeyHeldFrames < CometBufferFrames) return;

            CancelStarCall(player);

            shiftDashDirection = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
            player.velocity = shiftDashDirection * ShiftDashVelocity;
            player.direction = shiftDashDirection.X < 0 ? -1 : 1;
            shiftDashTimer = ShiftDashDuration;
            player.AddBuff(dashCooldownBuff, ShiftDashCooldownTicks);
            StopSound(ref cometSoundSlot);
            cometSoundSlot = SoundEngine.PlaySound(new SoundStyle("BDOhehe/Sound/StingComet") { Volume = 0.8f }, player.Center);
            ticksSinceAnySkill = 0;
        }

        // Returns true while any non-Star's-Call Starfall cone is alive.
        // Star's Call cones use StrikeDownMode = true; they're tracked via
        // IsStarCallActive instead so the two skills can coexist cleanly.
        private static bool IsStarfallActive(Player player)
        {
            int type = ModContent.ProjectileType<StarfallCone>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == type &&
                    p.ModProjectile is StarfallCone cone && !cone.StrikeDownMode)
                    return true;
            }
            return false;
        }

        // Start the fade-out animation on every regular Starfall cone this
        // player owns. Star's Call cones (StrikeDownMode) are intentionally
        // skipped -- only Comet can cancel Star's Call. Also immediately
        // stops the skill's wind-up sound so a cancelled Starfall goes silent.
        private void FadeStarfall(Player player)
        {
            int type = ModContent.ProjectileType<StarfallCone>();
            bool faded = false;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == player.whoAmI && p.type == type &&
                    p.ModProjectile is StarfallCone cone && !cone.StrikeDownMode && !cone.Fading)
                {
                    cone.Fading = true;
                    faded = true;
                }
            }
            if (faded)
            {
                StopSound(ref starfallSoundSlot);
            }
        }

        private void TryTriggerStarfall(Player player)
        {
            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            bool mouseRightDown = Main.mouseRight;
            bool mouseRightJustPressed = mouseRightDown && !prevMouseRightDown;
            prevMouseRightDown = mouseRightDown;

            if (!mouseRightJustPressed || !shiftHeld) return;

            // Don't overlap with other active skills.
            if (shiftDashTimer > 0) return;
            if (IsSpinSkillActive(player)) return;
            if (IsStarfallActive(player)) return;
            if (IsStarCallActive(player)) return;

            int cooldownBuff = ModContent.BuffType<StarfallCooldown>();
            if (player.HasBuff(cooldownBuff)) return;

            Vector2 targetPos = Main.MouseWorld;
            int projType = ModContent.ProjectileType<StarfallCone>();

            // Quick-chain: shorten the windup to the original 6-frame delay
            // when the player chains off another Sting skill within 1 second.
            int delay = ticksSinceAnySkill <= StarfallQuickChainThreshold
                ? StarfallDelayFramesQuick
                : StarfallDelayFrames;

            for (int i = 0; i < StarfallConeCount; i++)
            {
                int idx = Projectile.NewProjectile(
                    player.GetSource_ItemUse(Item),
                    player.Center + new Vector2(0f, -90f),
                    Vector2.Zero,
                    projType,
                    Item.damage,
                    Item.knockBack,
                    player.whoAmI);

                if (idx >= 0 && idx < Main.maxProjectiles &&
                    Main.projectile[idx].ModProjectile is StarfallCone cone)
                {
                    cone.TargetPosition = targetPos;
                    cone.ConeIndex = i;
                    cone.DelayOverride = delay;
                }
            }

            // Face the cursor at activation so the player "aims" the skill.
            player.direction = targetPos.X < player.Center.X ? -1 : 1;

            // Play Starfall activation sound - gate to prevent multiple plays
            if (!starfallSoundPlayed)
            {
                StopSound(ref starfallSoundSlot);
                starfallSoundSlot = SoundEngine.PlaySound(new SoundStyle("BDOhehe/Sound/StingStarFall") { Volume = 0.8f }, player.Center);
                starfallSoundPlayed = true;
            }

            player.AddBuff(cooldownBuff, StarfallCooldownTicks);
            ticksSinceAnySkill = 0;
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

        // Allows triggering Comet (dash) during a cancellable spin skill.
        private void TryDashDuringSpin(Player player)
        {
            if (!IsCancellableSpinActive(player)) return;

            bool cometKeyHeld = Main.keyState.IsKeyDown(Keys.F);
            int dashCooldownBuff = ModContent.BuffType<CometCooldown>();
            bool onDashCooldown = player.HasBuff(dashCooldownBuff);
            bool cometBufferElapsed = cometKeyHeldFrames >= CometBufferFrames;

            if (cometKeyHeld && cometBufferElapsed && shiftDashTimer == 0 && !onDashCooldown)
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

                // Intentionally do NOT fade Starfall here -- Comet is the one
                // skill that does not cancel active Starfall cones.

                // Then trigger the dash
                shiftDashDirection = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
                player.velocity = shiftDashDirection * ShiftDashVelocity;
                player.direction = shiftDashDirection.X < 0 ? -1 : 1;
                shiftDashTimer = ShiftDashDuration;
                player.AddBuff(dashCooldownBuff, ShiftDashCooldownTicks);
                StopSound(ref cometSoundSlot);
                cometSoundSlot = SoundEngine.PlaySound(new SoundStyle("BDOhehe/Sound/StingComet") { Volume = 0.8f }, player.Center);
                ticksSinceAnySkill = 0;
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
            if (IsStarCallActive(player)) return;       // Star's Call lock is only cancellable by Comet
            if (player.HasBuff(cooldownBuff)) return;

            // Skill transition: if a Comet is currently active, cancel it
            // in-place (bleed its momentum) so this Frozen Ring takes over.
            if (shiftDashTimer > 0)
            {
                shiftDashTimer = 0;
                player.velocity *= ShiftDashMomentumRetention;
                StopSound(ref cometSoundSlot);
            }

            // Fade out any active Starfall cones (skill transition -> fade).
            FadeStarfall(player);

            // Stop any in-progress swing so the held sword isn't drawn.
            player.itemAnimation = 0;
            player.itemTime = 0;
            player.velocity = Vector2.Zero;

            // Throw the sword toward the cursor.
            Vector2 throwDir = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
            player.direction = throwDir.X < 0 ? -1 : 1;

            // Play the skill sound and hand its slot to the projectile so its
            // OnKill can stop the audio if the skill is cancelled mid-cast.
            SlotId ringSlot = SoundEngine.PlaySound(new SoundStyle("BDOhehe/Sound/StingFrozenRing") { Volume = 0.8f }, player.Center);

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
                    spinProj.SoundSlot = ringSlot;
                }
            }

            // Apply the 7-second cooldown buff (visible above the player's head).
            player.AddBuff(cooldownBuff, SpinSkillCooldownTicks);
            ticksSinceAnySkill = 0;
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
            // Star's Call uses Shift+Mouse1 and must not be accompanied by a
            // sword swing. Block the swing while the skill is active OR while
            // Shift+Mouse1 conditions would fire the skill on this click.
            if (ShouldBlockSwingForStarCall(player)) return false;

            // Basic attacks are disabled for the full Starfall duration so
            // they can't cancel the skill mid-cast.
            if (IsStarfallActive(player)) return false;

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

        // Suppress the basic-attack swing whenever Shift+Mouse1 is a valid
        // Star's Call cast, so the skill doesn't fire alongside a swing.
        private bool ShouldBlockSwingForStarCall(Player player)
        {
            if (IsStarCallActive(player)) return true;

            bool shiftHeld =
                Main.keyState.IsKeyDown(Keys.LeftShift) ||
                Main.keyState.IsKeyDown(Keys.RightShift);
            if (!shiftHeld) return false;

            if (shiftDashTimer > 0) return false;
            if (IsSpinSkillActive(player)) return false;
            if (IsStarfallActive(player)) return false;
            if (player.HasBuff(ModContent.BuffType<StarCallCooldown>())) return false;

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

            // A fresh swing interrupts any active Starfall -> fade them out.
            if (IsStarfallActive(player))
            {
                FadeStarfall(player);
            }

            // Lock facing to cursor at the start of each swing so the animation
            // stays consistent even if the cursor drifts across the player mid-swing.
            player.direction = Main.MouseWorld.X < player.Center.X ? -1 : 1;

            // Set current swing for animation BEFORE anything else
            currentSwing = comboStep;

            // Play swing sound based on current combo step (0-3) - gate to prevent multiple plays
            if (!swingSoundPlayed)
            {
                string customSwingSound = comboStep switch
                {
                    0 => "StingAuto1",
                    1 => "StingAuto2",
                    2 => "StingAuto3",
                    3 => "StingAuto4",
                    _ => "StingAuto1"
                };
                SoundEngine.PlaySound(new SoundStyle($"BDOhehe/Sound/{customSwingSound}") { Volume = 0.8f }, player.Center);
                swingSoundPlayed = true;
            }

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

                // Smoky purple afterimage trailing behind the player.
                for (int i = 0; i < 8; i++)
                {
                    Vector2 offset = dashDirection * (-i * 8f);
                    var p = new SmokeParticle(
                        player.Center + offset,
                        Vector2.Zero,
                        PurplePalette.RandomCloud(),
                        1.8f,
                        26);
                    p.GrowthAt1 = 1.7f;
                    ParticleSystem.Spawn(p);
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
            // Comet only arms during an attack animation; holding F alone
            // never triggers Comet. Skill-chaining into Comet is still possible
            // because UseItem cancels any active Frozen Ring, which then lets the
            // swing's very next frame run UseStyle and fire Comet.
            bool cometKeyHeld = Main.keyState.IsKeyDown(Keys.F);
            int dashCooldownBuff = ModContent.BuffType<CometCooldown>();
            bool onDashCooldown = player.HasBuff(dashCooldownBuff);
            bool cometBufferElapsed = cometKeyHeldFrames >= CometBufferFrames;

            if (cometKeyHeld && cometBufferElapsed && shiftDashTimer == 0 &&
                !onDashCooldown && player.itemAnimation > 0 && !cometSoundPlayed)
            {
                // Intentionally do NOT fade Starfall here -- Comet is the one
                // skill that does not cancel active Starfall cones.

                shiftDashDirection = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
                player.velocity = shiftDashDirection * ShiftDashVelocity;
                player.direction = shiftDashDirection.X < 0 ? -1 : 1;
                shiftDashTimer = ShiftDashDuration;
                player.AddBuff(dashCooldownBuff, ShiftDashCooldownTicks);
                StopSound(ref cometSoundSlot);
                cometSoundSlot = SoundEngine.PlaySound(new SoundStyle("BDOhehe/Sound/StingComet") { Volume = 0.8f }, player.Center);
                cometSoundPlayed = true;
                ticksSinceAnySkill = 0;
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

            // Continuous smoky purple afterimage. At 24f/frame the dash
            // would otherwise leave visible gaps between per-frame spawn
            // bursts; we interpolate between lastDashCenter and the current
            // center so the silhouette cloud fills every pixel of the
            // travel line.
            Vector2 from = lastDashCenter;
            Vector2 to = player.Center;
            float dist = Vector2.Distance(from, to);

            // One sample cluster every ~5 world pixels. Cap step count at 30
            // to guard against teleports/respawns (which would otherwise
            // spray particles across the world).
            const float stepPx = 5f;
            int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / stepPx));
            if (steps > 30)
            {
                steps = 1;
                from = to;
            }

            // Total body puffs per frame; distributed across the sample
            // points so density stays roughly constant regardless of speed.
            const int totalBodyPuffs = 12;
            int puffsPerStep = System.Math.Max(1, totalBodyPuffs / steps);

            for (int s = 0; s < steps; s++)
            {
                float t = (s + 0.5f) / steps;
                Vector2 sampleCenter = Vector2.Lerp(from, to, t);
                Vector2 anchor = sampleCenter - new Vector2(player.width * 0.5f, player.height * 0.5f);

                for (int i = 0; i < puffsPerStep; i++)
                {
                    Vector2 pos = new Vector2(
                        anchor.X + Main.rand.NextFloat(0f, player.width),
                        anchor.Y + Main.rand.NextFloat(0f, player.height));
                    var p = new SmokeParticle(
                        pos,
                        Vector2.Zero,
                        PurplePalette.RandomCloud(),
                        1.6f + Main.rand.NextFloat() * 0.6f,
                        28 + Main.rand.Next(8));
                    p.GrowthAt1 = 1.9f;
                    ParticleSystem.Spawn(p);
                }
            }

            // Leading wisps, also interpolated so they form a solid streak
            // rather than one-off puffs at the head.
            int wispSteps = System.Math.Max(1, steps);
            for (int s = 0; s < wispSteps; s++)
            {
                float t = (s + 0.5f) / wispSteps;
                Vector2 sampleCenter = Vector2.Lerp(from, to, t);
                var p = new SmokeParticle(
                    sampleCenter + Main.rand.NextVector2Circular(6f, 6f),
                    shiftDashDirection * Main.rand.NextFloat(1.2f, 3f),
                    PurplePalette.RandomHighlight(),
                    1.3f,
                    20);
                p.GrowthAt1 = 2.2f;
                ParticleSystem.Spawn(p);
            }

            // Advance the anchor for next frame.
            lastDashCenter = to;
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

                if (progress > 0f)
                {
                    var puff = new SmokeParticle(
                        player.itemLocation,
                        thrustDir * Main.rand.NextFloat(1f, 2.4f),
                        PurplePalette.RandomCloud(),
                        1.3f,
                        22);
                    puff.GrowthAt1 = 2.0f;
                    ParticleSystem.Spawn(puff);
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

            // Cloudy purple puffs at the blade tip (skip on the
            // pre-positioning call made from UseItem so we don't
            // double-spawn on frame 0).
            if (progress > 0f)
            {
                Vector2 bladeTip = player.Center + new Vector2(30f, 0f).RotatedBy(worldAngle);
                var puff = new SmokeParticle(
                    bladeTip,
                    player.velocity * 0.5f + Main.rand.NextVector2Circular(0.6f, 0.6f),
                    PurplePalette.RandomCloud(),
                    1.3f + Main.rand.NextFloat() * 0.4f,
                    20 + Main.rand.Next(6));
                puff.GrowthAt1 = 2.0f;
                ParticleSystem.Spawn(puff);
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


        // Smoky purple motes wandering along the blade's hitbox.
        public override void MeleeEffects(Player player, Rectangle hitbox)
        {
            if (Main.rand.NextBool(2))
            {
                Vector2 pos = new Vector2(
                    hitbox.X + Main.rand.NextFloat(hitbox.Width),
                    hitbox.Y + Main.rand.NextFloat(hitbox.Height));
                var p = new SmokeParticle(
                    pos,
                    Main.rand.NextVector2Circular(0.8f, 0.8f),
                    PurplePalette.RandomCloud(),
                    1.0f + Main.rand.NextFloat() * 0.4f,
                    18);
                p.GrowthAt1 = 1.8f;
                ParticleSystem.Spawn(p);
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
