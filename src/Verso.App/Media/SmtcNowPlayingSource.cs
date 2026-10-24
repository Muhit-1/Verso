using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Verso.Core.Contracts;
using Verso.Core.Models;

namespace Verso.App.Media;

/// <summary>
/// Wraps <c>GlobalSystemMediaTransportControlsSessionManager</c> — the same
/// signal Windows' own media overlay (Win+G / the volume flyout) reads from.
/// It reports whatever Spotify, YouTube Music, a browser tab playing video,
/// or anything else that registers with the OS is doing, with zero
/// per-service API keys.
/// </summary>
/// <remarks>
/// Available because <c>Directory.Build.props</c> targets
/// <c>net10.0-windows10.0.26100.0</c> specifically for this — the WinRT
/// projection for <c>Windows.Media.Control</c> comes free with that target,
/// no extra package.
/// </remarks>
internal sealed class SmtcNowPlayingSource : INowPlayingSource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public NowPlaying? Current { get; private set; }
    public IReadOnlyList<QueueItem> Queue { get; private set; } = [];

    public event EventHandler<NowPlaying?>? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No SMTC on this machine/session for whatever reason — the
            // widget desk still has to start. Current stays null, which the
            // web layer already treats as "no host media, use the demo player".
            return;
        }

        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachToCurrentSession();
        await RefreshAsync();
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachToCurrentSession();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Windows exposes one "current" session — usually whichever app most
    /// recently started or changed playback — not a list to choose between.
    /// Good enough: a widget showing "now playing" for two things at once
    /// would not be more useful, just more confusing.
    /// </summary>
    private void AttachToCurrentSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionMediaChanged;
            _session.PlaybackInfoChanged -= OnSessionPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnSessionTimelineChanged;
        }

        _session = _manager?.GetCurrentSession();

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionMediaChanged;
            _session.PlaybackInfoChanged += OnSessionPlaybackChanged;
            _session.TimelinePropertiesChanged += OnSessionTimelineChanged;
        }
    }

    private void OnSessionMediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();
    private void OnSessionPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
    private void OnSessionTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_session is null)
        {
            SetCurrent(null);
            Queue = [];
            return;
        }

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            var playback = _session.GetPlaybackInfo();
            var timeline = _session.GetTimelineProperties();

            SetCurrent(new NowPlaying
            {
                Title = props.Title ?? string.Empty,
                Artist = props.Artist ?? string.Empty,
                Album = props.AlbumTitle ?? string.Empty,
                DurationMs = (long)timeline.EndTime.TotalMilliseconds,
                PositionMs = (long)timeline.Position.TotalMilliseconds,
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Art = await ExtractArtColoursAsync(props.Thumbnail)
            });

            // Most sources — Spotify included, most of the time — do not
            // expose an upcoming-tracks list through SMTC at all. Rather
            // than invent one, leave it empty; the web layer already
            // treats an empty queue as "nothing to show" correctly.
            Queue = [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A session mid-teardown (the app just quit, the tab just
            // closed) throws reading properties from it. Treat that the
            // same as no session at all rather than propagating into a
            // COM call from script.
            SetCurrent(null);
            Queue = [];
        }
    }

    private void SetCurrent(NowPlaying? value)
    {
        Current = value;
        Changed?.Invoke(this, value);
    }

    /// <summary>
    /// Two colours sampled from the thumbnail — a dark and a light variant
    /// of its average colour — rather than a real palette extraction. The
    /// widget only ever paints a two-stop gradient from these (see
    /// docs/host-api.md), so a proper dominant-colour algorithm would be
    /// work the result could never show.
    /// </summary>
    private static async Task<string[]> ExtractArtColoursAsync(IRandomAccessStreamReference? thumbnail)
    {
        var fallback = new[] { "#2E5F63", "#7FB7A8" };
        if (thumbnail is null)
        {
            return fallback;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 16, ScaledHeight = 16 },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var bytes = pixels.DetachPixelData();
            if (bytes.Length < 4)
            {
                return fallback;
            }

            long r = 0, g = 0, b = 0;
            var count = bytes.Length / 4;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                b += bytes[i];
                g += bytes[i + 1];
                r += bytes[i + 2];
            }
            r /= count; g /= count; b /= count;

            var dark = $"#{Shade(r, 0.55):X2}{Shade(g, 0.55):X2}{Shade(b, 0.55):X2}";
            var light = $"#{Shade(r, 1.35, 30):X2}{Shade(g, 1.35, 30):X2}{Shade(b, 1.35, 30):X2}";
            return [dark, light];
        }
        catch (Exception ex)
        {
            Hosting.Log.Warn("now playing: could not sample the artwork colours", ex);
            return fallback;
        }
    }

    private static byte Shade(long channel, double factor, double add = 0)
        => (byte)Math.Clamp(channel * factor + add, 0, 255);

    public async Task PlayAsync()
    {
        if (_session is not null)
        {
            await _session.TryPlayAsync();
        }
    }

    public async Task PauseAsync()
    {
        if (_session is not null)
        {
            await _session.TryPauseAsync();
        }
    }

    public async Task NextAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipNextAsync();
        }
    }

    public async Task PreviousAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipPreviousAsync();
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionMediaChanged;
            _session.PlaybackInfoChanged -= OnSessionPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnSessionTimelineChanged;
        }
    }
}
