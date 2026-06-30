using System;
using System.Numerics;
using static DiscoAccess.Core.Strings.Strings;

namespace DiscoAccess.Core.World
{
    /// <summary>
    /// Composes the spoken line for the point under the cursor relative to the player: bearing first (the
    /// distinguishing part), then a whole-metre distance, then an above/below tag when the height differs.
    /// Mod-authored text, so it pulls every word from the strings table; engine-free and unit-tested.
    /// </summary>
    public static class SpatialReadout
    {
        public static string Describe(Vector3 reference, Vector3 cursor)
        {
            if (Geo.IsHere(reference, cursor)) return WorldHere;

            string line = WorldCompass(Geo.CompassIndex(reference, cursor))
                + ", " + WorldDistance((int)Math.Round(Geo.Distance(reference, cursor)));

            int vertical = Geo.VerticalSign(reference, cursor);
            if (vertical > 0) line += ", " + WorldAbove;
            else if (vertical < 0) line += ", " + WorldBelow;
            return line;
        }
    }
}
