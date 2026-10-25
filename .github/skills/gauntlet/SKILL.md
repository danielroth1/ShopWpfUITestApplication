---
name: gauntlet
description: "Run the ShopTest gauntlet loop until every gate and UI scenario is green"
disable-model-invocation: true
---
Run the ShopTest gauntlet loop.

Goal: ${input:goal:walking skeleton green}

Instructions:

1. Read `.github/agents/gauntlet.agent.md` and follow its loop protocol exactly.
2. Verify the toolchain once with `scripts\gauntlet.ps1 -Gate preflight`; if it fails, report the
   remediation instead of improvising.
3. Iterate at most 3 times: deterministic gates first, then the Windows-MCP UI scenarios in
   `ui-tests/scenarios/`.
4. Do not modify scenario assertions, seeds, or tests to obtain a green run.
5. Finish with the gate table, scenario table, and evidence paths under `artifacts/gauntlet/iter-<n>/`.
