using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;
using System.Runtime.CompilerServices;

namespace BDOhehe.Items.Weapons.Awaken { 

    public class Sting : ModItem
    {
        public override void SetStaticDefaults()
        {
            DisplayName.SetDefault("Sting");
            Tooltip.SetDefault("A weapon that is used by the Black Desert Online's Nova Class");
        }

        public override void SetDefaults()
        {
            Item.damage = 50;
            Item.DamageType = DamageClass.Melee;
            Item.width = 60;
            Item.height = 30;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.useStyle = 3;
            Item.knockBack = 6;
            Item.value = 10000;
            Item.rare = 4;
            Item.UseSound = SoundID.AbigailAttack;
            Item.autoReuse = true;
        }


        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ItemID.DirtBlock, 10);
            recipe.AddTile(TileID.WorkBenches);
            recipe.Register();
        }

        // Listens for the keys A and D, when pressed, it will change the direction the player is facing respectivly 
        public override void HoldItem(Player player)
        {
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A))
            {
                player.direction = -1;
            }
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D))
            {
                player.direction = 1;
            }
            if (Main.mouseX < Main.screenWidth / 2)
            {
                player.direction = -1;
            }
            else
            {
                player.direction = 1;
            }
        }

        // Watches the cursor, the weapon on hand will always swing towards the cursor
        public override void UseStyle(Player player, Rectangle heldItem)
        {
            Vector2 diff = Main.MouseWorld - player.Center;
            diff.Normalize();
            float rot = diff.ToRotation() - MathHelper.PiOver2;
            player.itemRotation = (float)Math.Atan2(diff.Y * player.direction, diff.X * player.direction);
        }

       

        // Count the number of hits
        int hitCount = 0;

        // This hook is called whenever the player attacks
        public override void OnHitNPC(Player player, NPC target, int damage, float knockback, bool crit)
        {
            // Increment the hit count
            hitCount++;

            // If the hit count is 3, reset it and give the player a buff
            if (hitCount >= 3)
            {
                hitCount = 0;
                player.AddBuff(BuffID.Wrath, 600);
            }
        }


        // give the attack light particles that shoot out 
        public override void MeleeEffects(Player player, Rectangle hitbox)
        {
            if (Main.rand.NextBool(3))
            {
                Dust.NewDust(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height, DustID.BubbleBurst_Purple);
            }
        }



    }
}
