namespace RTSPView.Core;

// Input is already filtered and ordered nearest first. Keep a pair stable during refreshes.
public sealed class AircraftRotation
{
    private DateTimeOffset _changed;
    private int _page;
    private string[] _ids = [];
    public AircraftTrack[] Select(AircraftTrack[] nearby, int requestedCount, DateTimeOffset now)
    {
        var count = Math.Clamp(requestedCount, 1, 2);
        if (nearby.Length == 0) { _ids = []; _page = 0; _changed = default; return []; }
        if (_changed == default || now - _changed >= TimeSpan.FromSeconds(20))
        {
            _page = _changed == default ? 0 : (_page + 1) % (int)Math.Ceiling(nearby.Length / (double)count);
            _ids = nearby.Skip(_page * count).Take(count).Select(a => a.Hex).ToArray(); _changed = now;
        }
        var chosen = _ids.Select(id => nearby.FirstOrDefault(a => a.Hex == id)).OfType<AircraftTrack>().Take(count).ToList();
        foreach (var track in nearby)
            if (chosen.Count < count && chosen.All(a => a.Hex != track.Hex)) chosen.Add(track);
        _ids = chosen.Select(a => a.Hex).ToArray();
        return chosen.ToArray();
    }
}
