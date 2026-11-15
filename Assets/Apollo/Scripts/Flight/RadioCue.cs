// Short pieces of the real air-to-ground recordings (Apollo Flight Journal, Resources/Radio), played at their
// moment: a clip from one place in a file to another, faded in and out so a cut never clicks.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RadioCue : MonoBehaviour
{
    // every line used, by name: file, start and end (seconds into the file), placed from the speech and the
    // Quindar tones found in each recording (Tools/radio_lines.py)
    public static readonly Dictionary<string, (string clip, float from, float to)> Lines = new Dictionary<string, (string, float, float)>
    {
        // Apollo 12, 14 Nov 1969 (a12a_000_00_00): Conrad after the strikes, CAPCOM Gerry Carr's call, the fuel cells back
        { "a12_dropout", ("a12a_000_00_00", 62.2f, 69.0f) },
        { "a12_sce", ("a12a_000_00_00", 92.9f, 96.8f) },
        { "a12_fuelcells", ("a12a_000_00_00", 177.9f, 187.2f) },
        // Apollo 11
        { "tli_go", ("a11_0022500", 97.2f, 101.4f) },             // McCandless: "Apollo 11, this is Houston. You are Go for TLI."
        { "tli_ignition", ("a11_0024020", 248.2f, 251.6f) },      // "We confirm ignition, and the thrust is Go."
        { "tli_cutoff", ("a11_0024020", 617.3f, 623.4f) },        // "We show cut-off"
        { "tli_ride", ("a11_0024020", 763.8f, 773.8f) },          // Armstrong: "That Saturn gave us a magnificent ride."
        { "sep", ("a11_0031627ob", 31.5f, 37.0f) },               // onboard: "Sep!"
        { "docked", ("a11_0032920", 9.9f, 17.2f) },              // "We are docked."
        { "los", ("a11_0753555", 284.0f, 294.0f) },               // "We'll see you on the other side."
        { "aos", ("a11_0761400", 110.0f, 115.0f) },               // "Apollo 11, Apollo 11, this is Houston. Do you read?"
        { "perfect", ("a11_0761400", 475.0f, 481.0f) },           // Collins: "It was like - it was like perfect!"
        // Apollo 11's powered descent (ALSJ a11a1023114-4746: the file runs about 2.5 s ahead of GET - 102:31:14)
        { "l_alarm", ("a11_1023114", 430.1f, 437.9f) },           // Armstrong: "Program alarm." Duke: "It's looking good to us." "It's a 1202."
        { "l_go_alarm", ("a11_1023114", 457.3f, 460.7f) },        // Duke: "Roger. We got you... We're Go on that alarm."
        { "l_go_landing", ("a11_1023114", 651.4f, 657.0f) },      // Duke: "Roger. Copy. Eagle, Houston. You're Go for landing. Over."
        { "l_1201", ("a11_1023114", 666.4f, 673.9f) },            // "1201." Duke: "Roger. 1201 alarm. We're Go. Same type."
        { "l_60", ("a11_1023114", 825.3f, 826.8f) },              // Duke: "60 seconds."
        { "l_30", ("a11_1023114", 854.3f, 855.8f) },              // Duke: "30 seconds."
        { "l_contact", ("a11_1023114", 862.9f, 864.2f) },         // Aldrin: "Contact light."
        { "l_stop", ("a11_1023114", 866.5f, 869.6f) },            // Armstrong: "Shutdown." Aldrin: "Okay. Engine stop."
        { "l_landed", ("a11_1023114", 886.1f, 896.5f) },          // Armstrong: "Houston, Tranquility Base here. The Eagle has landed." Duke: "...We're breathing again."
    };

    AudioSource src;
    float stopAt = -1f;
    readonly HashSet<string> played = new HashSet<string>();

    public static RadioCue On(GameObject host) { var r = host.GetComponent<RadioCue>(); return r ? r : host.AddComponent<RadioCue>(); }

    // load every file now, so a line starts exactly on time later
    public void Preload()
    {
        foreach (var l in Lines.Values)
        {
            var c = Resources.Load<AudioClip>("Radio/" + l.clip);
            if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        }
    }

    // a line, once
    public void Say(string key, float volume = 0.95f)
    {
        if (played.Contains(key) || !Lines.TryGetValue(key, out var l)) return;
        played.Add(key);
        var c = Resources.Load<AudioClip>("Radio/" + l.clip);
        if (!c) { Debug.LogWarning("RADIO missing " + l.clip); return; }
        if (!src) { src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f; src.playOnAwake = false; src.priority = 0; }
        StopAllCoroutines();
        StartCoroutine(Play(c, l.from, l.to, volume));
        Debug.Log($"RADIO {key}: {l.clip} {l.from:0.0}-{l.to:0.0} s");
    }

    public bool Busy => src && src.isPlaying;

    // a line may play again (after a rewind)
    public void Again(string key) => played.Remove(key);
    public void Stop() { StopAllCoroutines(); if (src) src.Stop(); }

    IEnumerator Play(AudioClip c, float from, float to, float volume)
    {
        if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        while (c.loadState == AudioDataLoadState.Loading) yield return null;
        src.Stop();
        src.clip = c;
        src.time = Mathf.Clamp(from, 0f, c.length - 0.1f);
        src.volume = 0f;
        src.Play();
        stopAt = Time.unscaledTime + (Mathf.Min(to, c.length) - from);
        while (src.isPlaying)
        {
            float left = stopAt - Time.unscaledTime, since = (to - from) - left;
            src.volume = volume * Mathf.Clamp01(since / 0.12f) * Mathf.Clamp01(left / 0.3f);
            if (left <= 0f) { src.Stop(); break; }
            yield return null;
        }
    }
}
