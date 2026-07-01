using System;
using System.Globalization;
using System.Text.RegularExpressions;
using static DiscoAccess.Core.Strings.Strings;

namespace DiscoAccess.Core.World
{
    /// <summary>
    /// Resolves the spoken name for a world thing from the raw game data a Module proxy extracts (the
    /// engine-free half of the naming rule, so it is unit-tested). The best source is the game's own authored
    /// name: the examine conversation's CONVERSANT actor, localized ("Cuno", "Pile of Eternite", "Drainage
    /// Pipe") - the same name a sighted player reads on examining the thing. Where that is absent (a container
    /// has no conversation) the name is reconstructed from the designer's <c>GameObject.name</c>, speaking the
    /// object NOUN rather than a bare category word - "crate" and "money" are useful where "container" is not.
    ///
    /// - A named character prefers the authored name (which drops a location prefix, "Yard Cuno" to "Cuno");
    ///   else a clean <c>GameObject.name</c>; else "person", never a raw conversation title (which would leak).
    /// - An exit prefers its authored name too, which the proxy resolves as the localized DESTINATION it leads
    ///   to ("Whirling-in-Rags", "Cuno's shack"), so the player hears where a door goes; else its clean name,
    ///   else the category word "exit". A plain door keeps its own clean name, else "door".
    /// - Everything else (containers, props) prefers the authored name; failing that speaks the object noun
    ///   pulled from the <c>GameObject.name</c> (the last word of "Harbor Crate 22" is "crate"; the slug
    ///   clutter "box_3 rooftop" carries its noun before the underscore, "box"), and as a last resort a
    ///   spoiler-filtered conversation title for the location-slug form ("Ice_eternite").
    ///
    /// <paramref name="authoredName"/> is whatever authored display name the proxy resolved for this thing
    /// from the game (the conversant actor for a character or prop, the destination area for an exit); this
    /// engine-free half decides how it combines with the <c>GameObject.name</c> fallbacks.
    /// </summary>
    public static class EntityNaming
    {
        public static string Resolve(string? rawName, string? authoredName, string? conversationTitle, bool isNamedCharacter, string category)
        {
            string name = Normalize(rawName);
            string? authored = CleanAuthored(authoredName);

            // A named character: the game's authored actor name reads cleanest (it drops the "Yard Cuno"
            // location prefix to "Cuno"); else a clean GameObject.name; else the generic word, never a title.
            if (isNamedCharacter)
                return authored ?? (name.Length > 0 && !IsSlug(name) ? name : WorldThingPerson);

            // A plain door: its own clean name, else the category word. No authored name (a door leads nowhere
            // the proxy resolves).
            if (category == WorldTaxonomy.Door)
                return name.Length > 0 && !IsSlug(name) ? name : TypeWord(category);

            // An exit: the destination it leads to when the proxy resolved one, plus the portal type read from
            // the GameObject.name ("courtyard-door-..." to "door", "...stairs..." to "stairs"), so the player
            // hears "Whirling in Rags door" or "floor 2 stairs". With no resolved destination but a door named
            // for a specific outdoor spot ("Balcony", "Roof" - see SpotFromDoorName, set up by the proxy for
            // exterior doors), that spot leads, defaulting to "door" ("balcony door"). Failing both, a clean
            // bespoke name, else the plain type word.
            if (category == WorldTaxonomy.Exit)
            {
                string? typeKw = ExitTypeKeyword(name);
                if (authored != null) return authored + " " + (typeKw ?? WorldThingExit);
                string? spot = SpotFromDoorName(name);
                if (spot != null) return spot + " " + (typeKw ?? WorldThingDoor);
                return name.Length > 0 && !IsSlug(name) ? name : (typeKw ?? WorldThingExit);
            }

            // Containers and props: the game's authored object name when it has one ("Pile of Eternite"),
            // else the object noun from the name. For the slug clutter whose leading token is a location
            // ("Ice_eternite"), a spoiler-safe title reads better than the noun extractor's guess, so try it
            // before extracting; the title-less clutter ("box_3 rooftop") just extracts its noun.
            if (authored != null) return authored;

            if (name.Length > 0 && FirstToken(name).IndexOf('_') >= 0)
            {
                string? slugTitle = SpoilerSafeTitle(conversationTitle);
                if (slugTitle != null) return slugTitle;
            }
            if (name.Length > 0) return ExtractNoun(name);

            string? title = SpoilerSafeTitle(conversationTitle);
            return title ?? TypeWord(category);
        }

