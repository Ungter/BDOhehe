using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BDOhehe.Particles
{
    // Base class for all custom mod particles.
    //
    // Intentionally does NOT inherit from any tModLoader type. Particles are
    // pure visual data, updated + drawn in one additive batch by
    // ParticleSystem. Keeping them off of ModProjectile/ModDust means we pay
    // no per-particle AI dispatch cost and we are free to break Terraria's
    // 2x2 pixel grid with HD textures drawn at sub-pixel positions.
    public abstract class BaseParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color = Color.White;
        public float Scale = 1f;
        public float Rotation;
        // Applied to Velocity every frame (0.95 = mild drag, 1 = no drag).
        public float Drag = 1f;
        // Frames alive so far.
        public int Time;
        // Total frames the particle should live.
        public int Lifetime = 60;
        // Toggled false by Update when the particle should be recycled.
        public bool Active = true;

        // 0 at spawn, 1 right before death. Useful for alpha/size fades.
        public float LifeRatio => Lifetime <= 0 ? 1f : (float)Time / Lifetime;

        public virtual void Update()
        {
            Position += Velocity;
            Velocity *= Drag;
            Time++;
            if (Time >= Lifetime) Active = false;
        }

        // Called by ParticleSystem inside a single SpriteBatch.Begin with
        // BlendState.Additive already bound. Implementations just issue one
        // (or a few) sprite draws.
        public abstract void Draw(SpriteBatch sb);
    }
}
