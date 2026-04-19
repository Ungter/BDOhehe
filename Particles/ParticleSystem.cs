using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace BDOhehe.Particles
{
    // Central manager for BaseParticle instances.
    //
    // - Updates every active particle once per frame (hooked via ModSystem).
    // - Draws every particle in a SINGLE batched SpriteBatch.Begin call with
    //   BlendState.Additive so overlapping particles brighten toward white,
    //   black pixels become transparent, and the whole thing reads as HD
    //   glow instead of pixel-grid dust.
    // - Generates its own high-res (128x128) radial gradient texture at Load
    //   so particle implementations can scale it down to whatever visible
    //   size they need without the 2x2 pixel-grid crunch of Terraria dust.
    public class ParticleSystem : ModSystem
    {
        // Hard cap so misbehaving callers can't run the list away.
        private const int MaxParticles = 4000;

        private static readonly List<BaseParticle> particles = new List<BaseParticle>(512);
        private static readonly List<BaseParticle> pending = new List<BaseParticle>(64);
        private static readonly List<PrimitiveTrail> trails = new List<PrimitiveTrail>(32);
        private static readonly List<PrimitiveTrail> pendingTrails = new List<PrimitiveTrail>(8);

        // HD soft gradient orb (filled at Load on the client).
        public static Texture2D GlowOrb { get; private set; }
        // HD soft line/streak (oblong gradient) used for sparks/streaks.
        public static Texture2D GlowStreak { get; private set; }
        // BasicEffect used by PrimitiveTrail for vertex-primitive ribbons.
        public static BasicEffect TrailEffect { get; private set; }

        public override void Load()
        {
            if (Main.dedServ) return;

            // FNA3D requires Texture2D/BasicEffect construction on the main
            // thread. Mod Load runs on a worker thread, so we enqueue the
            // graphics setup and then block until it completes.
            Main.QueueMainThreadAction(() =>
            {
                GenerateTextures();
                TrailEffect = new BasicEffect(Main.instance.GraphicsDevice);
            });

            On_Main.DrawDust += DrawDustHook;
        }

        public override void Unload()
        {
            if (!Main.dedServ)
            {
                On_Main.DrawDust -= DrawDustHook;
                Main.QueueMainThreadAction(() =>
                {
                    GlowOrb?.Dispose();
                    GlowStreak?.Dispose();
                    TrailEffect?.Dispose();
                    GlowOrb = null;
                    GlowStreak = null;
                    TrailEffect = null;
                });
            }

            particles.Clear();
            pending.Clear();
            trails.Clear();
            pendingTrails.Clear();
        }

        // Drive particle physics from the same tick dust updates on. This runs
        // on the client only (particles are purely visual).
        public override void PostUpdateDusts()
        {
            if (Main.dedServ) return;

            if (pending.Count > 0)
            {
                particles.AddRange(pending);
                pending.Clear();
            }
            if (pendingTrails.Count > 0)
            {
                trails.AddRange(pendingTrails);
                pendingTrails.Clear();
            }

            for (int i = 0; i < particles.Count; i++)
            {
                BaseParticle p = particles[i];
                if (!p.Active) continue;
                p.Update();
            }
            for (int i = 0; i < trails.Count; i++)
            {
                PrimitiveTrail t = trails[i];
                if (!t.Active) continue;
                t.Update();
            }

            // Compact: remove dead entries in place so memory is reused.
            int write = 0;
            for (int read = 0; read < particles.Count; read++)
            {
                BaseParticle p = particles[read];
                if (p.Active)
                {
                    if (write != read) particles[write] = p;
                    write++;
                }
            }
            if (write < particles.Count)
                particles.RemoveRange(write, particles.Count - write);

            int twrite = 0;
            for (int read = 0; read < trails.Count; read++)
            {
                PrimitiveTrail t = trails[read];
                if (t.Active)
                {
                    if (twrite != read) trails[twrite] = t;
                    twrite++;
                }
            }
            if (twrite < trails.Count)
                trails.RemoveRange(twrite, trails.Count - twrite);
        }

        // Called via On_Main.DrawDust so our particles render at the same
        // layer dust does (above most tiles, below the UI), with their own
        // additive batch.
        private static void DrawDustHook(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);

            if (particles.Count == 0 && pending.Count == 0 &&
                trails.Count == 0 && pendingTrails.Count == 0) return;
            if (GlowOrb == null) return;

            SpriteBatch sb = Main.spriteBatch;
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            Matrix viewMatrix = Main.GameViewMatrix.TransformationMatrix;

            // --- Vertex primitive trails (drawn first so particles overlay) ---
            if (trails.Count > 0 && TrailEffect != null)
            {
                BlendState prevBlend = gd.BlendState;
                DepthStencilState prevDepth = gd.DepthStencilState;
                RasterizerState prevRaster = gd.RasterizerState;
                SamplerState prevSampler = gd.SamplerStates[0];

                gd.BlendState = BlendState.Additive;
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearClamp;

                for (int i = 0; i < trails.Count; i++)
                {
                    PrimitiveTrail t = trails[i];
                    if (!t.Active) continue;
                    t.Draw(gd, viewMatrix);
                }

                gd.BlendState = prevBlend;
                gd.DepthStencilState = prevDepth;
                gd.RasterizerState = prevRaster;
                gd.SamplerStates[0] = prevSampler;
            }

            // --- Additive sprite particles ---
            sb.Begin(
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                viewMatrix);

            for (int i = 0; i < particles.Count; i++)
            {
                BaseParticle p = particles[i];
                if (!p.Active) continue;
                p.Draw(sb);
            }

            sb.End();
        }

        // Public spawn entry point. Called from anywhere in the mod. Pending
        // particles are appended on the next PostUpdateDusts tick so the
        // iteration above is never mutated mid-frame.
        public static T Spawn<T>(T particle) where T : BaseParticle
        {
            if (Main.dedServ || particle == null) return particle;
            if (particles.Count + pending.Count >= MaxParticles) return particle;
            pending.Add(particle);
            return particle;
        }

        public static PrimitiveTrail SpawnTrail(PrimitiveTrail trail)
        {
            if (Main.dedServ || trail == null) return trail;
            pendingTrails.Add(trail);
            return trail;
        }

        public static void Clear()
        {
            particles.Clear();
            pending.Clear();
            trails.Clear();
            pendingTrails.Clear();
        }

        // Generate a 128x128 soft radial gradient (the "glow orb"). Black at
        // the edges so additive blending drops it away cleanly, bright white
        // at the center so color tints work across the whole value range.
        private static void GenerateTextures()
        {
            GraphicsDevice gd = Main.instance.GraphicsDevice;

            const int orbSize = 128;
            Texture2D orb = new Texture2D(gd, orbSize, orbSize);
            Color[] orbData = new Color[orbSize * orbSize];
            float cx = (orbSize - 1) * 0.5f;
            float cy = (orbSize - 1) * 0.5f;
            float maxDist = cx;
            for (int y = 0; y < orbSize; y++)
            {
                for (int x = 0; x < orbSize; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float d = (float)System.Math.Sqrt(dx * dx + dy * dy) / maxDist;
                    float a = MathHelper.Clamp(1f - d, 0f, 1f);
                    // Smooth curve: a^2 * (3 - 2a) is a smoothstep (soft falloff).
                    a = a * a * (3f - 2f * a);
                    byte v = (byte)(a * 255f);
                    // Pre-multiplied feel: RGB == A. Additive treats black as
                    // transparent, so no hard border pixels either way.
                    orbData[y * orbSize + x] = new Color(v, v, v, v);
                }
            }
            orb.SetData(orbData);
            GlowOrb = orb;

            // Streak: a wider-than-tall gradient ellipse for spark/trail motes.
            const int streakW = 128;
            const int streakH = 32;
            Texture2D streak = new Texture2D(gd, streakW, streakH);
            Color[] streakData = new Color[streakW * streakH];
            float sx = (streakW - 1) * 0.5f;
            float sy = (streakH - 1) * 0.5f;
            for (int y = 0; y < streakH; y++)
            {
                for (int x = 0; x < streakW; x++)
                {
                    float nx = (x - sx) / sx;
                    float ny = (y - sy) / sy;
                    float d = (float)System.Math.Sqrt(nx * nx + ny * ny);
                    float a = MathHelper.Clamp(1f - d, 0f, 1f);
                    a = a * a;
                    byte v = (byte)(a * 255f);
                    streakData[y * streakW + x] = new Color(v, v, v, v);
                }
            }
            streak.SetData(streakData);
            GlowStreak = streak;
        }
    }
}
