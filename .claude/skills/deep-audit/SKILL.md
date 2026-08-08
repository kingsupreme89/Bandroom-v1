---
name: deep-audit
description: Verify a "done" claim from Cline (or anyone) about a Bandroom task/feature before trusting it — rebuild the code, read the actual diff, and check one level past the change for consequences the report didn't mention. Use this whenever a task on TASK_BOARD.md is marked complete, a completion report is pasted in, or someone says a bug is fixed / a feature works, before updating the board or telling the user it's done. Also use proactively before relaying any "all tests pass" / "0 errors" / "fixed" claim about Bandroom.Core, the Windows app (BandAudioHook.csproj), or Bandroom.Mac.
---

# Deep Audit

A self-reported "done" is a claim, not a fact. This skill is the checklist for turning a claim into
something you can actually stand behind before it goes on the board or gets relayed to the user.

## Why this exists

On 2026-08-07, Cline's own `AUDIT_REPORT_2026-08-07.md` flagged a "bug" (`OnTackleForLoss` not
gated by the new engine flag) and recommended a fix. Reading one level past the flagged line —
the comment block at `WebMainForm.cs:844-847` — showed the owner had *explicitly* asked for that
function to stay ungated as an intentional exception. The recommended fix would have silently
broken a deliberate decision. The report wasn't lying, it just didn't look far enough. That's the
gap this skill closes: not catching dishonesty, catching incomplete verification.

## When to run this

Any time you're about to mark something ✅ on `TASK_BOARD.md`, or tell the user "confirmed" /
"verified" / "done," based on:
- A message pasted from Cline (or any other agent) claiming a task is complete
- An entry in the `## Cline → Orchestrator` log section of `TASK_BOARD.md`
- Your own change, before claiming it's finished

Skip it only for pure documentation/comment edits with no behavioral effect.

## The five checks

Work through these in order. Don't skip ahead because the report sounds confident — confidence
and correctness are independent, and the whole point is that self-reports are unverified.

### 1. Rebuild it yourself
Don't trust "0 errors" or "build succeeded" from the report. Run the actual build command for
whichever project(s) are affected:

```
dotnet build src/Bandroom.Core/Bandroom.Core.csproj
dotnet build BandAudioHook.csproj
dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj
```

If the report names a specific project path, verify that path actually exists first — reports have
been wrong about paths before (e.g. claiming `src/Bandroom/Bandroom.csproj` when the real file is
`BandAudioHook.csproj` at repo root). A build that can't even be located isn't verified, it's
unknown.

### 2. Read the actual changed code
Open the real file at the line numbers the report cites. Don't accept the report's *description*
of what changed as a substitute for reading it. Descriptions summarize; summaries drop context.

### 3. Check one level past the change
This is the step that catches what happened with `OnTackleForLoss`. For each changed function or
flag, look at:
- **Callers** — who invokes this, and does the change affect them the way the report assumes?
- **Related gates/flags** — is there a sibling flag or condition nearby that this change should
  have touched but didn't (or touched but shouldn't have)?
- **Adjacent comments** — comments near the change often encode a prior decision, a "why," or an
  explicit exception. If a comment contradicts what the report just changed or recommends changing,
  the comment wins until you can confirm otherwise with the user — don't assume the comment is
  stale.

If the report is itself a *recommendation* (like Cline's audit notes), this check applies to the
recommendation too, not just to changes already made: read the surrounding code before agreeing a
suggested fix is actually correct.

### 4. Check for regressions in adjacent behavior
A fix for bug A can break already-working behavior B if they share state, a flag, or a code path.
Skim the rest of the function/class the change lives in, not just the lines that changed. Ask: does
anything else in this file rely on the old behavior this change just altered?

### 5. Record any mismatch
If everything checks out, it's fine to mark it ✅ trusted. If you find a gap between what was
claimed and what's actually true — wrong file path, untested edge case, a recommendation that
contradicts existing intent, anything — write it down in the `## Cline → Orchestrator` section of
`TASK_BOARD.md` (or reply to the user directly if that's the more immediate channel), specifically
enough that someone reading it later doesn't have to redo the investigation:

```
- [HH:MM] <task # or report reference> — <what was claimed> vs <what's actually true>, with file:line
```

Silence on a mismatch is worse than a slow correction — the board is only useful if it's honest
about what's actually verified versus what's still just claimed.

## Worked example (keep this shape when you find something)

> Cline's `AUDIT_REPORT_2026-08-07.md` Note #3 recommended adding
> `if (_useEngineForEvents) return;` to `OnTackleForLoss`. Checking one level past the flagged
> line, `WebMainForm.cs:844-847` has an existing comment: the owner explicitly asked for TFL to
> keep firing on both sides as an intentional exception to home-only/engine gating, since that
> detection path is considered reliable. Applying Note #3 would silently break that. **Do not
> apply this recommendation** — flagged on the board instead of applied.
