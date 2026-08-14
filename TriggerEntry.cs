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

    /// <summary>Per-event whistle playback speed multiplier -- REPLACED AltWhistlePath 2026-08-12
    /// (owner request: "remove the alternate whistle and just make that button a whistle speed
    /// toggle"). Same global lead-in whistle clip (ConfigStore.LeadInWhistlePath /
    /// AudioPlayer.LeadInClipPath) always plays; this only changes how fast/slow it plays for this
    /// one card via AudioPlayer's SoundTouchSpeedSampleProvider (same tempo-shift technique
    /// PlaybackSpeed2x already uses for the main clip). 1.0 = normal speed (default, same behavior
    /// as before this field existed). Only takes effect when PlayLeadInWhistle is also true.
    /// Missing from old saved JSON deserializes to 1.0, no migration needed. Cycled through a fixed
    /// preset list (see WHISTLE_SPEED_PRESETS in app.js) by clicking the whistle-speed button rather
    /// than free-typed, so it can't drift to an unreasonable value by accident.</summary>
    public double WhistleSpeed { get; set; } = 1.0;

    /// <summary>Speed toggle for this card's clip -- see AudioPlayer.Play's speed2x param and
    /// SoundTouchSpeedSampleProvider for how it's applied. Affects both real in-game firing and Preview.
    /// Missing from old saved JSON deserializes to false (default), so no migration needed.</summary>
    public bool PlaybackSpeed2x { get; set; } = false;

    /// <summary>Per-event override that forces this card's main clip through the Stadium reverb
    /// preset (the "coming out of the stadium PA speakers" sound), regardless of whatever the
    /// Sound Booth's global Reverb preset is currently set to. Added 2026-08-12 (owner request:
    /// wanted the PA-speaker effect on exactly one event, not applied globally to every clip via
    /// the Sound Booth). Deliberately Stadium reverb only, NOT the Megaphone EQ preset -- owner
    /// clarified this should sound like open-air stadium speakers, not a narrow-band radio/
    /// megaphone honk. Same on/off-per-card convention as PlaybackSpeed2x above. Missing from old
    /// saved JSON deserializes to false (default), no migration needed.</summary>
    public bool PaSpeakerEffect { get; set; } = false;

    /// <summary>Per-event override for AudioPlayer.FadeStartSeconds/FadeOutDuration -- null (the
    /// default) means this card just follows the Sound Booth's global Audio Timing settings,
    /// same as before these fields existed. Set means this one card fades on its own schedule
    /// regardless of the global value, for events that need to cut earlier/later or ramp out
    /// faster/slower than the rest of the profile (e.g. a short stinger vs. a long fight song).
    /// Missing from old saved JSON deserializes to null, no migration needed. Surfaced together
    /// with WhistleSpeed/PlaybackSpeed2x/PaSpeakerEffect in the event card's settings pill.</summary>
    public double? FadeStartSecondsOverride { get; set; } = null;
    public double? FadeOutDurationOverride { get; set; } = null;

    /// <summary>Per-event kill switch for fading entirely -- when true, this card's clip always
    /// plays straight through (or to a hard cutoff/natural end) with no volume ramp-down,
    /// regardless of the global Audio Timing fade settings or FadeStartSecondsOverride/
    /// FadeOutDurationOverride above. Added 2026-08-14 (owner request: some songs shouldn't ever
    /// fade out). Missing from old saved JSON deserializes to false (default), no migration
    /// needed -- every existing assignment keeps fading exactly as before.</summary>
    public bool NoFade { get; set; } = false;
}
