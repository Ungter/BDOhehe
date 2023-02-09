using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Armour
{
    internal class TalisHead : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Talis Head");
            Tooltip.SetDefault(
                                "\nThis armor has reduced defense in return for better flexibility. Mostly worn by nobles." +
                                "\nEquipping 3 parts will trigger the set effect");
        }

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 4;
            Item.bodySlot = -1;
            Item.shoeSlot = -1;
            Item.headSlot = 0;
        }


        public override void UpdateEquip(Player player)
        {
            if ((player.armor[1].type == ModContent.ItemType<TalisBody>()) &&
                 (player.armor[2].type == ModContent.ItemType<TalisShoes>()) &&
                 (player.armor[0].type == ModContent.ItemType<TalisHead>()))
            {
                player.setBonus = "+10% movement speed";
                player.moveSpeed += 0.1f;
                player.manaRegenBonus += 1;

            }
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<Materials.SoftHide>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Materials.IronIngot>(), 12);
            recipe.AddIngredient(ModContent.ItemType<Materials.BlackStonePow>(), 30);
            recipe.AddIngredient(ModContent.ItemType<Materials.RedCrystal>(), 2);
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }
    }
}
