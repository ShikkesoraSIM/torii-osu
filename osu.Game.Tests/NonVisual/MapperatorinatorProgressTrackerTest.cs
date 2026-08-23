// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Mapperatorinator;

namespace osu.Game.Tests.NonVisual
{
    /// <summary>
    /// Lines are lifted from a real CPU run (linux, v32 config) so the parser is tested
    /// against what inference.py actually prints, not what we think it prints.
    /// </summary>
    [TestFixture]
    public class MapperatorinatorProgressTrackerTest
    {
        [Test]
        public void TestFullCpuRun()
        {
            var tracker = new MapperatorinatorProgressTracker();
            Assert.That(tracker.Render().detail, Is.EqualTo("starting up..."));

            tracker.Feed("Using CPU for inference (auto-selected fallback).");
            Assert.That(tracker.ReportedDevice, Is.EqualTo("cpu"));
            Assert.That(tracker.UsesCpuDespiteGpu, Is.False);

            tracker.Feed("Model loaded: OliBomby/Mapperatorinator-v32/gamemode=0 on device cpu");
            Assert.That(tracker.Render().detail, Is.EqualTo("CPU · loading the model..."));

            tracker.Feed("Precomputing encoder outputs for 207 windows...");
            var (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · reading the audio (207 windows)..."));
            Assert.That(progress, Is.EqualTo(0.02f).Within(0.001f));

            tracker.Feed("Encoder precompute: 93.10s (449.8 ms/window)");
            tracker.Feed("Generating timing");
            tracker.Feed("  0%|          | 0/207 [00:00<?, ?it/s]");
            (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · timing 0/207 · warming up"));
            Assert.That(progress, Is.EqualTo(0.05f).Within(0.001f));

            tracker.Feed("  4%|▍         | 9/207 [00:15<05:33,  1.68s/it, 20.1 tok/s]");
            (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · timing 9/207 · ~05:33 left"));
            Assert.That(progress, Is.EqualTo(0.054f).Within(0.001f));

            tracker.Feed("100%|██████████| 207/207 [02:07<00:00,  1.62it/s, 20.8 tok/s]");
            Assert.That(tracker.Render().progress, Is.EqualTo(0.15f).Within(0.001f));

            // second precompute carries on from where timing left off, never backwards.
            tracker.Feed("Precomputing encoder outputs for 207 windows...");
            (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · reading the audio (207 windows)..."));
            Assert.That(progress, Is.EqualTo(0.15f).Within(0.001f));

            tracker.Feed("Encoder precompute: 101.11s (488.5 ms/window)");
            tracker.Feed("Generating map");
            tracker.Feed(" 50%|█████     | 104/207 [09:00<09:05,  5.25s/it, 33.2 tok/s]");
            (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · mapping 104/207 · ~09:05 left"));
            Assert.That(progress, Is.EqualTo(0.20f + 0.77f * 0.5f).Within(0.001f));

            // noise between the bar and the save line must not disturb the state.
            tracker.Feed("Warning: Incomplete slider at 164505");
            Assert.That(tracker.Render().detail, Is.EqualTo("CPU · mapping 104/207 · ~09:05 left"));

            tracker.Feed("Generated .osz saved to /tmp/torii-mapperatorinator/19249f5c/out/beatmap22982283.osz");
            (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("CPU · importing..."));
            Assert.That(progress, Is.EqualTo(0.97f).Within(0.001f));
        }

        [Test]
        public void TestCudaRunWithoutTimingPass()
        {
            var tracker = new MapperatorinatorProgressTracker("cuda");
            tracker.Feed("Model loaded: OliBomby/Mapperatorinator-v32 on device cuda:0");
            Assert.That(tracker.ReportedDevice, Is.EqualTo("cuda"));
            Assert.That(tracker.ActualDevice, Is.EqualTo("cuda"));
            Assert.That(tracker.Render().detail, Is.EqualTo("GPU (CUDA) · loading the model..."));

            tracker.Feed("Precomputing encoder outputs for 60 windows...");
            tracker.Feed("Generating map");
            tracker.Feed("  0%|          | 0/60 [00:00<?, ?it/s]");
            Assert.That(tracker.Render().progress, Is.EqualTo(0.10f).Within(0.001f));
        }

        [Test]
        public void TestRocmShowsUpAsCudaInsideTorch()
        {
            var tracker = new MapperatorinatorProgressTracker("rocm");
            tracker.Feed("Model loaded: OliBomby/Mapperatorinator-v32 on device cuda");
            Assert.That(tracker.ActualDevice, Is.EqualTo("rocm"));
            Assert.That(tracker.Render().detail, Is.EqualTo("GPU (ROCm) · loading the model..."));
        }

        [Test]
        public void TestCpuFallbackWithGpuPresentIsCalledOut()
        {
            var tracker = new MapperatorinatorProgressTracker("rocm");
            tracker.Feed("Using CPU for inference (auto-selected fallback).");
            Assert.That(tracker.UsesCpuDespiteGpu, Is.True);
            Assert.That(tracker.ActualDevice, Is.EqualTo("cpu"));
            tracker.Feed("Generating map");
            tracker.Feed(" 10%|█         | 15/150 [00:48<07:12,  3.20s/it, 27.8 tok/s]");
            Assert.That(tracker.Render().detail, Is.EqualTo("CPU (GPU not used!) · mapping 15/150 · ~07:12 left"));
        }

        [Test]
        public void TestModelDownloadBar()
        {
            var tracker = new MapperatorinatorProgressTracker();
            tracker.Feed("model.safetensors:  45%|████      | 1.12G/2.50G [00:30<00:37, 37.0MB/s]");
            var (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("downloading the model 1.12G/2.50G · ~00:37 left"));
            Assert.That(progress, Is.EqualTo(0.05f * 0.45f).Within(0.001f));
        }

        [Test]
        public void TestUnknownStageFollowsOn()
        {
            var tracker = new MapperatorinatorProgressTracker();
            tracker.Feed("Generating timing");
            tracker.Feed("100%|██████████| 10/10 [00:10<00:00,  1.00it/s]");
            tracker.Feed("Refining positions");
            tracker.Feed(" 10%|█         | 1/10 [00:01<00:09,  1.00it/s]");
            var (progress, detail) = tracker.Render();
            Assert.That(detail, Is.EqualTo("refining positions 1/10 · ~00:09 left"));
            Assert.That(progress, Is.EqualTo(0.15f + (0.97f - 0.15f) * 0.1f).Within(0.001f));
        }
    }
}
