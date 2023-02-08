using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Armour
{
    internal class TalisBody : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Talis Armor");
            Tooltip.SetDefault( "Increased mana regen rate" +
                                "\nThis armor has reduced defense in return for better flexibility. Mostly worn by nobles." +
                                "\nEquipping 3 parts will trigger the set effect");
        }

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = 10000;
            Item.rare = ItemRarityID.Green;
            Item.defense = 10;
            Item.bodySlot = 0;
            Item.shoeSlot = -1;
            Item.headSlot = -1;
        }

        public override void UpdateEquip(Player player)
        {
            if ((player.armor[1].type == ModContent.ItemType<Items.Armour.TalisBody>()) && 
                (player.armor[2].type == ModContent.ItemType<Items.Armour.TalisShoes>()) && 
                (player.armor[0].type == ModContent.ItemType<Items.Armour.TalisHead>()))
            {
                player.setBonus = "+10% movement speed";
                player.moveSpeed += 0.1f;
            }

            player.manaRegenBonus = (int) (2.875 + 11.75 * (player.statMana/player.statManaMax)); 
        }

        

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.SoftHide>(), 20);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.IronIngot>(), 12);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.BlackStonePow>(), 30);
            recipe.AddIngredient(ModContent.ItemType<Items.Materials.RedCrystal>(), 2);
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }
    }
}
