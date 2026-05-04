using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Central audio hub for the game. Owns all AudioSources and handles sound effects
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [Tooltip("AudioSource used for the SFX clips. Created automatically if left empty.")]
    public AudioSource sfxSource;
    [Tooltip("AudioSource used for looping SFX. Created automatically if left empty.")]
    public AudioSource loopingSFXSource;

    [Header("SFX")]
    [Tooltip("All sound effect clips. Reference by clip name when calling PlaySFX().")]
    public List<AudioClip> sfxClips;
    [Tooltip("Minimum seconds before the same SFX clip can play again.")]
    public float sfxCooldown = 0.05f;

    private Dictionary<string, AudioClip> sfxLookup     = new Dictionary<string, AudioClip>();
    private Dictionary<string, float>     sfxLastPlayed = new Dictionary<string, float>();
    
    // Tracks which clip key is currently looping so we can guard against restarts
    private string currentLoopingKey = null;

    // ─────────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        InitialiseSFXSource();
        InitialiseLoopingSFXSource();
        
    }
    #endregion
    
    // ─────────────────────────────────────────────────────────────────────────────
    #region Initialisation

    private void InitialiseSFXSource()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        foreach (var clip in sfxClips)
            if (clip != null) sfxLookup[clip.name] = clip;
    }
    
    private void InitialiseLoopingSFXSource()
    {
        if (loopingSFXSource == null)
        {
            loopingSFXSource = gameObject.AddComponent<AudioSource>();
            loopingSFXSource.playOnAwake = false;
            loopingSFXSource.loop = true;
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    #region SFX

    /// <summary>
    /// Plays a one-shot SFX by name. Silently skipped if the clip is on cooldown
    /// or the key is not found.
    /// </summary>
    public void PlaySFX(string key, float volume = 1f)
    {
        if (!sfxLookup.TryGetValue(key, out var clip))
        {
            Debug.LogWarning($"[AudioManager] SFX '{key}' not found. Check sfxClips list.");
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (sfxLastPlayed.TryGetValue(key, out float lastTime) && now - lastTime < sfxCooldown)
            return;

        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        sfxLastPlayed[key] = now;
    }

    /// <summary>
    /// Starts a looping SFX by name. If the same key is already looping, does nothing.
    /// Replaces any different clip that was previously looping.
    /// </summary>
    /// <param name="key">Clip name to loop.</param>
    /// <param name="volume">Playback volume 0–1.</param>
    public void PlaySFXLooping(string key, float volume = 1f)
    {
        if (!sfxLookup.TryGetValue(key, out var clip))
        {
            Debug.LogWarning($"[AudioManager] Looping SFX '{key}' not found. Check sfxClips list.");
            return;
        }

        // Already looping this clip — do nothing
        if (currentLoopingKey == key && loopingSFXSource.isPlaying)
            return;

        loopingSFXSource.Stop();
        loopingSFXSource.clip   = clip;
        loopingSFXSource.volume = Mathf.Clamp01(volume);
        loopingSFXSource.Play();
        currentLoopingKey = key;
    }

    /// <summary>
    /// Stops the currently looping SFX. If a specific key is provided,
    /// only stops if that key is the one currently looping.
    /// </summary>
    /// <param name="key">Optional key to match before stopping. Pass null to force stop any loop.</param>
    public void StopSFXLooping(string key = null)
    {
        if (key != null && currentLoopingKey != key)
            return;

        loopingSFXSource.Stop();
        loopingSFXSource.clip = null;
        currentLoopingKey = null;
    }

    /// <summary>
    /// Stops all SFX immediately including any active loop, and resets cooldown timers.
    /// </summary>
    public void StopAllSFX()
    {
        if (sfxSource != null && sfxSource.isPlaying)
            sfxSource.Stop();

        StopSFXLooping();
        sfxLastPlayed.Clear();
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────────────────
}