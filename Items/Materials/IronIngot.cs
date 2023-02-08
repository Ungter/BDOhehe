using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Materials
{
    internal class IronIngot : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Iron Ingot"); 
            Tooltip.SetDefault("A processed natural resource obtained through smelting iron ore.");
        }
         
        public override void SetDefaults()
        {
            Item.width = 40;
            Item.height = 40;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = 1;
            Item.value = 10000;
            Item.rare = 2;
            Item.autoReuse = true;
            Item.maxStack = 999;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ItemID.IronOre, 2);
            recipe.AddTile(TileID.Furnaces);
            recipe.Register();
        }
    }

}
