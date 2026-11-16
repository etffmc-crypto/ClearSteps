"""Plays 16-channel test audio into 'Art Tune +' and records 'Art Tune Unified Output' to prove the tune is live.
Stands in for MW4 (which outputs 16 channels to that device). Usage: python chain_test.py <label> [out.json]"""
import sys, json, time
import numpy as np
import sounddevice as sd

SR = 48000
label = sys.argv[1] if len(sys.argv) > 1 else "run"

def find(name, want_out):
    wasapi = next(i for i, h in enumerate(sd.query_hostapis()) if "WASAPI" in h["name"])
    for i, d in enumerate(sd.query_devices()):
        if d["hostapi"] == wasapi and d["name"].startswith(name) and (d["max_output_channels"] if want_out else d["max_input_channels"]) > 0:
            return i, d
    raise SystemExit("device not found: " + name)

out_i, out_d = find("Art Tune +", True)
in_i, in_d = find("Art Tune Unified Output", False)
ch = out_d["max_output_channels"]
print(f"play -> {out_d['name']} ({ch} ch)   record <- {in_d['name']} ({in_d['max_input_channels']} ch)")

def footsteps(seconds):
    """Quiet, short low-mid thumps twice a second: a stand-in for distant footsteps."""
    n = int(SR * seconds); x = np.zeros(n, dtype=np.float32); rng = np.random.default_rng(1)
    for start in range(0, n - 4800, SR // 2):
        t = np.arange(4800) / SR
        x[start:start + 4800] += (np.sin(2 * np.pi * 180 * t) + 0.5 * rng.standard_normal(4800)) * np.exp(-t * 60)
    return x * 0.02

def gunfire(seconds):
    """Loud broadband bursts ten times a second: a stand-in for your own gun."""
    n = int(SR * seconds); x = np.zeros(n, dtype=np.float32); rng = np.random.default_rng(2)
    for start in range(0, n - 2400, SR // 10):
        x[start:start + 2400] += rng.standard_normal(2400) * np.exp(-np.arange(2400) / SR * 40)
    return x * 0.5

def run(name, sig_by_channel, seconds=3.0):
    n = int(SR * seconds)
    buf = np.zeros((n, ch), dtype=np.float32)
    for c, sig in sig_by_channel.items():
        if c < ch: buf[:, c] = sig[:n]
    rec = sd.playrec(buf, samplerate=SR, device=(in_i, out_i), input_mapping=[1, 2], dtype="float32")
    sd.wait(); time.sleep(0.3)
    body = rec[int(SR * 0.5):]                       # skip start-up
    rms = np.sqrt(np.mean(body ** 2, axis=0)) + 1e-9
    db = [round(float(20 * np.log10(v)), 1) for v in rms]
    peak = round(float(20 * np.log10(np.max(np.abs(body)) + 1e-9)), 1)
    print(f"{name:34s} L {db[0]:7.1f} dB   R {db[1]:7.1f} dB   peak {peak:6.1f} dB")
    return {"test": name, "L_dB": db[0], "R_dB": db[1], "peak_dB": peak}

steps, gun = footsteps(3), gunfire(3)
results = [
    run("silence", {}),
    run("footsteps front-left (ch0)", {0: steps}),
    run("footsteps front-right (ch1)", {1: steps}),
    run("footsteps rear-left (ch4)", {4: steps}),
    run("footsteps rear-right (ch5)", {5: steps}),
    run("gunfire front L+R (ch0,1)", {0: gun, 1: gun}),
    run("steps rear-left + gun front", {4: steps, 0: gun, 1: gun}),
]
if len(sys.argv) > 2:
    json.dump({"label": label, "results": results}, open(sys.argv[2], "w"), indent=1)
