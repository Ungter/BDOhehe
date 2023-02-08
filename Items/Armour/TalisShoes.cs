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
            if ((player.armor[0].type == ModContent.ItemType<Items.Armour.TalisHead>()) &&
                 (player.armor[1].type == ModContent.ItemType<Items.Armour.TalisBody>()) &&
                 (player.armor[2].type == ModContent.ItemType<Items.Armour.TalisShoes>()))
            {
                player.setBonus = "+10% movement speed";
                player.moveSpeed += 0.1f;
            }
            player.moveSpeed += 0.20f;
        }

        

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.SoftHide>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.IronIngot>(), 8);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.BlackStonePow>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.RedCrystal>(), 1);
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }



    }
}
