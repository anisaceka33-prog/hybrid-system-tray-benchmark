# Measurement protocol

Use Release/x64 builds, fixed payloads prepared before timed bridge calls, explicit warm-up and measured iteration counts, and timestamped raw outputs. Record startup from process launch to the application-specific readiness marker; for hybrid this is the `frontend-ready` web message. Measure CPU over a fixed interval from CPU-time deltas, and retain both process rows and aggregate memory values.

The experiment is descriptive. Do not silently remove outliers, alter raw files, or infer causality from a single run. Document machine state, runtime versions, lifecycle mode, and any deviations.
