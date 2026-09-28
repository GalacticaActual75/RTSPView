namespace RTSPView.Core;

public enum AircraftPhotoDecision { Waiting, Photo, WithoutPhoto }

// Once an encounter is displayed text-only, a late result must not pop into it.
public sealed class AircraftPhotoGate
{
    private readonly Dictionary<string, (DateTimeOffset Started, AircraftPhotoDecision Decision)> _encounters = [];
    public void Retain(IEnumerable<string> identities)
    {
        var current = identities.ToHashSet();
        foreach (var id in _encounters.Keys.Where(id => !current.Contains(id)).ToArray()) _encounters.Remove(id);
    }
    public AircraftPhotoDecision Decide(string identity, bool ready, bool failed, DateTimeOffset now, int seconds)
    {
        if (!_encounters.TryGetValue(identity, out var state)) state = (now, AircraftPhotoDecision.Waiting);
        if (state.Decision == AircraftPhotoDecision.Waiting)
            state.Decision = ready ? AircraftPhotoDecision.Photo : failed || now - state.Started >= TimeSpan.FromSeconds(seconds)
                ? AircraftPhotoDecision.WithoutPhoto : AircraftPhotoDecision.Waiting;
        _encounters[identity] = state;
        return state.Decision;
    }
}
