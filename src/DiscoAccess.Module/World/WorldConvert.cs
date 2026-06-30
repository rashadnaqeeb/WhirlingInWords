using Snv = System.Numerics.Vector3;
using UVec = UnityEngine.Vector3;

namespace DiscoAccess.Module.World
{
    /// <summary>The one place Unity and System.Numerics vectors convert, at the Module/Core boundary, so no
    /// Unity type crosses into the engine-free Core world layer.</summary>
    internal static class WorldConvert
    {
        public static Snv ToSnv(UVec v) => new Snv(v.x, v.y, v.z);
        public static UVec ToUnity(Snv v) => new UVec(v.X, v.Y, v.Z);
    }
}
