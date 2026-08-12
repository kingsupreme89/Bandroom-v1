namespace SupremeStadiumSoundSelector;

public class TriggerEntry
{
    public string Trigger { get; set; } = "";
    public string Event { get; set; } = "";
    public string AudioFile { get; set; } = "";

    /// <summary>Optional second clip for this same situation, played on its own independent
    /// volume channel (AudioPlayer.PaVolume) alongside AudioFile -- e.g. a PA announcer call
    /// layered under/over the main hype cue. Empty = no PA clip assigned, same convention as
    /// AudioFile. Missing from old saved JSON deserializes to "" automatically (default), so no
    /// migration step is needed for existing profiles.</summary>
    public string PaAudioFile { get; set; } = "";

    /// <summary>Per-event volume, 0-100 -- lets one card (e.g. a quiet PA chime vs. a loud
    /// touchdown horn) be balanced independently without touching Master/Home/Away. Applied as
    /// a multiplier on top of whichever base volume FireEvent/PreviewEventFromWeb would already
    /// use. Missing from old saved JSON deserializes to the default (100 = unchanged), so no
    /// migration step is needed for existing profiles.</summary>
    public int Volume { get; set; } = 100;

    /// <summary>Optional CONDITIONAL alternate clip for this same card, used INSTEAD of
    /// AudioFile (not layered alongside it, unlike PaAudioFile above) whenever the game is
    /// currently flagged as a Big Game (GameWatcher.IsBigGame / ConfigStore.BigGameSettings).
    /// Added 2026-08-10 for the owner's "gameplan" redesign -- e.g. a home defense card can play
    /// a quieter/different clip normally and a bigger one specifically when both bands are
    /// physically present. Empty = no Big Game variant assigned, falls back to AudioFile same as
    /// every other card. Missing from old saved JSON deserializes to "" automatically, no
    /// migration step needed.</summary>
    public string BigGameAudioFile { get; set; } = "";

    /// <summary>Per-song override for the global lead-in whistle (AudioPlayer's
    /// LeadInWhistleEnabled / #toggle-leadin-whistle) -- true (default) means this card's clip
    /// still gets the whistle when the global toggle is on, false always skips it for this card
    /// regardless of the global toggle. Missing from old saved JSON deserializes to true (default),
    /// so every existing assignment keeps behaving exactly as it did before this field existed.</summary>
    public bool PlayLeadInWhistle { get; set; } = true;

    /// <summary>Per-event alternate to the global lead-in whistle clip (ConfigStore.LeadInWhistlePath
    /// / AudioPlayer.LeadInClipPath) -- empty means "use the global one" (default, same behavior as
    /// before this field existed). Only takes effect when PlayLeadInWhistle is also true; this
    /// doesn't add a second on/off toggle, it swaps WHICH clip plays for just this card.</summary>
    public string AltWhistlePath { get; set; } = "";

    /// <summary>Speed toggle for this card's clip -- see AudioPlayer.Play's speed2x param and
    /// SoundTouchSpeedSampleProvider for how it's applied. Affects both real in-game firing and Preview.
    /// Missing from old saved JSON deserializes to false (default), so no migration needed.</summary>
    public bool PlaybackSpeed2x { get; set; } = false;
}