        // The game's authored display name (a conversant actor, or an exit's destination area), accepted when
        // it is a plain short name and rejected when unusable: empty, a machine id (an underscore), the player
        // ("You"/"Player"), or a mechanical/conditional token. Hyphens are display punctuation in a curated
        // name ("Whirling-in-Rags"), spoken as a space, so they are converted, not treated as the slug marker
        // they are in a raw GameObject.name. A "Name, the Title" actor name keeps just the name before the
        // comma ("Garte, the Cafeteria Manager" to "Garte"). Conservative because the noun extractor is always
        // a safe fallback, so over-rejecting only loses a nicer name, never speaks a worse one.
        private static string? CleanAuthored(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw!.IndexOf('_') >= 0) return null;              // a machine id (actor_5), not a display name
            string s = Regex.Replace(raw.Replace('-', ' ').Trim(), @"\s+", " ");
            int comma = s.IndexOf(',');
            if (comma >= 0) s = s.Substring(0, comma).Trim();     // "Garte, the Cafeteria Manager" -> "Garte"
            if (s.Length == 0) return null;
            if (string.Equals(s, "You", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "Player", StringComparison.OrdinalIgnoreCase)) return null;
            string[] words = Regex.Split(s, @"\s+");
            if (words.Length > 5) return null;                    // a description, not a name (allow "Whirling in Rags")
            foreach (string w in words)
                foreach (string meta in MetaTokens)
                    if (string.Equals(w, meta, StringComparison.OrdinalIgnoreCase))
                        return null;
            return s;
        }

        // Light cleanup: drop Unity's "(Clone)" and a trailing duplicate suffix (" (2)", " 2", "_3"), then
        // collapse whitespace. Separators are kept (their presence marks a slug, handled below).
        private static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw!.Replace("(Clone)", "").Trim();
            s = Regex.Replace(s, @"\s*\(\d+\)$", "").Trim(); // " (2)" duplicate suffix
            s = Regex.Replace(s, @"[ _]\d+$", "").Trim();    // " 2" / "_3" duplicate suffix
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        // A name is a slug (machine-generated, not for display) when it carries a separator the clean names
        // never use: an underscore, or a hyphen joining lowercase word characters ("crypto-garys-apt"). The
        // hyphen test requires lowercase/digit on both sides so a proper hyphenated display name, which keeps
        // its capitals ("Jean-Vicquemare"), is not mistaken for a slug and read as the generic word.
        private static bool IsSlug(string name)
            => name.IndexOf('_') >= 0 || Regex.IsMatch(name, @"[a-z0-9]-[a-z0-9]");

        private static string FirstToken(string name)
        {
            int sp = name.IndexOf(' ');
            return sp < 0 ? name : name.Substring(0, sp);
        }

        // The object noun spoken for a container/prop, lowercased like the common noun it is. The slug
        // clutter leads with an "object_index" token ("box_3 rooftop", "crate_landsend") - the noun is the
        // part before the underscore. Otherwise the noun is the last alphabetic word, which fits both the
        // clean "<Location> <Object>" name ("Harbor Crate 22" to "crate") and the "<adjective> <noun>" form
        // ("empty bottle" to "bottle").
        private static string ExtractNoun(string name)
        {
            string first = FirstToken(name);
            int us = first.IndexOf('_');
            if (us > 0) return first.Substring(0, us).ToLowerInvariant();

            string? last = null;
            foreach (string w in Regex.Split(name, @"[\s\-]+"))
                if (Regex.IsMatch(w, @"^[A-Za-z]{2,}$")) last = w;
            return (last ?? name).ToLowerInvariant();
        }

        // Meta/mechanical tokens that mark a conversation title as unsafe to speak (they describe a hidden
        // check or branch a sighted player cannot see). Matched case-insensitively as whole words.
        private static readonly string[] MetaTokens =
            { "PERC", "CHECK", "VISCAL", "COMP", "IF", "EARLIER", "LATER", "CLICKED", "THREAD" };

