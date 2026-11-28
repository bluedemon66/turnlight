using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OneBeam.Levels.Presentation.Chamber;

/// <summary>Sound effect ids from 飞书《一束光线》音效 v1.1, 3.3.</summary>
public enum Sfx
{
    A1, A2,                  // environment: hum loop, dome tick
    B1, B2, B3, B4,          // light: move a cell, turn, turn refused, light hum loop
    C1, C2, C3, C4, C5,      // plants: stage up, mother, wilt, recover, die
    D1, D2, D3, D4, D5, D6,  // sowing: seed button, hover pit, sow, refused, mother seeds, seed lands
    E1, E2, E3, E4, E5, E6,  // level: start, pause/resume, speed, won, lost, restart
    F1, F2,                  // interface: card opens, journal
}

/// <summary>Background music ids, same document 3.4.</summary>
public enum Track { M1, M2, M3, M4 }

/// <summary>
/// One place for all sound effects and background music. Game code only says what happened
/// (<see cref="Play"/>, <see cref="SetLoop"/>, <see cref="SetPaused"/>, <see cref="Settle"/>);
/// the rules of 3.2 and 3.4 live here. No audio files exist yet: a sound is looked up at
/// res://audio/sfx/&lt;id&gt;.(ogg|wav|mp3) and res://audio/music/&lt;id&gt;.(ogg|mp3); a missing
/// file plays nothing. Every request is still recorded in <see cref="Log"/> for tests.
/// Game speed never touches pitch_scale, so sounds and music keep their pitch at any speed.
/// </summary>
public partial class SoundDirector : Node
{
    private const double SameSoundWindow = .1;   // 3.2: one play per sound per 0.1 s
    private const int Voices = 6;
    private const float MusicVolumeDb = -12;     // music sits under the effects
    private const float PausedMusicDropDb = -10;
    private const float PausedAmbienceDropDb = -8;
    private const double Crossfade = 3;
    private const double DomeTickMin = 20, DomeTickMax = 40;

    // 3.2 priority, high to low: death > wilt > seed > stage up > everything else (operations).
    private static int Priority(Sfx id) => id switch
    {
        Sfx.C5 => 4,
        Sfx.C3 => 3,
        Sfx.D5 => 2,
        Sfx.C1 or Sfx.C2 => 1,
        _ => 0
    };

    private static readonly Sfx[] Loops = { Sfx.A1, Sfx.B4 };

    private readonly Dictionary<Sfx, AudioStream?> _sfx = new();
    private readonly Dictionary<Track, AudioStream?> _music = new();
    private readonly Dictionary<Sfx, double> _lastPlayed = new();
    private readonly List<(AudioStreamPlayer Player, int Priority)> _voices = new();
    private readonly Dictionary<Sfx, AudioStreamPlayer> _loops = new();
    private readonly HashSet<Sfx> _loopsWanted = new();
    private readonly List<string> _log = new();
    private AudioStreamPlayer _musicA = null!, _musicB = null!;
    private Track? _current;
    private readonly Random _random = new();
    private bool _paused, _settled;
    private double _nextDomeTick;
    private Tween? _musicTween;

