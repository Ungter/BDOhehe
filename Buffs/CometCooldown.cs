using Terraria;
using Terraria.ModLoader;

namespace BDOhehe.Buffs
{
    // Cooldown indicator shown above the player after the Sting Comet skill.
    // Displays like the Potion Sickness timer (shows remaining seconds) and is a
    // pure visual timer -- it has no gameplay side effects beyond its presence.
    public class CometCooldown : ModBuff
    {
        // Reuse the Sting sprite as the buff icon so we don't need a new texture.
        public override string Texture => "BDOhehe/Items/Weapons/Awaken/Sting";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = false;
            Main.buffNoTimeDisplay[Type] = false;
            Main.buffNoSave[Type] = true;
        }
    }
}
