using Terraria;
using Terraria.ModLoader;

namespace BDOhehe.Buffs
{
    // 7-second cooldown indicator for the Sting Frozen Ring skill.
    // Visible above the player's head with a countdown, like a potion timer.
    public class FrozenRingCooldown : ModBuff
    {
        public override string Texture => "BDOhehe/Items/Weapons/Awaken/Sting";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;
            Main.buffNoTimeDisplay[Type] = false;
            Main.buffNoSave[Type] = true;
        }
    }
}
