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
            Tooltip.SetDefault("This armor has reduced defense in return for better flexibility. Mostly worn by nobles.\n" +
                               "+7% Movement Speed");
        }

        public override void SetDefaults()
        {
            Item.width = 18;
            Item.height = 18;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 5;
            Item.legSlot = 0;
            Item.headSlot = -1;
            Item.bodySlot = -1;
            
        }

        public override void UpdateEquip(Player player)
        {
            player.moveSpeed += 0.07f;
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
