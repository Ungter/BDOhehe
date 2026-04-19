using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace BDOhehe.Particles
{
    // Vertex-primitive trail. Instead of drawing a stretched sprite, we feed
    // a ribbon of VertexPositionColorTexture points to the GPU and let the
    // rasterizer connect them into a quad strip. This is exactly how
    // Calamity's sword/boss-dash trails are built: they record a rolling
    // buffer of world positions, build left/right ribbon edges across each
    // segment, and hand the result to a BasicEffect with additive blending.
    //
    // No custom .fx is required (BasicEffect is a vanilla XNA/FNA shader),
    // which keeps this drop-in for tModLoader without asset pipeline work.
    public class PrimitiveTrail
    {
        public bool Active = true;
        public int Lifetime = 30;
        public int Time;
        public float Width = 12f;
        public Color StartColor = new Color(230, 170, 255);
        public Color EndColor = new Color(80, 20, 140);

        private readonly Queue<Vector2> points = new Queue<Vector2>();
        private readonly int maxPoints;

        public PrimitiveTrail(int maxPoints = 14)
        {
            this.maxPoints = Math.Max(2, maxPoints);
        }

        // Push a new world-space point to the head of the trail. Older points
        // are dropped once maxPoints is reached.
        public void Append(Vector2 worldPoint)
        {
            points.Enqueue(worldPoint);
            while (points.Count > maxPoints) points.Dequeue();
        }

        public void Update()
        {
            Time++;
            if (Time >= Lifetime) Active = false;
        }

        // Draws the trail as a triangle strip of textured ribbon quads.
        // Requires the SpriteBatch has been flushed before calling -- see
        // ParticleSystem which flushes/re-begins before invoking trails.
        public void Draw(GraphicsDevice gd, Matrix transform)
        {
            if (points.Count < 2) return;

            Vector2[] pts = points.ToArray();
            int segCount = pts.Length - 1;
            VertexPositionColorTexture[] verts =
                new VertexPositionColorTexture[(segCount + 1) * 2];

            float life = Lifetime <= 0 ? 0f : (float)Time / Lifetime;
            float globalFade = MathHelper.Clamp(1f - life, 0f, 1f);

            for (int i = 0; i < pts.Length; i++)
            {
                // Normal = perpendicular to the local tangent.
                Vector2 tangent;
                if (i == 0) tangent = pts[1] - pts[0];
                else if (i == pts.Length - 1) tangent = pts[i] - pts[i - 1];
                else tangent = pts[i + 1] - pts[i - 1];

                if (tangent.LengthSquared() < 0.0001f) tangent = Vector2.UnitX;
                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.Y, tangent.X);

                // Taper the ribbon: thin at the tail (oldest point i=0),
                // full width at the head (newest point i=last).
                float t = (float)i / (pts.Length - 1);
                float w = Width * t;
                Color col = Color.Lerp(EndColor, StartColor, t) * globalFade;

                Vector2 left = pts[i] + normal * w;
                Vector2 right = pts[i] - normal * w;

                verts[i * 2] = new VertexPositionColorTexture(
                    new Vector3(left, 0f), col, new Vector2(t, 0f));
                verts[i * 2 + 1] = new VertexPositionColorTexture(
                    new Vector3(right, 0f), col, new Vector2(t, 1f));
            }

            int triCount = segCount * 2;
            short[] indices = new short[triCount * 3];
            int idx = 0;
            for (int i = 0; i < segCount; i++)
            {
                short a = (short)(i * 2);
                short b = (short)(i * 2 + 1);
                short c = (short)(i * 2 + 2);
                short d = (short)(i * 2 + 3);
                indices[idx++] = a;
                indices[idx++] = b;
                indices[idx++] = c;
                indices[idx++] = b;
                indices[idx++] = d;
                indices[idx++] = c;
            }

            BasicEffect fx = ParticleSystem.TrailEffect;
            fx.Texture = ParticleSystem.GlowStreak;
            fx.TextureEnabled = true;
            fx.VertexColorEnabled = true;
            fx.World = Matrix.Identity;
            fx.View = transform;
            fx.Projection = Matrix.CreateOrthographicOffCenter(
                0f, gd.Viewport.Width, gd.Viewport.Height, 0f, 0f, 1f);

            foreach (EffectPass pass in fx.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    verts, 0, verts.Length,
                    indices, 0, triCount);
            }
        }
    }
}
