using DiscoAccess.Core.World;
using Xunit;

namespace DiscoAccess.Tests
{
    public class EntityNamingTests
    {
        private static string Resolve(string? name, string? authored = null, string? title = null, bool named = false, string cat = WorldTaxonomy.Container)
            => EntityNaming.Resolve(name, authored, title, named, cat);

        [Fact]
        public void AuthoredConversant_IsPreferredForProps()
        {
            // The game's own examine name beats the noun extractor: "Pile of Eternite", not "eternite".
            Assert.Equal("Pile of Eternite",
                Resolve("Eternite_door", authored: "Pile of Eternite", title: "YARD / PILE OF ETERNITE", cat: WorldTaxonomy.Other));
            Assert.Equal("Drainage Pipe", Resolve("Drainage Pipe", authored: "Drainage Pipe", cat: WorldTaxonomy.Other));
        }

        [Fact]
        public void NamedCharacter_TitleActorName_KeepsNameBeforeComma()
        {
            // "Name, the Title" actor names read just the name: "Garte", not the discarded lowercase fallback.
            Assert.Equal("Garte", Resolve("garte", authored: "Garte, the Cafeteria Manager", named: true, cat: WorldTaxonomy.Npc));
            Assert.Equal("Lilienne", Resolve("npc_lilienne", authored: "Lilienne, the Net Picker", named: true, cat: WorldTaxonomy.Npc));
        }

        [Fact]
        public void NamedCharacter_PrefersAuthoredName_DroppingLocationPrefix()
        {
            // "Yard Cuno" is the GameObject name; the authored actor name "Cuno" is what the game shows.
            Assert.Equal("Cuno", Resolve("Yard Cuno", authored: "Cuno", named: true, cat: WorldTaxonomy.Npc));
            // Even a slug GameObject name yields the real name when the game authored one.
            Assert.Equal("Cuno", Resolve("npc_cunoesse", authored: "Cuno", named: true, cat: WorldTaxonomy.Npc));
        }

        [Fact]
        public void Exit_NamedByDestination_WithPortalType_HyphensBecomeSpaces()
        {
            // An exit reads the place it leads to (localized, hyphens spoken as spaces) plus the portal type
            // read from the GameObject.name: "Whirling-in-Rags" through a "...-door-..." reads "Whirling in
            // Rags door".
            Assert.Equal("Whirling in Rags door",
                Resolve("waterfront-door-rooftop", authored: "Whirling-in-Rags", cat: WorldTaxonomy.Exit));
            Assert.Equal("Cuno's shack door",
                Resolve("courtyard-door-cunos-shack", authored: "Cuno's shack", cat: WorldTaxonomy.Exit));
            // No portal word in the name falls back to the generic "exit": the tent flap's name is just "tent".
            Assert.Equal("Tent exit", Resolve("tent", authored: "Tent", cat: WorldTaxonomy.Exit));
            // A harbour gate reads "gate"; an inter-floor staircase reads "stairs".
            Assert.Equal("Docks gate", Resolve("harbor-gate-1", authored: "Docks", cat: WorldTaxonomy.Exit));
            Assert.Equal("Whirling in Rags stairs",
                Resolve("whirling-stairs-f2", authored: "Whirling-in-Rags", cat: WorldTaxonomy.Exit));
        }

        [Fact]
        public void ExitDestination_FloorWhenNamesCollide_ElseTheDistinctName()
        {
            // All Whirling floors share "Whirling-in-Rags", so the shared name says nothing: use the floor.
            Assert.Equal("floor 2",
                EntityNaming.ExitDestinationLabel("Whirling-int-f2", "Whirling-in-Rags", "Whirling-in-Rags"));
            Assert.Equal("floor 3",
                EntityNaming.ExitDestinationLabel("Whirling-int-f3-antechamber", "Whirling-in-Rags", "Whirling-in-Rags"));
            // The Doomed basement (-s1) shares its name with the floor above, so it reads "basement".
            Assert.Equal("basement",
                EntityNaming.ExitDestinationLabel("Doomed-commerce-int-s1", "Doomed Commercial Area", "Doomed Commercial Area"));
            // A floor the game names distinctly is preferred over a floor number: "Bookstore", not "floor 1".
            Assert.Equal("Bookstore",
                EntityNaming.ExitDestinationLabel("Doomed-commerce-int-f1", "Bookstore", "Doomed Commercial Area"));
            // A different building is distinct, so its own name is used (hyphens spaced later, in Resolve).
            Assert.Equal("Whirling-in-Rags",
                EntityNaming.ExitDestinationLabel("Whirling-int-f1", "Whirling-in-Rags", "Martinaise"));
        }

        [Fact]
        public void InterFloorExit_ComposesWithType()
        {
            // The proxy passes the floor label as the authored name; the exit branch appends the portal type.
            Assert.Equal("floor 2 stairs",
                Resolve("Whirling 1st stairs", authored: "floor 2", cat: WorldTaxonomy.Exit));
        }

        [Fact]
        public void SpotDoor_NamedForTheSpot_DefaultsToDoor()
        {
            // An exterior door named for a specific spot (the proxy passes no destination so the coarse
            // "Martinaise" does not hide the spot): the spot leads, defaulting to "door".
            Assert.Equal("balcony door", Resolve("Balcony", cat: WorldTaxonomy.Exit));
            Assert.Equal("roof door", Resolve("Roof", cat: WorldTaxonomy.Exit));
        }

        [Fact]
        public void Exit_Elevator_ReadsElevator()
        {
            Assert.Equal("floor 3 elevator",
                Resolve("whirl1-elevator-whirl3", authored: "floor 3", cat: WorldTaxonomy.Exit));
        }

