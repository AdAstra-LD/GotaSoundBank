using GotaSoundIO.IO;
using GotaSoundIO.Sound;
using GotaSoundIO.Sound.Encoding;
using System;
using System.Collections.Generic;
using System.IO;

namespace GotaSoundBank.SF2 {
    /// <summary>
    /// Base class for SF2 resamplers
    /// </summary>
    public abstract class SF2ResamplerBase {
        protected readonly uint targetSampleRate;
        protected readonly int targetBitDepth;

        public static int Clamp(int value, int min, int max) {
            return Math.Min(Math.Max(value, min), max);
        }

        /// <summary>
        /// Creates a new resampler instance
        /// </summary>
        /// <param name="targetSampleRate">Target sample rate in Hz</param>
        /// <param name="targetBitDepth">Target bit depth (1-16). Set to 16 for no requantization</param>
        protected SF2ResamplerBase(uint targetSampleRate = 48000, int targetBitDepth = 16) {
            this.targetSampleRate = targetSampleRate;
            this.targetBitDepth = Clamp(targetBitDepth, 1, 16); // PCM16 max
        }

        /// <summary>
        /// Resamples all samples in the SoundFont to the target sample rate if their current rate is lower
        /// </summary>
        public void Resample(SoundFont soundFont) {
            foreach (var sample in soundFont.Samples) {
                if (this.targetBitDepth < 16 || sample.Wave.SampleRate < targetSampleRate) {
                    ResampleWave(sample);
                }
            }
        }

        /// <summary>
        /// Requantizes a 16-bit sample to a lower bit depth and back
        /// </summary>
        protected short RequantizeSample(short sample) {
            // Calculate the number of steps for the target bit depth
            int steps = (1 << targetBitDepth);

            // Convert the 16-bit sample to the range 0-1
            double normalizedValue = (sample - short.MinValue) / (double)(ushort.MaxValue);

            // Quantize to the target bit depth
            int quantized = (int)Math.Round(normalizedValue * steps);

            // Convert back to 16-bit range
            return (short)(((quantized / (double)steps) * ushort.MaxValue) + short.MinValue);
        }

        /// <summary>
        /// Process the resampling using a specific interpolation method
        /// </summary>
        protected abstract void ProcessResampling(byte[] originalData, byte[] resampledData, double ratio);

        /// <summary>
        /// Resamples a single sample to the target sample rate
        /// </summary>
        protected void ResampleWave(SampleItem sample) {
            var originalWave = sample.Wave;
            double ratio = (double)targetSampleRate / originalWave.SampleRate;

            // Calculate new number of samples
            int newLength = (int)(originalWave.Audio.NumSamples * ratio);

            // Create memory stream to hold original data
            using (MemoryStream ms = new MemoryStream()) {
                // Get original data
                using (BinaryWriter writer = new BinaryWriter(ms)) {
                    FileWriter fw = new FileWriter(writer.BaseStream);
                    originalWave.Audio.Write(fw);

                    ms.Position = 0;
                    byte[] originalData = new byte[ms.Length];
                    ms.Read(originalData, 0, (int)ms.Length);

                    // Create and fill resampled data array using the specific interpolation method
                    byte[] resampledData = new byte[newLength * 2];
                    ProcessResampling(originalData, resampledData, ratio);

                    // Apply bit depth reduction if needed
                    if (targetBitDepth < 16) {
                        for (int i = 0; i < newLength; i++) {
                            short sample16 = BitConverter.ToInt16(resampledData, i * 2);
                            short requantized = RequantizeSample(sample16);
                            byte[] requantizedBytes = BitConverter.GetBytes(requantized);
                            resampledData[i * 2] = requantizedBytes[0];
                            resampledData[i * 2 + 1] = requantizedBytes[1];
                        }
                    }

                    // Create new wave with resampled data
                    var newWave = new RiffWave() {
                        Audio = new AudioData() {
                            Channels = new List<List<IAudioEncoding>>() {
                                new List<IAudioEncoding>() { new PCM16() }
                            }
                        },
                        SampleRate = targetSampleRate
                    };

                    // Read the resampled data into the new wave
                    using (MemoryStream resampledMs = new MemoryStream(resampledData)) {
                        FileReader fr = new FileReader(resampledMs);
                        newWave.Audio.Channels[0][0].ReadRaw(fr, (uint)newLength, (uint)(newLength * 2));
                    }

                    // Update loop points if they exist
                    if (originalWave.Loops) {
                        newWave.Loops = true;
                        newWave.LoopStart = (uint)(originalWave.LoopStart * ratio);
                        newWave.LoopEnd = (uint)(originalWave.LoopEnd * ratio);
                    }

                    sample.Wave = newWave;
                }
            }
        }
    }

    /// <summary>
    /// Zero-order hold (sample repetition) resampler
    /// </summary>
    public class SF2ZOHResampler : SF2ResamplerBase {
        public SF2ZOHResampler(uint targetSampleRate = 48000, int targetBitDepth = 16)
            : base(targetSampleRate, targetBitDepth) { }

        protected override void ProcessResampling(byte[] originalData, byte[] resampledData, double ratio) {
            int newLength = resampledData.Length / 2;

            for (int i = 0; i < newLength; i++) {
                int sourceIndex = (int)(i / ratio);
                sourceIndex = Math.Min(sourceIndex, (originalData.Length / 2) - 1);

                resampledData[i * 2] = originalData[sourceIndex * 2];
                resampledData[i * 2 + 1] = originalData[sourceIndex * 2 + 1];
            }
        }
    }

    /// <summary>
    /// Linear interpolation resampler
    /// </summary>
    public class SF2LerpResampler : SF2ResamplerBase {
        public SF2LerpResampler(uint targetSampleRate = 48000, int targetBitDepth = 16)
            : base(targetSampleRate, targetBitDepth) { }

        protected override void ProcessResampling(byte[] originalData, byte[] resampledData, double ratio) {
            int newLength = resampledData.Length / 2;

            for (int i = 0; i < newLength; i++) {
                double exactSourceIndex = i / ratio;
                int sourceIndex1 = (int)exactSourceIndex;
                int sourceIndex2 = Math.Min(sourceIndex1 + 1, (originalData.Length / 2) - 1);
                double fraction = exactSourceIndex - sourceIndex1;

                short sample1 = BitConverter.ToInt16(originalData, sourceIndex1 * 2);
                short sample2 = BitConverter.ToInt16(originalData, sourceIndex2 * 2);

                short interpolatedSample = (short)(sample1 + (sample2 - sample1) * fraction);

                byte[] interpolatedBytes = BitConverter.GetBytes(interpolatedSample);
                resampledData[i * 2] = interpolatedBytes[0];
                resampledData[i * 2 + 1] = interpolatedBytes[1];
            }
        }
    }
}