using DiscoAccess.Core.World;
using FortressOccident;
using Sunshine;
using Vector3 = System.Numerics.Vector3;

namespace DiscoAccess.Module.World
{
    /// <summary>
    /// The <see cref="IWorldItem"/> over a live <see cref="BasicEntity"/> (NPC, door, exit, container,
    /// prop). Reads everything live and classifies by the game's own <see cref="Interactable"/> subclass
    /// tree via <c>TryCast</c> (not GetType, which the interop boxes to BasicEntity).
    /// </summary>
    internal sealed class EntityProxy : IWorldItem
    {
        private readonly BasicEntity _e;

        public EntityProxy(BasicEntity e) { _e = e; }

        public string Name => _e.name;
        public Vector3 Position => WorldConvert.ToSnv(_e.transform.position);
        public ScanBounds Bounds => ScanBounds.Point(Position);
        public string Category => Classify(_e);
        public bool IsAccessible => _e.IsAccessible;
        public bool IsVisible => true; // present in the scene; fog/streaming refinement comes later

        public bool Interact() => _e.Interact(new Interactable.ClickEventData());

        // Map the entity's runtime type onto a taxonomy category. Order matters where types nest (Curtains
        // derives from Door). TravelDestination/Teleporter exits derive from NavMeshClickHandler, not
        // BasicEntity, so they are not in sceneEntitySet and not seen here; TransitionEntity is.
        private static string Classify(BasicEntity e)
        {
            if (e.TryCast<Character>() != null) return WorldTaxonomy.Npc;
            if (e.TryCast<Door>() != null) return WorldTaxonomy.Door;
            if (e.TryCast<TransitionEntity>() != null) return WorldTaxonomy.Exit;
            if (e.TryCast<ContainerSource>() != null) return WorldTaxonomy.Container;
            return WorldTaxonomy.Other;
        }
    }
}
