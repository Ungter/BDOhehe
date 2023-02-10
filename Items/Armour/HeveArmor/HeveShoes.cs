using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;
using BDOhehe.Items.Materials;

namespace BDOhehe.Items.Armour.HeveArmor
{
    internal class HeveShoes : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Strength Shoes of Heve");
            Tooltip.SetDefault("+20 Max HP\n" +
                               "Set Bonus: +50 Max HP\n" +
                               "Melee Inflects On Fire!\n" +
                               "Striders blessed by Hebe, the goddess of youth.");
        }

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 15;
            Item.bodySlot = -1;
            Item.legSlot = 0;
            Item.headSlot = -1;
        }

        public override void UpdateEquip(Player player)
        {
            player.statLifeMax2 += 20;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<IronIngot>(), 3);
            recipe.AddIngredient(ItemID.Cactus, 10);
            recipe.AddIngredient(ItemID.LifeCrystal, 1);
            recipe.AddTile(TileID.HeavyWorkBench);
            recipe.Register();
        }
    }
}
