using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Materials
{
    internal class BlackStonePow : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Black Stone Powder"); 
            Tooltip.SetDefault("You feel a faint flow of power running through these tiny shards.");
        }
         
        public override void SetDefaults()
        {
            Item.width = 40;
            Item.height = 40;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.autoReuse = true;
            Item.maxStack = 999;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            if (WorldGen.crimson)
            {
                recipe.AddIngredient(ItemID.CrimstoneBlock, 5);
            } else
            {
                recipe.AddIngredient(ItemID.EbonstoneBlock, 5);
            }
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }

    
    }
}
