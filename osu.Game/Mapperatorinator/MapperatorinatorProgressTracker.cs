// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.RegularExpressions;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Turns inference.py's own output into an honest progress bar. The tool works in
    /// stages (load model, precompute the audio, timing pass, precompute again, mapping
    /// pass, save) and prints a tqdm bar for the passes, with its own remaining-time
    /// estimate per pass. That beats any guess we can make from the audio length: the
    /// mapping pass alone ranges from 3 to 5+ seconds per window depending on how dense
    /// the map comes out.
    /// </summary>
    public class MapperatorinatorProgressTracker
    {
        private readonly object sync = new object();

        private string stage = @"starting up";
        private bool stageHasBar;
        private float stageStart;
        private float stageEnd = 0.02f;
        private float stageFraction;
        private string? counter;
        private string? remaining;
        private bool sawTimingPass;
        private bool done;

        // "  4%|▍         | 9/207 [00:15<05:33,  1.68s/it, 20.1 tok/s]"
        // "model.safetensors:  45%|████      | 1.12G/2.50G [00:30<00:37, 37.0MB/s]"
        private static readonly Regex bar_regex = new Regex(
            @"(?<percent>\d{1,3})%\|[^|]*\|\s*(?<current>[\d.]+[kMG]?)/(?<total>[\d.]+[kMG]?)\s*\[(?<elapsed>[\d:]+)<(?<remaining>[\d:?]+)",
            RegexOptions.Compiled);

        /// <summary>Feed every line the tool prints. Safe to call from the process reader thread.</summary>
        public void Feed(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (sync)
            {
                if (done)
                    return;

                if (line.Contains(@"Generated .osz saved to", StringComparison.Ordinal) || line.Contains(@"Generated beatmap saved to", StringComparison.Ordinal))
                {
                    done = true;
                    return;
                }

                var bar = bar_regex.Match(line);

                if (bar.Success)
                {
                    if (line.Contains(@"B/s", StringComparison.Ordinal))
                    {
                        // huggingface bajando el modelo: solo la primera vez, pero son gigas.
                        if (!stageHasBar || stage != @"downloading the model")
                            setStage(@"downloading the model", stageStart, Math.Max(stageEnd, 0.05f), hasBar: true);
                    }
                    else if (!stageHasBar)
                    {
                        // una barra sin encabezado conocido: se cuelga de la etapa actual.
                        stageHasBar = true;
                    }

                    stageFraction = Math.Clamp(int.Parse(bar.Groups[@"percent"].Value) / 100f, 0, 1);
                    string current = bar.Groups[@"current"].Value, total = bar.Groups[@"total"].Value;
                    counter = $"{current}/{total}";
                    string left = bar.Groups[@"remaining"].Value;
                    remaining = left == @"?" ? null : left;
                    return;
                }

                if (line.StartsWith(@"Model loaded", StringComparison.Ordinal) || line.StartsWith(@"Loading model", StringComparison.Ordinal))
                    setStage(@"loading the model", 0f, 0.02f, hasBar: false);
                else if (line.StartsWith(@"Precomputing encoder outputs", StringComparison.Ordinal))
                {
                    // sin barra: en cpu son ~90 s por pasada, en gpu un par de segundos.
                    float from = Math.Max(currentOverall(), stageEnd);
                    string windows = Regex.Match(line, @"for (\d+) windows").Groups[1].Value;
                    setStage(windows.Length > 0 ? $"reading the audio ({windows} windows)" : @"reading the audio", from, from + 0.03f, hasBar: false);
                }
                else if (line.StartsWith(@"Generating timing", StringComparison.Ordinal))
                {
                    sawTimingPass = true;
                    setStage(@"timing", 0.05f, 0.15f, hasBar: true);
                }
                else if (line.StartsWith(@"Generating map", StringComparison.Ordinal))
                    setStage(@"mapping", sawTimingPass ? 0.20f : 0.10f, 0.97f, hasBar: true);
                else if (line.StartsWith(@"Generating ", StringComparison.Ordinal) || line.StartsWith(@"Refining ", StringComparison.Ordinal))
                {
                    // etapas que no conocemos (otros configs): siguen desde donde estamos.
                    float from = Math.Max(currentOverall(), stageEnd);
                    setStage(line.Trim().ToLowerInvariant(), from, 0.97f, hasBar: true);
                }
            }
        }

        /// <summary>Overall progress (0..0.97) and the detail text for the notification.</summary>
        public (float progress, string detail) Render()
        {
            lock (sync)
            {
                if (done)
                    return (0.97f, @"importing...");

                float progress = currentOverall();

                if (!stageHasBar)
                    return (progress, $"{stage}...");

                string text = counter != null ? $"{stage} {counter}" : stage;

                if (remaining != null)
                    text += $" · ~{remaining} left";
                else if (stageFraction <= 0)
                    text += @" · warming up";

                return (progress, text);
            }
        }

        private float currentOverall() => stageStart + (stageEnd - stageStart) * stageFraction;

        private void setStage(string name, float from, float to, bool hasBar)
        {
            stage = name;
            stageStart = Math.Clamp(from, 0f, 0.97f);
            stageEnd = Math.Clamp(Math.Max(to, stageStart), 0f, 0.97f);
            stageFraction = 0;
            stageHasBar = hasBar;
            counter = null;
            remaining = null;
        }
    }
}
