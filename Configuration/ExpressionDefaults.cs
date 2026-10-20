using System;
using System.Collections.Generic;

namespace AvatarReplacer
{
    /// <summary>
    /// Single source of truth for how the game's (giantess) expressions are
    /// mapped onto a VRM's standard blendshape presets. Every consumer —
    /// the JSON expression-map templates and the runtime expression bridge —
    /// reads from this one table, so the file the player edits always shows
    /// exactly what the mod does.
    ///
    /// A default of null means "no standard preset; left to auto-discovery
    /// or a manual override". Those rows are still listed so the player can
    /// override them freely.
    /// </summary>
    public static class ExpressionDefaults
    {
        public enum Slot { Eye, Mouth }

        public struct Entry
        {
            public string GameExpression;
            public Slot Slot;
            public string DefaultPreset; // BlendShapePreset name, or null
            public string Note;          // short human hint, shown in templates
        }

        // Row order is the order the generated templates use: grouped and
        // commented lines here become commented rows there via the notes.
        private static readonly Entry[] Table =
        {
            // ---- Eyes -------------------------------------------------------
            E("happy eyes",        Slot.Eye, "Neutral",  "faces happy"),
            E("serious eyes",      Slot.Eye, "Neutral",  ""),
            E("neutral tilt eyes", Slot.Eye, "Neutral",  ""),
            E("sad eyes",          Slot.Eye, "Sorrow",   ""),
            E("cry eyes",          Slot.Eye, "Sorrow",   ""),
            E("frown eyes",        Slot.Eye, "Sorrow",   ""),
            E("hurt eyes",         Slot.Eye, "Sorrow",   "grow behaviour forces this"),
            E("tears sad",         Slot.Eye, "Sorrow",   "special"),
            E("angry eyes",        Slot.Eye, "Angry",    ""),
            E("mad eyes",          Slot.Eye, "Angry",    ""),
            E("anger small",       Slot.Eye, "Angry",    "special"),
            E("anger large",       Slot.Eye, "Angry",    "special"),
            E("smug eyes",         Slot.Eye, "Fun",      ""),
            E("shock eyes",        Slot.Eye, "Fun",      ""),
            E("screaming eyes",    Slot.Eye, "Fun",      ""),
            E("Eyes Screaming",    Slot.Eye, "Fun",      "dialogue variant"),
            E("Eyes Screaming Alt 2", Slot.Eye, "Fun",   "dialogue variant"),
            E("tears laugh",       Slot.Eye, "Joy",      "special"),
            E("shy eyes",          Slot.Eye, "Neutral",  ""),
            E("embarrased eyes",   Slot.Eye, "Neutral",  "game's own spelling"),
            E("vore eyes",         Slot.Eye, "Neutral",  ""),
            E("laugh eyes",        Slot.Eye, "Neutral",  ""),
            E("tilt",              Slot.Eye, "Neutral",  "special"),

            // Dialogue-only eye variants keep their historical defaults so
            // runtime behaviour is unchanged; now they are visible/editable.
            E("Eyes Bored",        Slot.Eye, "Neutral", "dialogue variant"),
            E("Eyes Disgust",      Slot.Eye, "Angry",   "dialogue variant"),
            E("Eyes Empty",        Slot.Eye, "Neutral", "dialogue variant"),
            E("Eyes Sad 3",        Slot.Eye, "Sorrow",  "dialogue variant"),
            E("Eyes Sleepy",       Slot.Eye, "Neutral", "dialogue variant"),

            // ---- Mouths -----------------------------------------------------
            E("happy mouth",       Slot.Mouth, "Joy",    ""),
            E("Mouth Happy Alt",   Slot.Mouth, "Joy",    "dialogue variant"),
            E("laugh mouth",       Slot.Mouth, "Joy",    ""),
            E("serious mouth",     Slot.Mouth, "Neutral",""),
            E("neutral tilt mouth",Slot.Mouth, "Neutral",""),
            E("smile mouth",       Slot.Mouth, "Neutral",""),
            E("Mouth Empty",       Slot.Mouth, "Neutral","dialogue variant"),
            E("sad mouth",         Slot.Mouth, "Sorrow", ""),
            E("cry mouth",         Slot.Mouth, "Sorrow", ""),
            E("frown mouth",       Slot.Mouth, "Sorrow", ""),
            E("hurt mouth",        Slot.Mouth, "Sorrow", ""),
            E("Mouth Sad 3",       Slot.Mouth, "Sorrow", "dialogue variant"),
            E("angry mouth",       Slot.Mouth, "Angry",  ""),
            E("mad mouth",         Slot.Mouth, "Angry",  ""),
            E("Mouth Disgust",     Slot.Mouth, "Angry",  "dialogue variant"),
            E("smug mouth",        Slot.Mouth, "Fun",    ""),
            E("Mouth Smug",        Slot.Mouth, "Fun",    "dialogue variant"),
            E("screaming mouth",   Slot.Mouth, "O",      ""),
            E("Mouth Screaming",   Slot.Mouth, "O",      "dialogue variant"),
            E("shy mouth",         Slot.Mouth, "Neutral",""),
            E("embarrased mouth",  Slot.Mouth, "Neutral","game's own spelling"),

            // Lip-sync (vowels). Kept here so the whole behaviour is visible.
            E("a mouth",           Slot.Mouth, "A",      "lip sync"),
            E("i mouth",           Slot.Mouth, "I",      "lip sync"),
            E("u mouth",           Slot.Mouth, "U",      "lip sync"),
            E("e mouth",           Slot.Mouth, "E",      "lip sync"),
            E("o mouth",           Slot.Mouth, "O",      "lip sync"),
            E("vore mouth",        Slot.Mouth, "A",      ""),

            // ---- Special: blink ---------------------------------------------
            // The blink pipeline is separate; a filled row here reroutes it.
            E("Blink",             Slot.Mouth, null,     "special: reroutes blink pipeline"),
        };

        private static Entry E(string game, Slot slot, string preset, string note) =>
            new Entry { GameExpression = game, Slot = slot, DefaultPreset = preset, Note = note };

        public static IReadOnlyList<Entry> All => Table;

        public static int Count => Table.Length;
    }
}