        [Fact]
        public void Exit_NoDestination_FallsBackToTypeWord()
        {
            Assert.Equal("door", Resolve("some-door-slug", cat: WorldTaxonomy.Exit)); // slug with a portal word
            Assert.Equal("exit", Resolve("trigger_zone", cat: WorldTaxonomy.Exit));  // slug, no portal word
        }

        [Fact]
        public void SpotFromDoorName_OnlyCleanNonTypePlaces()
        {
            Assert.Equal("balcony", EntityNaming.SpotFromDoorName("Balcony"));
            Assert.Null(EntityNaming.SpotFromDoorName("exit-courtyard"));   // a slug id
            Assert.Null(EntityNaming.SpotFromDoorName("Whirling Door"));    // contains a portal type
            Assert.Null(EntityNaming.SpotFromDoorName("Stairs"));           // is a portal type
        }

        [Fact]
        public void Door_IgnoresAuthored()
        {
            Assert.Equal("Whirling Door", Resolve("Whirling Door", authored: "Someone", cat: WorldTaxonomy.Door));
        }

        [Fact]
        public void UnusableAuthored_IsRejected_FallingBackToTheNoun()
        {
            Assert.Equal("crate", Resolve("Harbor Crate 22", authored: "You"));        // the player
            Assert.Equal("box", Resolve("box_3 rooftop", authored: "actor_5"));         // a machine id
            Assert.Equal("crate", Resolve("Harbor Crate 22", authored: "   "));         // blank
            Assert.Equal("stone", Resolve("stone_x", authored: "STONE PERC", cat: WorldTaxonomy.Other)); // meta token
        }

        [Fact]
        public void Container_SpeaksTheObjectNoun_NotTheLocation()
        {
            // The clean "<location> <object>" name reduces to the object noun, lowercased.
            Assert.Equal("crate", Resolve("Harbor Crate 22"));
            Assert.Equal("crate", Resolve("Fishmarket Crate"));
            Assert.Equal("bucket", Resolve("Yard Bucket"));
            Assert.Equal("money", Resolve("Church Bench Money"));
            Assert.Equal("metalbox", Resolve("Waterlock Metalbox"));
        }

        [Fact]
        public void Container_StripsDuplicateSuffixesBeforeExtracting()
        {
            Assert.Equal("crate", Resolve("Crate (2)"));
            Assert.Equal("money", Resolve("Harbor Wall Money 1 (2)"));
            Assert.Equal("can", Resolve("Can (Clone)"));
        }

        [Fact]
        public void SlugClutter_NounIsBeforeTheUnderscore()
        {
            // "object_index location" - the noun is the token before the underscore.
            Assert.Equal("box", Resolve("box_3 rooftop"));
            Assert.Equal("crate", Resolve("crate_1 gate"));
            Assert.Equal("crate", Resolve("crate_landsend"));
        }

        [Fact]
        public void AdjectiveNoun_KeepsTheNoun()
        {
            // "empty bottle" is adjective-then-noun: the noun is the last word.
            Assert.Equal("bottle", Resolve("empty bottle"));
        }

        [Fact]
        public void HyphenName_NounIsTheLastWord_TitleIgnored()
        {
            // The title here is a location ("RAILING"); the name still yields the better noun.
            Assert.Equal("jacket", Resolve("Filthy-jacket", title: "BOARDWALK / RAILING"));
        }

        [Fact]
        public void LocationLeadingSlug_PrefersSpoilerSafeTitle()
        {
            // "Ice_eternite" - the noun extractor would guess the location "ice"; the title names it.
            Assert.Equal("Eternite", Resolve("Ice_eternite", title: "ICE / ETERNITE", cat: WorldTaxonomy.Other));
            Assert.Equal("Pile Of Eternite",
                Resolve("Eternite_door", title: "YARD / PILE OF ETERNITE", cat: WorldTaxonomy.Other));
        }

        [Fact]
        public void LocationLeadingSlug_UnsafeTitle_FallsBackToExtractedNoun()
        {
            // The title is rejected (a check word), so the pre-underscore token is spoken instead.
            Assert.Equal("stone", Resolve("stone_perc_1", title: "STONE PERC", cat: WorldTaxonomy.Other));
        }

        [Fact]
        public void EmptyName_NoTitle_FallsBackToCategoryWord()
        {
            Assert.Equal("container", Resolve("", cat: WorldTaxonomy.Container));
            Assert.Equal("object", Resolve(null, cat: WorldTaxonomy.Other));
        }

        [Fact]
        public void NamedCharacter_KeepsFullName()
        {
            Assert.Equal("Kim Kitsuragi", Resolve("Kim Kitsuragi", named: true, cat: WorldTaxonomy.Npc));
            Assert.Equal("Cunoesse", Resolve("Cunoesse", named: true, cat: WorldTaxonomy.Npc));
        }

        [Fact]
        public void NamedCharacter_HyphenatedName_IsKept()
        {
            // A real display name hyphenates with its capitals intact, so it is not a machine slug and is
            // spoken in full rather than reduced to the generic "person".
            Assert.Equal("Jean-Vicquemare", Resolve("Jean-Vicquemare", named: true, cat: WorldTaxonomy.Npc));
        }

        [Fact]
        public void NamedCharacterSlug_ReadsPerson_NeverTheTitle()
        {
            Assert.Equal("person",
                Resolve("npc_cunoesse", title: "CUNOESSE", named: true, cat: WorldTaxonomy.Npc));
        }

        [Fact]
        public void Door_KeepsCleanNameElseCategoryWord()
        {
            Assert.Equal("door", Resolve("courtyard-door-crypto-garys-apt", cat: WorldTaxonomy.Door));
            Assert.Equal("Whirling Door", Resolve("Whirling Door", cat: WorldTaxonomy.Door));
        }
    }
}
