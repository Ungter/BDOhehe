using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using System;

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
            Item.useAnimation = 10;
            Item.useStyle = ItemUseStyleID.Thrust;
            Item.knockBack = 6;
            Item.value = 10000;
            Item.rare = ItemRarityID.LightRed;
            Item.UseSound = SoundID.AbigailAttack;
            Item.autoReuse = false;
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
            // Supposed to center the sprite but it's not working
            player.itemLocation.X = player.position.X;
            player.itemLocation.Y = player.position.Y;

            // Turns player to the left or right depending on the cursor and where it's pressed
            // along with A and D keys
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A) ||
                (Main.mouseLeft && Main.mouseX <= Main.screenWidth / 2) ) 
            {
                player.direction = -1;
                

            }
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D) ||
                (Main.mouseLeft && Main.mouseX >= Main.screenWidth / 2))
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

        // This hook is called whenever the player attacks
        public override void OnHitNPC(Player player, NPC target, int damage, float knockback, bool crit)
        {
            // Count the number of hits
            int hitCount = 0;

            // Increment the hit count
            hitCount++;

            // If the hit count is 3, reset it and give the player a buff
            if (hitCount >= 3)
            {
                hitCount -= hitCount; 
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
