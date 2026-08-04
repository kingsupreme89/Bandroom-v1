using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace SupremeStadiumSoundSelector;

/// <summary>
/// Controls audio ducking: reduces background music volume when event cues play.
/// Uses state machine to track duck level and smoothly fade between states.
/// </summary>
public sealed class AudioDuckingController
{
    public enum DuckState { Normal, Ducking, Fading }

    public DuckState CurrentState { get; private set; } = DuckState.Normal;
    public float CurrentDuckAmount { get; private set; } = 0f;  // 0-1, where 1 = fully ducked

    private float _targetDuckAmount = 0f;
    private float _fadeDuration = 1f;
    private float _fadeElapsed = 0f;
    private List<IWavePlayer> _backgroundPlayers = new();

    public event Action<DuckState>? StateChanged;
    public event Action<string>? Log;

    public AudioDuckingController() { }

    /// <summary>Event fired (cue playing) - duck the background music.</summary>
    public void OnEventFired(string eventName)
    {
        DuckMusic(targetDuckAmount: 1f, fadeDurationSeconds: 0.2f);
        Log?.Invoke($"[Ducking] Event fired: {eventName}");
    }

    /// <summary>Play ended - resume background music.</summary>
    public void OnPlayEnded()
    {
        DuckMusic(targetDuckAmount: 0f, fadeDurationSeconds: 1f);
        Log?.Invoke("[Ducking] Play ended, resuming music");
    }

    /// <summary>Called every frame (~400ms) to update duck fade.</summary>
    public void UpdateDuckFade(float deltaTimeSeconds)
    {
        if (Math.Abs(_targetDuckAmount - CurrentDuckAmount) < 0.01f)
        {
            CurrentDuckAmount = _targetDuckAmount;
            if (CurrentState == DuckState.Fading)
            {
                CurrentState = _targetDuckAmount > 0.5f ? DuckState.Ducking : DuckState.Normal;
                StateChanged?.Invoke(CurrentState);
            }
            return;
        }

        // Smoothly interpolate toward target
        float step = (1f / _fadeDuration) * deltaTimeSeconds;
        CurrentDuckAmount = float.Lerp(CurrentDuckAmount, _targetDuckAmount, Math.Min(step, 1f));

        if (CurrentState != DuckState.Fading)
        {
            CurrentState = DuckState.Fading;
            StateChanged?.Invoke(CurrentState);
        }
    }

    /// <summary>Set ducking target and fade duration.</summary>
    private void DuckMusic(float targetDuckAmount, float fadeDurationSeconds)
    {
        _targetDuckAmount = Math.Clamp(targetDuckAmount, 0f, 1f);
        _fadeDuration = fadeDurationSeconds;
        _fadeElapsed = 0f;
    }

    /// <summary>Preset ducking profiles for different use cases.</summary>
    public void SetDuckingPreset(string presetName)
    {
        switch (presetName?.ToLower())
        {
            case "aggressive":  // Fade very quick, deep duck
                DuckMusic(1f, 0.15f);
                break;
            case "subtle":      // Slow fade, light duck
                DuckMusic(0.4f, 2f);
                break;
            case "off":         // No ducking
                DuckMusic(0f, 0f);
                break;
            default:            // Standard
                DuckMusic(1f, 0.3f);
                break;
        }
    }

    /// <summary>Get current duck level as dB reduction (0 to -30dB).</summary>
    public float GetDuckLevelDb() => -30f * CurrentDuckAmount;

    /// <summary>Register background audio player for ducking.</summary>
    public void RegisterBackgroundPlayer(IWavePlayer player)
    {
        if (player != null && !_backgroundPlayers.Contains(player))
            _backgroundPlayers.Add(player);
    }
}
