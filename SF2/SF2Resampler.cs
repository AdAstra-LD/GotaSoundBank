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

        protected SF2ResamplerBase(uint targetSampleRate = 48000) {
            this.targetSampleRate = targetSampleRate;
        }

        /// <summary>
        /// Resamples all samples in the SoundFont to the target sample rate if their current rate is lower
        /// </summary>
        public void Resample(SoundFont soundFont) {
            foreach (var sample in soundFont.Samples) {
                if (sample.Wave.SampleRate < targetSampleRate) {
                    ResampleWave(sample);
                }
            }
        }

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

        /// <summary>
        /// Process the resampling using a specific interpolation method
        /// </summary>
        protected abstract void ProcessResampling(byte[] originalData, byte[] resampledData, double ratio);
    }

    /// <summary>
    /// Zero-order hold (sample repetition) resampler
    /// </summary>
    public class SF2ZOHResampler : SF2ResamplerBase {
        public SF2ZOHResampler(uint targetSampleRate = 48000) : base(targetSampleRate) { }

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
        public SF2LerpResampler(uint targetSampleRate = 48000) : base(targetSampleRate) { }

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