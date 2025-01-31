﻿using GotaSoundIO.IO;
using GotaSoundIO.Sound;
using GotaSoundIO.Sound.Encoding;
using System;
using System.Collections.Generic;
using System.IO;

namespace GotaSoundBank.SF2 {
    public static class SF2LerpResampler {
        /// <summary>
        /// Resamples all samples in the SoundFont to 48000Hz using linear interpolation if their sample rate is lower
        /// </summary>
        /// <param name="soundFont">The SoundFont to process</param>
        public static void ResampleTo48k(SoundFont soundFont) {
            foreach (var sample in soundFont.Samples) {
                if (sample.Wave.SampleRate < 48000) {
                    ResampleWaveLerp(sample);
                }
            }
        }

        /// <summary>
        /// Resamples a single sample to 48000Hz using linear interpolation
        /// </summary>
        /// <param name="sample">The sample to resample</param>
        private static void ResampleWaveLerp(SampleItem sample) {
            var originalWave = sample.Wave;
            double ratio = 48000.0 / originalWave.SampleRate;

            // Calculate new number of samples
            int newLength = (int)(originalWave.Audio.NumSamples * ratio);

            // Create memory stream to hold original data
            using (MemoryStream ms = new MemoryStream()) {
                // Create writer to get the original data
                using (BinaryWriter writer = new BinaryWriter(ms)) {
                    // Write the original samples to our stream
                    FileWriter fw = new FileWriter(writer.BaseStream);
                    originalWave.Audio.Write(fw);

                    // Reset stream position for reading
                    ms.Position = 0;

                    // Read samples as 16-bit PCM
                    byte[] originalData = new byte[ms.Length];
                    ms.Read(originalData, 0, (int)ms.Length);

                    // Create resampled data array
                    byte[] resampledData = new byte[newLength * 2]; // 2 bytes per sample for PCM16

                    // Perform linear interpolation resampling
                    for (int i = 0; i < newLength; i++) {
                        double exactSourceIndex = i / ratio;
                        int sourceIndex1 = (int)exactSourceIndex;
                        int sourceIndex2 = Math.Min(sourceIndex1 + 1, (originalData.Length / 2) - 1);
                        double fraction = exactSourceIndex - sourceIndex1;

                        // Get sample 1
                        short sample1 = BitConverter.ToInt16(originalData, sourceIndex1 * 2);

                        // Get sample 2
                        short sample2 = BitConverter.ToInt16(originalData, sourceIndex2 * 2);

                        // Perform linear interpolation
                        short interpolatedSample = (short)(sample1 + (sample2 - sample1) * fraction);

                        // Convert interpolated sample back to bytes
                        byte[] interpolatedBytes = BitConverter.GetBytes(interpolatedSample);
                        resampledData[i * 2] = interpolatedBytes[0];
                        resampledData[i * 2 + 1] = interpolatedBytes[1];
                    }

                    // Create new wave with resampled data
                    var newWave = new RiffWave() {
                        Audio = new AudioData() {
                            Channels = new List<List<IAudioEncoding>>() {
                                new List<IAudioEncoding>() { new PCM16() }
                            }
                        },
                        SampleRate = 48000
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

                    // Update the sample with new wave
                    sample.Wave = newWave;
                }
            }
        }
    }
}