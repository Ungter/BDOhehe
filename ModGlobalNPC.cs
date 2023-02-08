using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent.ItemDropRules;
using BDOhehe.Items.Materials;

namespace BDOhehe
{
    internal class ModGlobalNPC : GlobalNPC
    {
        
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            if (checkSoftHideDrops(npc))
            {
                npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<Items.Materials.SoftHide>(), 2, 2, 3));
            }
        }

        // Soft Hide Drops 
        public Boolean checkSoftHideDrops(NPC npc) 
        {
            return (npc.type == NPCID.Zombie || npc.type == NPCID.Squirrel || npc.type == NPCID.Seagull
                || npc.type == NPCID.Rat || npc.type == NPCID.SquirrelRed || npc.type == NPCID.SnowFlinx
                || npc.type == NPCID.DiggerBody || npc.type == NPCID.DiggerHead || npc.type == NPCID.DiggerTail
                || npc.type == NPCID.GoblinScout || npc.type == NPCID.Vulture || npc.type == NPCID.EaterofSouls
                || npc.type == NPCID.Crimera || npc.type == NPCID.Duck || npc.type == NPCID.Frog || npc.type == NPCID.Shark
                || npc.type == NPCID.SandShark || npc.type == NPCID.Guide);
        }
    }
}
