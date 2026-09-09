# Beta opacity renderer checks

Run on an interactive Windows desktop with the bundled LibVLC version:

```powershell
dotnet run --project tests/SpotMonitor.OpacityChecks/SpotMonitor.OpacityChecks.csproj -c Release -- --auto
```

The test generates moving red and blue Y4M videos; no camera access or credentials are needed. It compares Direct3D11, GDI, and the production-used CompositedVideoPresenter. Pixel samples come only from known test viewport locations. The native renderers intentionally fail fractional alpha; only compositor and lifecycle failures fail the test process. A missing/occluded desktop must not count as success. Results are written beside the test executable.

Recorded local results are in opacity-beta3-results.json. All new-compositor checks passed: 20/50/100 percent opacity, ellipse clipping, hide/show, stop/restart with a different color stream, and snapshot capture. Example: the 50 percent red-over-blue pixel was RGB(128,2,127), and 20 percent was RGB(53,3,204).

The test does not measure production RTSP decoding performance. Beta uses software decoding for Doorbell and Garage, preserves their source pixel resolution, and presents the latest available frame at up to 30 FPS without a queued-frame backlog. Validate CPU, smoothness, native/custom shapes, framing, and reconnects on the Windows camera host. Main cameras retain native hardware playback.
