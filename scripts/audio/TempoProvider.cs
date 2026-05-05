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
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;
using SoundTouch;

#nullable enable

internal class TempoProvider : ISoundDataProvider
{
    private readonly float[] samples;
    private int position = 0;
    private double playbackPosition = 0;

    public int SampleRate { get; }
    public SampleFormat SampleFormat { get; }
    public AudioFormat Format { get; }
    public int Length => samples.Length;

    public bool CanSeek { get; private set; } = true;
    public bool IsDisposed { get; private set; } = false;
    public SoundFormatInfo? FormatInfo { get; private set; } = null;

    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    private readonly SoundTouchProcessor processor;
    private readonly float[] buffer = new float[1024];

    public float Tempo
    {
        get => (float)processor.Rate;
        set
        {
            if (processor.Rate != value)
            {
                processor.Clear();
                processor.Rate = value;
                Seek(Position);
            }
        }
    }

    public int Position => (int)playbackPosition;

    public TempoProvider(float[] samples, AudioFormat format)
    {
        this.samples = samples;
        SampleRate = format.SampleRate;
        SampleFormat = format.Format;
        Format = format;

        processor = new()
        {
            SampleRate = format.SampleRate,
            Channels = format.Channels
        };
    }

    private void fillSTSamples(int samplesNeeded)
    {
        int channels = Format.Channels;

        while (processor.AvailableSamples < samplesNeeded)
        {
            try
            {
                int samplesLeft = samples.Length - position;

                if (samplesLeft <= 0)
                {
                    processor.Flush();
                    break;
                }

                int toFill = Math.Min(samplesLeft, buffer.Length);
                Array.Copy(samples, position, buffer, 0, toFill);

                processor.PutSamples(buffer, toFill / channels);
                position += toFill;
            }
            catch { }
        }
    }

    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed)
            return 0;

        int samplesLeft = samples.Length - position;

        if (samplesLeft <= 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        int toServe = Math.Min(samplesLeft, buffer.Length);
        int channels = Format.Channels;

        fillSTSamples(toServe);
        processor.ReceiveSamples(buffer, toServe / channels);

        playbackPosition += toServe * Tempo;
        PositionChanged?.Invoke(this, new(Position));
        return toServe;
    }

    /// <summary>
    /// Equivalent to <see cref="ReadBytes(Span{float})"/> without tempo scaling
    /// </summary>
    /// <param name="buffer">The buffer to write the bytes to</param>
    /// <returns>The number of bytes read</returns>
    public int ReadBytesRaw(Span<float> buffer)
    {
        if (IsDisposed)
            return 0;

        int samplesLeft = samples.Length - position;

        if (samplesLeft <= 0)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        int toServe = Math.Min(samplesLeft, buffer.Length);
        samples.AsSpan().Slice(position, toServe).CopyTo(buffer);

        position += toServe;
        playbackPosition = position;
        PositionChanged?.Invoke(this, new(Position));
        return toServe;
    }

    public void Seek(int sampleOffset)
    {
        processor.Clear();
        position = Math.Clamp(sampleOffset, 0, samples.Length);
        playbackPosition = position;
    }

    /// <summary>
    /// Attempts to detect the BPM of a given range of samples, returning 0 if detection failed
    /// </summary>
    /// <param name="startSamples">The start position of the range of samples to scan</param>
    /// <param name="endSamples">The end position of the range of samples to scan</param>
    /// <returns>The detected BPM, or 0 if detection failed</returns>
    public float GetBPM(int startSamples, int endSamples)
    {
        int channels = Format.Channels;
        startSamples = startSamples / channels * channels;
        endSamples = endSamples / channels * channels;

        float tempo = Tempo;
        float bpm = 0;

        for (int i = 0; i < 5; i++)
        {
            Tempo = 1 / (float)Math.Pow(2, i);

            Seek(startSamples);
            BpmDetect detector = new(channels, Format.SampleRate);

            float[] buffer = new float[4096];

            while (playbackPosition < endSamples)
            {
                int samplesRead = ReadBytes(buffer);
                detector.InputSamples(buffer, samplesRead / channels);

                if (samplesRead == 0)
                    break;
            }

            bpm = detector.GetBpm();
            if (bpm > 0)
                break;

            Logger.Log($"BPM detect pass {i} failed");
        }

        Tempo = tempo;
        return bpm;
    }

    public void Dispose()
    {
        if (!IsDisposed)
            IsDisposed = true;
        EndOfStreamReached = null;
        PositionChanged = null;
    }
}
