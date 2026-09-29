using BirdHell.Core;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Media;

namespace BirdHell.Presentation;

public interface IGameplayAudioSink
{
    void PlayProjectileFired();
    void PlayPlayerHit();
    void PlayPlayerDeath();
    void PlayEnemyHit();
    void PlayEnemyDeath();
}

/// <summary>Routes confirmed simulation events to presentation without re-evaluating combat rules.</summary>
public static class GameplayFeedbackAudioRouter
{
    public static void Route(GameplayFeedbackEvents feedback, IGameplayAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        for (var index = 0; index < feedback.ProjectilesFired; index++) sink.PlayProjectileFired();
        for (var index = 0; index < feedback.PlayerDamageEvents; index++) sink.PlayPlayerHit();
        if (feedback.PlayerDied) sink.PlayPlayerDeath();
        for (var index = 0; index < feedback.EnemyDamageEvents; index++) sink.PlayEnemyHit();
        for (var index = 0; index < feedback.EnemyDeaths; index++) sink.PlayEnemyDeath();
    }
}

/// <summary>Owns the one-time loaded gameplay audio assets for the MonoGame host.</summary>
public sealed class GameplayAudio : IGameplayAudioSink, IDisposable
{
    private const string MusicAsset = "audio/Fading Poisons";
    private const string PlayerHitAsset = "audio/oof";
    private const string EnemyHitAsset = "audio/hit";
    private const string PlayerDeathAsset = "audio/death_sound";
    private const string EnemyDeathAsset = "audio/enemy_death";
    private const string ProjectileFiredAsset = "audio/blaster";

    private readonly Song _music;
    private readonly SoundEffect _playerHit;
    private readonly SoundEffect _enemyHit;
    private readonly SoundEffect _playerDeath;
    private readonly SoundEffect _enemyDeath;
    private readonly SoundEffect _projectileFired;
    private bool _disposed;

    public GameplayAudio(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _music = content.Load<Song>(MusicAsset);
        _playerHit = content.Load<SoundEffect>(PlayerHitAsset);
        _enemyHit = content.Load<SoundEffect>(EnemyHitAsset);
        _playerDeath = content.Load<SoundEffect>(PlayerDeathAsset);
        _enemyDeath = content.Load<SoundEffect>(EnemyDeathAsset);
        _projectileFired = content.Load<SoundEffect>(ProjectileFiredAsset);
    }

    public void StartMusic()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MediaPlayer.IsRepeating = true;
        if (MediaPlayer.State != MediaState.Playing) MediaPlayer.Play(_music);
    }

    public void PauseMusic()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (MediaPlayer.State == MediaState.Playing) MediaPlayer.Pause();
    }

    public void ResumeMusic()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (MediaPlayer.State == MediaState.Paused) MediaPlayer.Resume();
        else if (MediaPlayer.State != MediaState.Playing) StartMusic();
    }

    public void PlayProjectileFired() => _projectileFired.Play();
    public void PlayPlayerHit() => _playerHit.Play();
    public void PlayPlayerDeath() => _playerDeath.Play();
    public void PlayEnemyHit() => _enemyHit.Play();
    public void PlayEnemyDeath() => _enemyDeath.Play();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MediaPlayer.Stop();
        // ContentManager owns and disposes all loaded Song/SoundEffect assets.
    }
}
