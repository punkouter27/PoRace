using System;
using UnityEngine;

namespace PoRace {

/// <summary>B6 procedural servo voice for one racer, synthesised on the audio thread: up to four sine partials (one per
/// limb, GfxTier.ServoPartials) whose pitch follows that limb's joint speed |q̇| and level its torque, band-passed motor
/// hiss that rises with mechanical power, and (Mobile, where a racer gets one voice) the foot-scrape noise. It rides on
/// a looping clip of ones on the same AudioSource, so the source's 3D panning and roll-off and the distance low-pass
/// after it still apply. RaceAudio writes the targets once per frame; each audio block glides to them (no zipper).
/// Add it after the AudioSource and before the AudioLowPassFilter: filters run in component order.</summary>
public class ServoSynth : MonoBehaviour {
  public const int MaxPartials = 4;
  [NonSerialized] public int partials = 1;
  [NonSerialized] public readonly float[] freq = new float[MaxPartials], gain = new float[MaxPartials];
  /// <summary>Hiss level 0..1 and band centre (Hz); scrape level 0..1 and one-pole tone 0..1 (lower = darker).</summary>
  [NonSerialized] public float hiss, hissFreq = 1200f, scrape, scrapeTone = 0.2f;

  readonly float[] _f = new float[MaxPartials], _g = new float[MaxPartials], _df = new float[MaxPartials], _dg = new float[MaxPartials], _ph = new float[MaxPartials];
  float _hiss, _scrape, _low, _band, _lp, _sr = 48000f; uint _rng = 0x9E3779B9u;

  void Awake() { _sr = AudioSettings.outputSampleRate; for (int p = 0; p < MaxPartials; p++) { freq[p] = _f[p] = 300f; _ph[p] = p * 0.27f; } }

  float Noise() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return _rng * (2f / uint.MaxValue) - 1f; }

  void OnAudioFilterRead(float[] data, int channels) {
    int n = data.Length / channels; if (n == 0) return;
    float inv = 1f / n; int np = Mathf.Clamp(partials, 1, MaxPartials);
    for (int p = 0; p < np; p++) { _df[p] = (freq[p] - _f[p]) * inv; _dg[p] = (gain[p] - _g[p]) * inv; }
    float dh = (hiss - _hiss) * inv, ds = (scrape - _scrape) * inv, tone = Mathf.Clamp01(scrapeTone);
    float F = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(hissFreq, _sr * 0.12f) / _sr), invSr = 1f / _sr;
    for (int i = 0; i < n; i++) {
      float s = 0f;
      for (int p = 0; p < np; p++) {
        _f[p] += _df[p]; _g[p] += _dg[p];
        float ph = _ph[p] += _f[p] * invSr; if (ph >= 1f) _ph[p] = ph -= 1f;
        float w = 2f * Mathf.PI * ph;
        s += _g[p] * (Mathf.Sin(w) + 0.3f * Mathf.Sin(2f * w) + 0.12f * Mathf.Sin(3f * w));
      }
      float x = Noise();
      _low += F * _band; float high = x - _low - 0.6f * _band; _band += F * high;   // Chamberlin state-variable band-pass
      _lp += (x - _lp) * tone;
      _hiss += dh; _scrape += ds;
      float y = Mathf.Clamp(0.35f * s + 0.5f * _hiss * _band + 1.6f * _scrape * _lp, -1f, 1f);
      for (int c = 0; c < channels; c++) data[i * channels + c] *= y;
    }
  }
}

}
