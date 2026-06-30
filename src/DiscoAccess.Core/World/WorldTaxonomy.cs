using System.Collections.Generic;

namespace DiscoAccess.Core.World
{
    /// <summary>
    /// The classification of world things the sensing layer reads: which category a thing sounds and is
    /// listed under, and what the "what does the sonar sonify" settings toggle. Kept flat for Disco's
    /// smaller world (the WOTR reference had a two-level tree); subcategories can be added later without
    /// touching call sites. The keys are stable settings-path segments, never spoken; the menu maps them to
    /// authored display names.
    /// </summary>
    public static class WorldTaxonomy
    {
        public const string Npc = "npc";
        public const string Door = "door";
        public const string Exit = "exit";
        public const string Container = "container";
        public const string Orb = "orb";
        public const string Other = "other";

        /// <summary>Every category, in readout order.</summary>
        public static readonly IReadOnlyList<string> All = new[] { Npc, Door, Exit, Container, Orb, Other };
    }
}
