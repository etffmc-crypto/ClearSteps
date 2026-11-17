// ClearSteps Doctor - the built-in troubleshooter. Each routine works through a symptom the way a patient IT person
// would: look, explain in plain words, fix what it can, and say exactly what is left for the human. No internet, no AI
// service, nothing to pay for. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClearSteps
{
    /// Plays a short stereo test sound into a named playback device through the classic Windows wave API.
    public static class TestTone
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveOutGetDevCapsW(IntPtr id, out WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        static int FindDevice(string namePrefix)
        {
            uint n = waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                WAVEOUTCAPS c; if (waveOutGetDevCapsW(new IntPtr(i), out c, Marshal.SizeOf(typeof(WAVEOUTCAPS))) != 0) continue;
                if (c.szPname != null && c.szPname.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// Starts the sound (soft footstep-like thumps, left then right) and calls 'during' while it plays.
        public static void Play(string devicePrefix, double seconds, Action during)
        {
            int id = FindDevice(devicePrefix);
            if (id < 0) throw new InvalidOperationException("Windows does not list a playback device starting with \"" + devicePrefix + "\".");
            const int rate = 48000; int frames = (int)(rate * seconds);
            var pcm = new short[frames * 2]; var rng = new Random(7);
            for (int start = 0, step = 0; start < frames - 6000; start += rate / 3, step++)
                for (int i = 0; i < 6000; i++)
                {
                    double t = i / (double)rate, env = Math.Exp(-t * 45);
                    short v = (short)((Math.Sin(2 * Math.PI * 170 * t) * 0.6 + (rng.NextDouble() - 0.5) * 0.5) * env * 3500);
                    pcm[(start + i) * 2 + (step % 2)] = v;
                }
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = rate * 4 };
            IntPtr h; int rc = waveOutOpen(out h, (uint)id, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (rc != 0) throw new InvalidOperationException("Windows would not open that device for a test sound (code " + rc + ").");
            int bytes = pcm.Length * 2; IntPtr data = Marshal.AllocHGlobal(bytes); IntPtr hdr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
            try
            {
                Marshal.Copy(pcm, 0, data, pcm.Length);
                Marshal.StructureToPtr(new WAVEHDR { lpData = data, dwBufferLength = (uint)bytes }, hdr, false);
                waveOutPrepareHeader(h, hdr, Marshal.SizeOf(typeof(WAVEHDR)));
                waveOutWrite(h, hdr, Marshal.SizeOf(typeof(WAVEHDR)));
                DateTime end = DateTime.Now.AddSeconds(seconds);
                while (DateTime.Now < end) { if (during != null) during(); Thread.Sleep(40); }
                waveOutReset(h); waveOutUnprepareHeader(h, hdr, Marshal.SizeOf(typeof(WAVEHDR)));
            }
            finally { waveOutClose(h); Marshal.FreeHGlobal(data); Marshal.FreeHGlobal(hdr); }
        }
    }

    public class Symptom { public string Id, Title, Body; }

    public static class Doctor
    {
        public static readonly Symptom[] Symptoms = {
            new Symptom { Id = "checkup", Title = "Give everything a full check-up", Body = "Checks every part of the chain, repairs what it can, then plays a test sound through the tune to prove it reaches your headset." },
            new Symptom { Id = "silent", Title = "I hear nothing from the game", Body = "Follows the game sound step by step from Art Tune + to your ears and finds where it stops." },
            new Symptom { Id = "stock", Title = "The game sounds normal, footsteps are not boosted", Body = "Finds out why the tune is being skipped." },
            new Symptom { Id = "crackle", Title = "Crackling, robotic or delayed sound", Body = "Tries the usual cures: a bigger buffer, then the other driver mode for your headset." },
            new Symptom { Id = "other", Title = "Discord, browser or music has no sound", Body = "Checks the untuned path that everything except the game uses." },
            new Symptom { Id = "headset", Title = "I changed headset, or it does not show up", Body = "Looks at every playback device Windows knows, including hidden, disabled and unplugged ones." },
            new Symptom { Id = "quiet", Title = "The game is too quiet", Body = "Checks every volume control between the game and your headset." }
        };

        static string Db(float linear) { return linear <= 0.00001f ? "silence" : Math.Round(20 * Math.Log10(linear)).ToString() + " dB"; }

        /// Runs the audit and lets the guardian repair what it can. Returns true when nothing is left broken.
        static bool AuditAndHeal(Catalog cat, Action<string> say, out Status st, out List<Check> checks)
        {
            checks = Guardian.Audit(cat, out st);
            List<Check> broken = checks.FindAll(delegate(Check c) { return c.State == Health.Warn || c.State == Health.Bad; });
            if (broken.Count == 0) { say("The basics are all in place: tools, tune library, Art Tune + format, Equalizer APO, Voicemeeter, default device."); return true; }
            foreach (Check c in broken) say("Found: " + c.Title + " - " + c.Detail);
            foreach (string d in Guardian.Heal(cat, checks, st, say)) say("Fixed: " + d);
            checks = Guardian.Audit(cat, out st);
            broken = checks.FindAll(delegate(Check c) { return c.State == Health.Warn || c.State == Health.Bad; });
            foreach (Check c in broken) say("Still needs you: " + c.Title + " - " + (c.NeedsSetup ? "press Set up this PC on the Guardian page." : c.NeedsHeadset ? "pick your headset on the Headset page." : c.Detail));
            return broken.Count == 0;
        }

        /// Plays the test sound into Art Tune + and reads Voicemeeter's meters. inPeak = what came out of the tune, outPeak = what left for the headset.
        static bool retried;
        static bool SoundTest(Action<string> say, out float inPeak, out float outPeak)
        {
            float a = 0, b = 0;
            say("Playing a 3 second test sound into Art Tune + (you should hear soft thumps, left then right)...");
            try
            {
                Voicemeeter.With(delegate
                {
                    TestTone.Play("Art Tune +", 3.0, delegate
                    {
                        a = Math.Max(a, Math.Max(Voicemeeter.Level(1, 0), Voicemeeter.Level(1, 1)));
                        b = Math.Max(b, Math.Max(Voicemeeter.Level(3, 0), Voicemeeter.Level(3, 1)));
                    });
                    return 0;
                });
            }
            catch (Exception ex) { say("The test sound could not be played: " + ex.Message); inPeak = outPeak = 0; return false; }
            if (a < 0.0005f && !retried)
            {
                // Voicemeeter takes about five seconds to start its audio engine; if it was only just started, try once more.
                say("Nothing yet. Voicemeeter may still be starting, so waiting a few seconds and trying once more...");
                Thread.Sleep(7000); retried = true;
                bool ok = SoundTest(say, out inPeak, out outPeak); retried = false; return ok;
            }
            inPeak = a; outPeak = b;
            say("Out of the tune and into Voicemeeter: " + Db(a) + ". Out of Voicemeeter towards your headset: " + Db(b) + ".");
            return true;
        }

        static void MixerRouting(Action<string> say, bool fix)
        {
            Guardian.CheckRoute();
            if (Guardian.RouteChecked && !Guardian.RouteOk && fix && Guardian.Headset != "")
            {
                say("Found: Voicemeeter has lost its devices (this happens when it crashes or is force-closed). Setting it up again for " + Guardian.Headset + "...");
                Voicemeeter.Configure(Guardian.Headset, Voicemeeter.Wdm, delegate(string s) { });
                Guardian.RouteOk = true;
                say("Fixed: Voicemeeter is routed to " + Guardian.Headset + " again, and this time the setting is saved so it survives a crash.");
            }
            Voicemeeter.With(delegate
            {
                string input = Voicemeeter.GetS("Strip[0].device.name"), output = Voicemeeter.GetS("Bus[0].device.name");
                say("Voicemeeter input 1 is \"" + (input == "" ? "nothing" : input) + "\" and output A1 is \"" + (output == "" ? "nothing" : output) + "\".");
                if (!input.StartsWith("Art Tune Unified Output", StringComparison.OrdinalIgnoreCase)) say("Input 1 should be Art Tune Unified Output. Pick your headset again on the Headset page and it is put right.");
                if (output == "") say("No output device is chosen, so sound has nowhere to go. Pick your headset on the Headset page.");
                string[] names = { "Strip[0].Mute", "Strip[0].A1", "Bus[0].Mute", "Strip[2].Mute", "Strip[2].A1" };
                float[] want = { 0, 1, 0, 0, 1 };
                string[] plain = { "the game input was muted", "the game input was not sent to your headset (A1 off)", "the headset output was muted", "the normal-audio input was muted", "normal audio was not sent to your headset" };
                for (int i = 0; i < names.Length; i++)
                {
                    float v = Voicemeeter.GetF(names[i]);
                    if (!float.IsNaN(v) && Math.Abs(v - want[i]) > 0.5f)
                    {
                        if (fix) { Voicemeeter.SetF(names[i], want[i]); say("Fixed: " + plain[i] + "."); } else say("Found: " + plain[i] + ".");
                    }
                }
                float g1 = Voicemeeter.GetF("Strip[0].Gain"), gb = Voicemeeter.GetF("Bus[0].Gain");
                if (!float.IsNaN(g1) && !float.IsNaN(gb)) say("Voicemeeter volume: game input " + Math.Round(g1) + " dB, headset output " + Math.Round(gb) + " dB (0 is full, lower is quieter).");
                Thread.Sleep(300);
                return 0;
            });
        }

        static void HeadsetPresent(Action<string> say)
        {
            string want = Guardian.Headset;
            if (string.IsNullOrEmpty(want)) { try { want = Voicemeeter.CurrentOutput(); Guardian.Headset = want; Guardian.SaveSettings(); } catch { } }
            if (string.IsNullOrEmpty(want)) { say("No headset has been picked yet. Use the Headset page."); return; }
            Endpoint e = Audio.List().Find(delegate(Endpoint x) { return x.IsRender && string.Equals(x.FullName, want, StringComparison.OrdinalIgnoreCase); });
            if (e == null) say("Your headset \"" + want + "\" is not known to Windows any more. If you replaced it, pick the new one on the Headset page.");
            else if (e.State == 1) say("Your headset \"" + want + "\" is connected and switched on in Windows.");
            else if (e.State == 2) { Audio.SetEnabled(e.Id, true); say("Fixed: your headset \"" + want + "\" was disabled in Windows; it is enabled again."); }
            else say("Your headset \"" + want + "\" is unplugged or powered off. Plug it in or turn it on; the guardian reconnects it by itself.");
        }

        public static string DeviceList()
        {
            var sb = new StringBuilder();
            foreach (Endpoint e in Audio.List())
            {
                if (!e.IsRender) continue;
                string state = e.State == 1 ? "ready" : e.State == 2 ? "DISABLED in Windows" : e.State == 8 ? "unplugged" : "not present";
                sb.AppendLine("  " + e.FullName + " - " + state);
            }
            return sb.ToString();
        }

        public static void Run(string id, Catalog cat, Action<string> say)
        {
            Status st; List<Check> checks; float a, b;
            say("Looking into: " + Array.Find(Symptoms, delegate(Symptom s) { return s.Id == id; }).Title);
            bool basics = AuditAndHeal(cat, say, out st, out checks);
            bool haveMixer = Voicemeeter.IsInstalled && st.Mixer;

            if (id == "headset")
            {
                say("Every playback device Windows knows about:"); foreach (string l in DeviceList().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)) say(l);
                int enabled = 0;
                foreach (Endpoint e in Audio.List())
                    if (e.IsRender && e.State == 2 && e.Adapter.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) < 0) { Audio.SetEnabled(e.Id, true); enabled++; say("Fixed: \"" + e.FullName + "\" was disabled in Windows; enabled it so you can pick it."); }
                say(enabled > 0 ? "Now open the Headset page and press Find my headsets." : "If yours says unplugged, check the cable, dongle or Bluetooth and its power. If it is ready, open the Headset page, press Find my headsets and pick it.");
                say("Bluetooth headsets: use the entry called Headphones, not Headset or Hands-Free (that one is the low-quality call mode).");
                return;
            }
            if (!st.Stack) { say("The audio tools are not installed, so there is nothing more to test yet. Press Set up this PC on the Guardian page."); return; }
            if (!haveMixer) { say(Voicemeeter.IsInstalled ? "No headset has been picked yet, so the tuned sound goes nowhere. Use the Headset page." : "Voicemeeter is not installed here, so I cannot look inside your mixer. In your own mixer, make sure Art Tune Unified Output is added and sent to your headset."); return; }

            if (id == "checkup" || id == "silent" || id == "quiet" || id == "other")
            {
                HeadsetPresent(say);
                try { MixerRouting(say, true); } catch (Exception ex) { say("Could not read Voicemeeter: " + ex.Message); }
            }
            if (id == "checkup" || id == "silent" || id == "stock")
            {
                if (SoundTest(say, out a, out b))
                {
                    if (a < 0.0005f) say("Nothing came out of the tune. " + (basics ? "Restarting Windows usually clears this after a fresh install (the audio driver needs it)." : "Fix the items above first."));
                    else if (b < 0.0005f) say("The tune is producing sound, but it is not leaving Voicemeeter. Pick your headset again on the Headset page.");
                    else say("The whole chain works: the test sound went through the tune and out to your headset. If the game is still silent or sounds normal, the game is not sending to the tune: in MW4 set Game Sound Device to Art Tune + and Speaker Output to Windows Default (see Game settings).");
                }
            }
            if (id == "stock")
            {
                say(st.EapoOnPlus ? "Equalizer APO is attached to Art Tune + and the tune loaded is " + Guardian.DescribeTune(cat, st.Current) + "." : "Equalizer APO is not attached; the Guardian page can fix that.");
                say("In game, three settings switch the tune off without any error: Game Sound Device must be Art Tune +, Speaker Output must be Windows Default (not 7.1 or Stereo), and Enhanced Headphone Mode must be Off.");
                say("To hear the difference clearly, try Classic LE with Ultra Low gun on the Tune page: footsteps come up a lot and your own gun drops.");
            }
            if (id == "crackle")
            {
                try
                {
                    string result = Voicemeeter.With(delegate
                    {
                        float buf = Voicemeeter.GetF("Option.buffer.wdm");
                        if (!float.IsNaN(buf) && buf < 500) { Voicemeeter.SetF("Option.buffer.wdm", 512f); Thread.Sleep(300); Voicemeeter.SetF("Command.Restart", 1f); Thread.Sleep(1200); return "Raised Voicemeeter's WDM buffer from " + buf + " to 512 and restarted its audio engine. Play for a few minutes."; }
                        if (!float.IsNaN(buf) && buf < 1000) { Voicemeeter.SetF("Option.buffer.wdm", 1024f); Thread.Sleep(300); Voicemeeter.SetF("Command.Restart", 1f); Thread.Sleep(1200); return "The buffer was already " + buf + ", so I raised it to 1024 and restarted the audio engine."; }
                        Voicemeeter.SetF("Command.Restart", 1f); Thread.Sleep(1200);
                        return "The buffer is already large (" + buf + "), so I only restarted the audio engine.";
                    });
                    say("Fixed: " + result);
                }
                catch (Exception ex) { say("Could not change Voicemeeter: " + ex.Message); }
                say("If it still crackles: on the Headset page pick your headset again. If that does not help, open Voicemeeter, click A1 and choose the same headset from the KS list instead of WDM.");
                say("Also worth knowing: Bluetooth headsets and wireless dongles on a busy USB hub are the usual cause. A wired connection or a different USB port often cures it.");
            }
            if (id == "other")
            {
                say(st.Normal != null ? "Windows sends everything except the game to Normal Audio, which Voicemeeter passes to your headset." : "The Normal Audio device is missing; run Set up this PC again.");
                say("In Discord, set Output Device to Normal Audio (or Default). In MW4, set Voice Chat Output Device to Normal Audio too.");
            }
            if (id == "quiet")
            {
                double boost = ConfigWriter.ReadBoost();
                say("Volume boost on the Tune page is " + boost + " dB. Raise it a few dB at a time if needed.");
                say("In game, Master and Effects volume must both be 100. In the Windows volume mixer, Call of Duty must be at 100 as well.");
                say("Theater styles are quieter than Classic LE on purpose (the world is left natural). Classic LE is the loud one.");
            }
            say("Done.");
        }

        /// A plain-text report a friend can paste to whoever is helping them.
        public static string Report(Catalog cat)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ClearSteps " + Setup.AppVersion + " report, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Windows " + Environment.OSVersion.Version + ", library " + Paths.LibraryVersion);
            Status st; foreach (Check c in Guardian.Audit(cat, out st)) sb.AppendLine((c.State == Health.Ok ? "[ok]   " : c.State == Health.Off ? "[n/a]  " : "[FAIL] ") + c.Title + ": " + c.Detail);
            sb.AppendLine("Headset on record: " + (Guardian.Headset == "" ? "(none)" : Guardian.Headset));
            sb.AppendLine("Playback devices:"); sb.Append(DeviceList());
            try { if (File.Exists(Paths.ConfigFile)) { sb.AppendLine("config.txt:"); foreach (string l in File.ReadAllLines(Paths.ConfigFile)) if (l.StartsWith("Include:") || l.StartsWith("Device:")) sb.AppendLine("  " + l); } } catch { }
            try { if (File.Exists(Guardian.LogFile)) { string[] all = File.ReadAllLines(Guardian.LogFile); sb.AppendLine("Recent activity:"); for (int i = Math.Max(0, all.Length - 25); i < all.Length; i++) sb.AppendLine("  " + all[i]); } } catch { }
            return sb.ToString();
        }
    }
}
