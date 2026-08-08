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
}
