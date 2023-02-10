using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;
using BDOhehe.Items.Materials;

namespace BDOhehe.Items.Armour.HeveArmor
{
    
    internal class HeveBody : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Strength Shoes of Heve");
            Tooltip.SetDefault("Set Bonus: +50 Max HP\n" +
                               "Melee Inflects On Fire!\n" +
                               "Armor blessed by Hebe, the goddess of youth.");
        }

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 15;
            Item.bodySlot = 0;
            Item.legSlot = -1;
            Item.headSlot = -1;
        }

        public override void UpdateEquip(Player player)
        {
            if ((player.armor[1].type == ModContent.ItemType<HeveBody>()) &&
                 (player.armor[2].type == ModContent.ItemType<HeveShoes>()) &&
                 (player.armor[0].type == ModContent.ItemType<HeveHelm>()))
            {
                player.statLifeMax2 += 50;
            }
            player.moveSpeed -= 0.1f;
        }

        public override void OnHitNPC(Player player, NPC target, int damage, float knockBack, bool crit)
        {
            if ((player.armor[1].type == ModContent.ItemType<HeveBody>()) &&
                 (player.armor[2].type == ModContent.ItemType<HeveShoes>()) &&
                 (player.armor[0].type == ModContent.ItemType<HeveHelm>()))
            {
                target.AddBuff(BuffID.OnFire, 180);
            }
        }


        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<IronIngot>(), 3);
            recipe.AddIngredient(ItemID.Silk, 10);
            recipe.AddIngredient(ItemID.LifeCrystal, 2);
            recipe.AddTile(TileID.HeavyWorkBench);
            recipe.Register();
        }
    }
}
