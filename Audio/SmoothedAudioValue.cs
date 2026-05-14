using UnityEngine;

namespace VoiceChatPlugin.Audio;

internal sealed class SmoothedAudioValue
{
    public float Current { get; private set; }

    public SmoothedAudioValue(float initial = 0f)
    {
        Current = initial;
    }

    public void SetImmediate(float value)
    {
        Current = value;
    }

    public float Step(float target, float unitsPerSecond)
    {
        Current = Mathf.MoveTowards(Current, target, Mathf.Max(0.001f, unitsPerSecond) * Time.deltaTime);
        return Current;
    }
}
