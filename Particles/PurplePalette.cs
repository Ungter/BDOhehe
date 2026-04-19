using Microsoft.Xna.Framework;
using Terraria;

namespace BDOhehe.Particles
{
    // Central source of purple shades so every Sting effect pulls from the
    // same palette. Spans a wide gamut (deep violet through royal purple to
    // magenta-purple) so overlapping additive particles don't wash toward
    // white.
    public static class PurplePalette
    {
        // Darkest: void/ink purple. Used for cloud cores and afterburn haze.
        public static readonly Color Ink = new Color(35, 5, 70);
        public static readonly Color DeepViolet = new Color(70, 15, 120);
        public static readonly Color RoyalPurple = new Color(110, 30, 170);
        public static readonly Color Amethyst = new Color(150, 55, 210);
        public static readonly Color Orchid = new Color(190, 90, 235);
        // Lightest: soft lavender. Not pure white -- keeps the tint readable.
        public static readonly Color Lavender = new Color(215, 140, 245);
        // Accent: pinkish magenta to mix into clouds for variation.
        public static readonly Color Magenta = new Color(205, 70, 190);

        // Random anywhere from Ink up to Lavender. Biased slightly dark so
        // cloud bodies look dense.
        public static Color RandomCloud()
        {
            Color[] pool = { Ink, DeepViolet, DeepViolet, RoyalPurple, RoyalPurple, Amethyst, Orchid, Magenta };
            Color a = pool[Main.rand.Next(pool.Length)];
            Color b = pool[Main.rand.Next(pool.Length)];
            return Color.Lerp(a, b, Main.rand.NextFloat());
        }

        // Random highlight: mid-to-bright purple for spark/core accents.
        public static Color RandomHighlight()
        {
            Color a = Main.rand.NextBool() ? Amethyst : Orchid;
            Color b = Main.rand.NextBool() ? Orchid : Lavender;
            return Color.Lerp(a, b, Main.rand.NextFloat());
        }

        // Random deep tone: for dark cloud afterburn.
        public static Color RandomDeep()
        {
            Color a = Main.rand.NextBool() ? Ink : DeepViolet;
            Color b = Main.rand.NextBool() ? DeepViolet : RoyalPurple;
            return Color.Lerp(a, b, Main.rand.NextFloat());
        }
    }
}
