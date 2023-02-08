using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Materials
{
    internal class SoftHide : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Soft Hide");
            Tooltip.SetDefault("A processed natural resource obtained through Gathering");
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

        


    }
}
