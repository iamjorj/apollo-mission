# Where the speech and the Quindar tones are in the radio recordings (float32 mono dumps from Step6Assets)
import sys, numpy as np
def load(p, sr): return np.fromfile(p, dtype='<f4'), sr
def analyse(p, sr, around):
    x, sr = load(p, sr)
    win = int(sr * 0.05)
    n = len(x) // win
    f = x[:n * win].reshape(n, win)
    rms = np.sqrt((f ** 2).mean(axis=1)) + 1e-9
    db = 20 * np.log10(rms)
    floor = np.percentile(db, 20)
    t = np.arange(n) * 0.05
    def tone(hz):
        k = np.exp(-2j * np.pi * hz * np.arange(win) / sr)
        return np.abs(f @ k) / win / rms
    q1, q2 = tone(2525), tone(2475)
    for (a, b, what) in around:
        i0, i1 = int(a / 0.05), min(n, int(b / 0.05))
        s = ''
        segs = []; on = False
        for i in range(i0, i1):
            sp = db[i] > floor + 9
            if sp and not on: start = t[i]; on = True
            if not sp and on:
                if t[i] - start > 0.25: segs.append((round(start, 2), round(t[i], 2)))
                on = False
        tones = [round(t[i], 2) for i in range(i0, i1) if max(q1[i], q2[i]) > 0.45]
        # merge tone frames
        tm = []
        for v in tones:
            if not tm or v - tm[-1][1] > 0.12: tm.append([v, v])
            else: tm[-1][1] = v
        print(f'{p.split("/")[-1]} {what} [{a}-{b}] floor {floor:.1f} dB')
        print('   speech:', segs)
        print('   quindar:', [(round(a_, 2), round(b_ + 0.05, 2), '2525' if q1[int(a_ / 0.05)] > q2[int(a_ / 0.05)] else '2475') for a_, b_ in tm])
D = '../SourceFiles/Step6_Audio/'
analyse(D + 'a12a_000_00_00.f32', 11025, [(25, 75, 'strikes + Conrad dropout'), (85, 115, 'Try SCE to aux'), (170, 200, 'fuel cells back')])
analyse(D + 'a11_0022500.f32', 8820, [(85, 108.7, 'Go for TLI')])
analyse(D + 'a11_0024020.f32', 8820, [(230, 262, 'ignition confirm'), (575, 630, 'cutoff'), (750, 780, 'magnificent ride')])
analyse(D + 'a11_0031627ob.f32', 8820, [(20, 50, 'Sep')])
analyse(D + 'a11_0032920.f32', 8820, [(0, 30, 'we are docked')])
analyse(D + 'a11_0753555.f32', 8820, [(265, 300, 'see you on the other side'), (300, 340, 'LOS')])
analyse(D + 'a11_0761400.f32', 8820, [(90, 125, 'Do you read'), (460, 495, 'like perfect')])
