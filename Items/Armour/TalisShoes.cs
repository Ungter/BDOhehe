using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Armour
{
    internal class TalisShoes : ModItem
    {
        // basic armor
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Talis Shoes");
            Tooltip.SetDefault("+10% Movement Speed" +
                               "\nThis armor has reduced defense in return for better flexibility. Mostly worn by nobles.");
        }

        public override void SetDefaults()
        {
            Item.width = 18;
            Item.height = 18;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 3;
            Item.legSlot = 0;
            Item.headSlot = -1;
            Item.bodySlot = -1;
            
        }

        public override void UpdateEquip(Player player)
        {
            if ((player.armor[0].type == ModContent.ItemType<TalisHead>()) &&
                 (player.armor[1].type == ModContent.ItemType<TalisBody>()) &&
                 (player.armor[2].type == ModContent.ItemType<TalisShoes>()))
            {
                player.setBonus = "+10% movement speed";
                player.moveSpeed += 0.1f;
            }
            player.moveSpeed += 0.20f;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<Materials.SoftHide>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Materials.IronIngot>(), 8);
            recipe.AddIngredient(ModContent.ItemType<Materials.BlackStonePow>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Materials.RedCrystal>(), 1);
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }



    }
}
