using Terraria;
using Terraria.ModLoader;

namespace BDOhehe.Buffs
{
    // Cooldown indicator shown above the player after the Sting Starfall skill.
    // Displays like the Potion Sickness timer (shows remaining seconds) and is a
    // pure visual timer -- it has no gameplay side effects beyond its presence.
    public class StarfallCooldown : ModBuff
    {
        public override string Texture => "BDOhehe/Buffs/StarfallCooldown";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;
            Main.buffNoTimeDisplay[Type] = false;
            Main.buffNoSave[Type] = true;
        }
    }
}
