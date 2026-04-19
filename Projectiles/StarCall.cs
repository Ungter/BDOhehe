using Microsoft.Xna.Framework;
using ReLogic.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace BDOhehe.Projectiles
{
    // Invisible parent projectile for the Star's Call skill.
    // Its entire job is to lock the player in place while the crown of
    // StarfallCones (spawned alongside it in Sting.cs) runs its delay.
    // When this projectile dies -- either by timeLeft reaching 0 or by an
    // external Kill() (Comet cancel) -- the cones' own delay counters will
    // either launch normally or be faded out by the caller.
    public class StarCall : ModProjectile
    {
        public Vector2 LockPosition;

        // Wind-up sound slot owned by the Sting item. Stopped in OnKill
        // only when Cancelled == true -- natural expiration lets the cast
        // audio play out over the cone strike-down.
        public SlotId SoundSlot;
        public bool Cancelled;

        // We reuse the Sting sprite path to satisfy autoload; PreDraw returns
        // false so nothing is ever drawn for this projectile.
        public override string Texture => "BDOhehe/Items/Weapons/Awaken/Sting";

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.tileCollide = false;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 7; // overridden by the item on spawn
        }

        public override bool? CanDamage() => false;

        public override bool PreDraw(ref Color lightColor) => false;

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];

            // Lock the owner in place every frame the projectile is alive.
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
        }

        public override void OnKill(int timeLeft)
        {
            if (!Cancelled) return;
            if (SoundSlot.IsValid && SoundEngine.TryGetActiveSound(SoundSlot, out var activeSound))
            {
                activeSound?.Stop();
            }
        }
    }
}
