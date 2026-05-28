using UnityEngine;

/// <summary>
/// Simple looping background music. Drop this on any GameObject,
/// assign a clip, and it plays on loop from Start.
///
/// Setup:
///   1. Create an empty GameObject, name it "Music".
///   2. Add this script.
///   3. Drag your music clip into the Music Clip field.
///   4. Adjust Volume (0–1) and Pitch.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    [SerializeField, Tooltip("The music clip to loop.")]
    private AudioClip musicClip;

    [SerializeField, Range(0f, 1f), Tooltip("Volume (0–1).")]
    private float volume = 0.5f;

    private AudioSource source;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.clip = musicClip;
        source.loop = true;
        source.volume = volume;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D — full volume everywhere
    }

    private void Start()
    {
        if (musicClip != null)
            source.Play();
    }

    /// <summary>Fade to a new volume over <paramref name="duration"/> seconds.</summary>
    public void FadeTo(float targetVolume, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeCoroutine(targetVolume, duration));
    }

    private System.Collections.IEnumerator FadeCoroutine(float target, float duration)
    {
        float start = source.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        source.volume = target;
    }
}
