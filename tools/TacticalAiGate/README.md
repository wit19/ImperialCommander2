# TacticalAiGate — live Core gate (M8/F3, versioned from /tmp/setgate)

Fast Core-only gate for the world feed (seq-gap/resync, hash stability,
posture law, leak guard, replay determinism incl. OrderPlanner public line),
the M6 overlord layer (focus ranking, threat-economy mirror, risk positions)
and the M8 fixes (live round-limit re-derivation, logger raw-name extraction).
The NUnit editor suites are [Explicit] (no Unity test framework wired), so
this executable is the ONLY place these guarantees run today — keep it green
with every feed/overlord change.

Rebuild + run (no Unity needed, C# 8):

    dotnet run --project tools/TacticalAiGate/gate.csproj

Expect: `ALL PASS` (plus `WORLDGATE ALL PASS` / overlord lines), rc=0.
`bin/` and `obj/` are build output — never commit them.