    /// <summary>Every request in order, e.g. "C1", "loop+B4", "pause", "music M2"; kept short.</summary>
    public IReadOnlyList<string> Log => _log;
    public void ClearLog() => _log.Clear();
    public Track? CurrentTrack => _current;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Name = $"Voice{i}" };
            AddChild(p);
            _voices.Add((p, 0));
        }
        foreach (var id in Loops)
        {
            var p = new AudioStreamPlayer { Name = $"Loop{id}" };
            AddChild(p);
            _loops[id] = p;
        }
        _musicA = new AudioStreamPlayer { Name = "MusicA", VolumeDb = -80 };
        _musicB = new AudioStreamPlayer { Name = "MusicB", VolumeDb = -80 };
        AddChild(_musicA);
        AddChild(_musicB);
        _musicA.Finished += NextTrack;
        _musicB.Finished += NextTrack;
        _nextDomeTick = Now + Range(DomeTickMin, DomeTickMax);
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;
    private double Range(double a, double b) => a + _random.NextDouble() * (b - a);

    private void Record(string entry)
    {
        _log.Add(entry);
        if (_log.Count > 400) _log.RemoveRange(0, 100);
    }

    /// <summary>A one-shot sound effect.</summary>
    public void Play(Sfx id)
    {
        // 3.2: after the result only the result sounds (and the hum) play.
        if (_settled && id is not (Sfx.E4 or Sfx.E5)) return;
        double now = Now;
        if (_lastPlayed.TryGetValue(id, out double last) && now - last < SameSoundWindow) return;
        _lastPlayed[id] = now;
        Record(id.ToString());
        if (Stream(id) is not { } stream) return;

        int priority = Priority(id);
        int free = _voices.FindIndex(v => !v.Player.Playing);
        if (free < 0)
        {
            // All voices busy: replace the lowest-priority one if this sound matters more.
            int low = Enumerable.Range(0, _voices.Count).MinBy(i => _voices[i].Priority);
            if (_voices[low].Priority >= priority) return;
            free = low;
        }
        var player = _voices[free].Player;
        player.Stream = stream;
        player.Play();
        _voices[free] = (player, priority);
    }

    /// <summary>Start or stop a looping sound (A1 hum, B4 light hum). Loops stay silent while paused.</summary>
    public void SetLoop(Sfx id, bool on)
    {
        if (!_loops.ContainsKey(id)) throw new ArgumentException($"{id} is not a loop.", nameof(id));
        if (on == _loopsWanted.Contains(id)) return;
        if (on) _loopsWanted.Add(id); else _loopsWanted.Remove(id);
        Record((on ? "loop+" : "loop-") + id);
        ApplyLoops();
    }

    /// <summary>3.2 / 3.4: pausing stops the loops except the hum, lowers hum and music.</summary>
    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Record(paused ? "pause" : "resume");
        ApplyLoops();
        FadeMusic(_paused ? MusicVolumeDb + PausedMusicDropDb : MusicVolumeDb, .4);
    }

    /// <summary>The day ended: fade the music out; only result sounds play until <see cref="Resume"/>.</summary>
    public void Settle()
    {
        if (_settled) return;
        _settled = true;
        Record("settle");
        _loopsWanted.Remove(Sfx.B4);
        ApplyLoops();
        FadeMusic(-80, 1.5);
    }

    /// <summary>
    /// Back to play after the result (restart or the next day): the next track starts.
    /// A restart during play keeps the current track (3.4: no change on restart or new day).
    /// </summary>
    public void Resume()
    {
        _paused = false;
        if (!_settled) { ApplyLoops(); return; }
        _settled = false;
        Record("resume-after-result");
        ApplyLoops();
        NextTrack();
    }

    /// <summary>Start the music rotation if it is not running.</summary>
    public void StartMusic()
    {
        if (_current == null) NextTrack();
    }

    private void ApplyLoops()
    {
        foreach (var (id, player) in _loops)
        {
            bool want = _loopsWanted.Contains(id) && (!_paused || id == Sfx.A1);
            player.VolumeDb = id == Sfx.A1 && _paused ? PausedAmbienceDropDb : 0;
            if (want && !player.Playing && Stream(id) is { } stream)
            {
                if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
                else if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                player.Stream = stream;
                player.Play();
            }
            else if (!want && player.Playing) player.Stop();
        }
    }

    /// <summary>3.4: rotate through the tracks that exist, never the same one twice in a row, 3 s crossfade.</summary>
    private void NextTrack()
    {
        if (_settled) return;
        var available = Enum.GetValues<Track>().Where(t => MusicStream(t) != null).ToList();
        var choices = Enum.GetValues<Track>().Where(t => t != _current).ToList();
        var pool = available.Where(t => t != _current).ToList();
        var next = pool.Count > 0 ? pool[_random.Next(pool.Count)] : choices[_random.Next(choices.Count)];
        _current = next;
        Record($"music {next}");
        if (MusicStream(next) is not { } stream) return;
        var (outgoing, incoming) = _musicA.Playing ? (_musicA, _musicB) : (_musicB, _musicA);
        incoming.Stream = stream;
        incoming.VolumeDb = -80;
        incoming.Play();
        _musicTween?.Kill();
        _musicTween = CreateTween().SetParallel();
        float target = _paused ? MusicVolumeDb + PausedMusicDropDb : MusicVolumeDb;
        _musicTween.TweenProperty(incoming, "volume_db", target, Crossfade);
        if (outgoing.Playing) _musicTween.TweenProperty(outgoing, "volume_db", -80f, Crossfade);
        _musicTween.Chain().TweenCallback(Callable.From(() => { if (outgoing.VolumeDb <= -79) outgoing.Stop(); }));
    }

    private void FadeMusic(float volumeDb, double seconds)
    {
        var playing = new[] { _musicA, _musicB }.Where(p => p.Playing).ToList();
        if (playing.Count == 0) return;
        _musicTween?.Kill();
        _musicTween = CreateTween().SetParallel();
        foreach (var p in playing) _musicTween.TweenProperty(p, "volume_db", volumeDb, seconds);
    }

    public override void _Process(double delta)
    {
        // A2: a small tick of the warm glass dome every 20-40 s while the day runs.
        if (Now < _nextDomeTick) return;
        _nextDomeTick = Now + Range(DomeTickMin, DomeTickMax);
        if (!_paused && !_settled && _loopsWanted.Contains(Sfx.A1)) Play(Sfx.A2);
    }

    private AudioStream? Stream(Sfx id)
    {
        if (!_sfx.TryGetValue(id, out var stream))
            _sfx[id] = stream = Find($"res://audio/sfx/{id}", "ogg", "wav", "mp3");
        return stream;
    }

    private AudioStream? MusicStream(Track id)
    {
        if (!_music.TryGetValue(id, out var stream))
            _music[id] = stream = Find($"res://audio/music/{id}", "ogg", "mp3");
        return stream;
    }

    private static AudioStream? Find(string stem, params string[] extensions)
    {
        foreach (var ext in extensions)
            if (ResourceLoader.Exists($"{stem}.{ext}"))
                return GD.Load<AudioStream>($"{stem}.{ext}");
        return null;
    }
}
