using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;
using BDOhehe.Items.Materials;

namespace BDOhehe.Items.Armour.HeveArmor
{
    internal class HeveHelm : ModItem
    {

        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Strength Helmet of Heve");
            Tooltip.SetDefault("-10% Movement Speed\n" +
                               "Set Bonus: +50 Max HP\n" +
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
            Item.bodySlot = -1;
            Item.legSlot = -1;
            Item.headSlot = 0;
        }

        public override void UpdateEquip(Player player)
        {
            player.moveSpeed -= 0.1f;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<IronIngot>(), 10);
            recipe.AddIngredient(ItemID.Silk, 10);
            recipe.AddIngredient(ItemID.LifeCrystal, 2);
            recipe.AddTile(TileID.HeavyWorkBench);
            recipe.Register();
        }
    }
}