        // The spoiler filter for the examine-conversation title: strip the ZAUM "<area> / " (and "ORB ")
        // scaffolding, then reject the remainder outright if it looks mechanical or conditional - a meta
        // token, a difficulty number, multiple clauses, or itself a slug. What survives is a short, plain
        // object title, recased from display caps. Conservative by design: the noun extractor and the
        // generic word are always safe, so over-rejecting is the correct failure.
        private static string? SpoilerSafeTitle(string? conversationTitle)
        {
            if (string.IsNullOrWhiteSpace(conversationTitle)) return null;
            string title = conversationTitle!;

            // The leading tag is the area (and any sub-area) name plus " / " - "ICE / ETERNITE",
            // "YARD / PILE OF ETERNITE" - so everything up to the last slash is location scaffolding; keep
            // only the thing after it. The standalone "ORB " prefix has no slash, so strip it first.
            title = Regex.Replace(title.Trim(), @"^\s*ORB\b\s*", "", RegexOptions.IgnoreCase);
            title = Regex.Replace(title, @"^.*/\s*", "").Trim();
            if (title.Length == 0) return null;

            if (IsSlug(title)) return null;                    // an internal id, not a title
            if (Regex.IsMatch(title, @"\d")) return null;      // a difficulty number leaks
            if (title.IndexOf(',') >= 0) return null;          // multiple clauses
            string[] words = Regex.Split(title, @"\s+");
            if (words.Length > 3) return null;                 // a conditional description, not a name
            foreach (string w in words)
                foreach (string meta in MetaTokens)
                    if (string.Equals(w, meta, StringComparison.OrdinalIgnoreCase))
                        return null;

            return TitleCase(title);
        }

        // The titles are display-styled ALL CAPS ("STONE", "FOOTPRINTS"); recase to natural words so the
        // reader does not spell them out.
        private static string TitleCase(string s)
            => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

        // What an exit's destination is called, given the destination's localized area name, the current
        // area's localized name, and the destination's scene id. Prefer the destination name when it is
        // distinct from where you are - a different building ("Martinaise", "Whirling-in-Rags") or a floor the
        // game gives its own name (Doomed Commercial's ground floor is "Bookstore"). When the destination
        // shares the current name (all Whirling floors are "Whirling-in-Rags"), that name says nothing new, so
        // fall back to the floor/level word from the scene id suffix ("Whirling-int-f2" to "floor 2",
        // "Doomed-commerce-int-s1" to "basement"). Null when neither yields anything, so the caller uses the
        // exit's own clean name or the plain type word.
        public static string? ExitDestinationLabel(string? destAreaId, string? destLocalizedName, string? currentLocalizedName)
        {
            if (!string.IsNullOrEmpty(destLocalizedName)
                && !string.Equals(destLocalizedName, currentLocalizedName, StringComparison.OrdinalIgnoreCase))
                return destLocalizedName;
            return LevelLabel(destAreaId);
        }

        // The floor/level word from a scene id's suffix: "-f<n>" is a numbered floor ("floor 2"), "-s<n>" a
        // basement/sublevel ("basement"). Null when the id carries no level suffix.
        private static string? LevelLabel(string? areaId)
        {
            if (string.IsNullOrEmpty(areaId)) return null;
            Match f = Regex.Match(areaId!, @"-f(\d+)", RegexOptions.IgnoreCase);
            if (f.Success) return WorldFloor + " " + int.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Regex.IsMatch(areaId!, @"-s\d+", RegexOptions.IgnoreCase)) return WorldBasement;
            return null;
        }

        // The portal type read from an exit's GameObject.name slug, as an authored (localizable) word, or null
        // when the name carries no known portal word (a tent flap is just "tent"; a spot door is "Balcony").
        // "stair" covers "stairs"/"stairwell"; the match is on the language-invariant English slug.
        private static string? ExitTypeKeyword(string name)
        {
            string lo = name.ToLowerInvariant();
            if (lo.Contains("stair")) return WorldThingStairs;
            if (lo.Contains("elevator") || lo.Contains("lift")) return WorldThingElevator;
            if (lo.Contains("door")) return WorldThingDoor;
            if (lo.Contains("gate")) return WorldThingGate;
            return null;
        }

        // A specific outdoor spot named by an exit's own GameObject name ("Balcony", "Roof"), lowercased for
        // speech. Null when the name is a slug id or is itself a portal-type word (a door/stairs/etc., a type
        // not a place). The proxy uses this only for doors leading to the main exterior, where the coarse area
        // name ("Martinaise") would otherwise hide that the door opens onto a particular balcony or roof.
        public static string? SpotFromDoorName(string? rawName)
        {
            string n = Normalize(rawName);
            if (n.Length == 0 || IsSlug(n)) return null;
            if (ExitTypeKeyword(n) != null) return null;
            return n.ToLowerInvariant();
        }

        private static string TypeWord(string category)
        {
            switch (category)
            {
                case WorldTaxonomy.Door: return WorldThingDoor;
                case WorldTaxonomy.Exit: return WorldThingExit;
                case WorldTaxonomy.Container: return WorldThingContainer;
                case WorldTaxonomy.Npc: return WorldThingPerson;
                default: return WorldThingObject;
            }
        }
    }
}
