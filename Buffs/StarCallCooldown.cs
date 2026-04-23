using Terraria;
using Terraria.ModLoader;

namespace BDOhehe.Buffs
{
    // Cooldown indicator shown above the player after the Sting Star's Call skill.
    // Displays like the Potion Sickness timer (shows remaining seconds).
    public class StarCallCooldown : ModBuff
    {
        public override string Texture => "BDOhehe/Buffs/StarCallCooldown";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;
            Main.buffNoTimeDisplay[Type] = false;
            Main.buffNoSave[Type] = true;
        }
    }
}
