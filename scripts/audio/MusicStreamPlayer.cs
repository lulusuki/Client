// Sound-Space-Quantum-Editor
// Copyright(C) 2027 Avibah
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

// Derivative from https://github.com/Avibah/Sound-Space-Quantum-Editor

using System;
using System.IO;
using SoundFlow.Enums;
using SoundFlow.Visualization;
using Player = SoundFlow.Components.SoundPlayer;

#nullable enable

// TODO: Summary

/// <summary>
///
/// </summary>
public class MusicStreamPlayer : IDisposable
{
    private Player? soundPlayer;
    private TempoProvider? tempoProvider;

    public bool HasSound => soundPlayer != null;

    // TODO: Add audio offset settings to change this value
    // Should be Offset - 28;
    public float AudioOffset { get; set; } = -28;


    /// <summary>
    /// Whether the currently loaded music is an MP3
    /// </summary>
    public bool IsMP3 { get; private set; }

    /// <summary>
    /// Whether the currently loaded music is an OGG
    /// </summary>
    public bool IsOGG { get; private set; }

    /// <summary>
    /// Gets or sets the active player's tempo / playback speed
    /// </summary>
    public float Tempo
    {
        set => tempoProvider?.Tempo = value;
        get => tempoProvider?.Tempo ?? 1;
    }

    /// <summary>
    /// Gets or sets the active player's volume
    /// </summary>
    public float Volume
    {
        set => soundPlayer?.Volume = Math.Max(value * SoundEngine.VOLUME_MULT, 0);
        get => (soundPlayer?.Volume / SoundEngine.VOLUME_MULT) ?? 1;
    }

    /// <summary>
    /// Whether the active player is currently playing
    /// </summary>
    public bool IsPlaying => soundPlayer?.State == PlaybackState.Playing;

    /// <summary>
    /// Gets the active player's total time as a <see cref="TimeSpan"/>
    /// </summary>
    public TimeSpan TotalTime => TimeSpan.FromSeconds(soundPlayer?.Duration ?? 0);

    /// <summary>
    /// Gets or sets the active player's current timestamp as a <see cref="TimeSpan"/>
    /// </summary>
    public TimeSpan CurrentTime
    {
        set => soundPlayer?.Seek(value - TimeSpan.FromMilliseconds(AudioOffset));
        get => TimeSpan.FromSeconds(soundPlayer == null ? 0 : TotalTime.TotalSeconds * soundPlayer.DataProvider.Position / soundPlayer.DataProvider.Length + AudioOffset / 1000d);
    }

    public event Action? Finished;

    /// <summary>
    /// Loads music from <paramref name="file"/> if possible, disposing previously loaded music and resetting the current time to 0
    /// </summary>
    /// <param name="file">The file to load music from</param>
    /// <returns>Whether file loading succeeded</returns>
    public bool Load(string file)
    {
        var settings = SettingsManager.Instance.Settings;

        if (!File.Exists(file))
            return false;

        if (soundPlayer != null)
            SoundEngine.DisposePlayer(soundPlayer);
        SoundEngine.SetMono(false); // False for now add setting for this in the future

        try
        {
            Player player = SoundEngine.InitializeMusic(file, out string fileType);
            player.PlaybackEnded += (s, e) => onEnded();
            soundPlayer = player;
            tempoProvider = (player.DataProvider as TempoProvider)!;

            IsMP3 = fileType.StartsWith("mp3");
            IsOGG = fileType.StartsWith("ogg");

            //if (tempoProvider != null)
            //    Waveform.Load(tempoProvider);
            //else
            //    Waveform.Reset();

            Volume = (float)(settings.VolumeMusic + settings.VolumeMaster / 2) / 100;
        }
        catch (Exception ex)
        {
            Logger.Error($"Audio failed to load - {file}: {ex.Message}");
            // TODO: add notification popup on failure - should be done by the component using this though
            return false;
        }

        Stop();

        return true;
    }

    /// <summary>
    /// Loads music from <paramref name="file"/> if possible, disposing previously loaded music and resetting the current time to 0
    /// </summary>
    /// <param name="file">The file to load music from</param>
    /// <returns>Whether file loading succeeded</returns>
    public bool Load(byte[] buffer)
    {
        var settings = SettingsManager.Instance.Settings;

        if (soundPlayer != null)
            SoundEngine.DisposePlayer(soundPlayer);
        SoundEngine.SetMono(false); // False for now add setting for this in the future

        try
        {
            Player player = SoundEngine.InitializeMusic(buffer, out string fileType);
            player.PlaybackEnded += (s, e) => onEnded();
            soundPlayer = player;
            tempoProvider = (player.DataProvider as TempoProvider)!;

            IsMP3 = fileType.StartsWith("mp3");
            IsOGG = fileType.StartsWith("ogg");

            //if (tempoProvider != null)
            //    Waveform.Load(tempoProvider);
            //else
            //    Waveform.Reset();

            Volume = (float)(settings.VolumeMusic + settings.VolumeMaster / 2) / 100;
        }
        catch (Exception ex)
        {
            Logger.Error($"Audio failed to load: {ex.Message}");
            // TODO: add notification popup on failure - should be done by the component using this though
            return false;
        }

        Stop();

        return true;
    }

    /// <summary>
    /// Resume playback of the active player
    /// </summary>
    /// <param name="time">Playback time in milliseconds</param>
    public virtual void Play(float? time = null)
    {
        CurrentTime = TimeSpan.FromMilliseconds(time ?? CurrentTime.TotalMilliseconds);
        soundPlayer?.Play();
    }

    public void Seek(float offset, SeekOrigin seekOrigin = SeekOrigin.Begin)
    {
        CurrentTime = TimeSpan.FromMilliseconds(offset);
        soundPlayer!.Seek(CurrentTime, seekOrigin);
    }

    /// <summary>
    /// Pause playback of the active player
    /// </summary>
    public void Pause()
    {
        soundPlayer?.Stop();
    }

    /// <summary>
    /// Stop playback and set the current time to 0
    /// </summary>
    public void Stop()
    {
        soundPlayer?.Stop();
        CurrentTime = TimeSpan.Zero;
    }


    private void onEnded()
    {
        Pause();
        CurrentTime = TotalTime;
        Finished?.Invoke();
        // Settings.currentTime.Value.Value = (float)(CurrentTime.TotalMilliseconds - MusicOffset);
    }

    /// <summary>
    /// Attempts to detect a BPM between two millisecond timestamps, returning 0 if detection failed
    /// </summary>
    /// <param name="start">The start position of the range to detect BPM from, in milliseconds</param>
    /// <param name="end">The end position of the range to detect BPM from, in milliseconds</param>
    /// <returns>The detected BPM, or 0 if detection failed</returns>
    public float DetectBPM(long start, long end)
    {
        if (tempoProvider == null)
            return 0;

        int sampleRate = tempoProvider.Format.SampleRate;
        int startOffset = (int)(start / 1000d * sampleRate);
        int endOffset = (int)(end / 1000d * sampleRate);

        return tempoProvider.GetBPM(startOffset, endOffset);
    }

    public void Dispose()
    {
        if (soundPlayer != null)
            SoundEngine.DisposePlayer(soundPlayer);
    }
}
