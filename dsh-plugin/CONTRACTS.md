# Contract audit and isolated acceptance evidence

## Release metadata: 0.1.2 + GpuLcd 1.0.6

The historical audit/rework records below retain their original version/status statements. Current release metadata is plugin **0.1.2** in package.json and dsh.plugin.json, with Node **>=24** and DSH floor **>=0.2.1-rc.2**. Only DSH **0.2.1-rc.2** was actually audited; the old >=0.1.7-0 range is not supported evidence, and future DSH versions still require contract revalidation. Plugin 0.1.0 is no longer recommended because it truncates middleware next.

Plugin 0.1.2 requires **GpuLcd >=1.0.5** for atomic waiting recovery; GpuLcd 1.0.4 ignores the state extension and cannot provide that guarantee. Release preparation changes metadata/documentation/packaging only; accepted production src/lib and receiver hashes remain frozen. The standalone archive has top-level dsh-gpulcd-status, LICENSE and CONTRACTS, and contains the same plugin bytes as both host packages. These are the 0.1.2 release artifacts, validated offline only. Old version attachments remain unchanged; no live plugin installation/activation, app restart, or physical rendering validation is claimed. The following historical isolated-typecheck command installs TEMP dependencies and is not rerun during dependency-free release preparation.

Plugin 0.1.1 was packaged with an inject declaration this Cordis version does not support ({ optional: [...] } is read as a service named "optional"), so it never mounted. 0.1.2 declares inject as ['agents'] and adds a regression assertion; live mount was verified in an isolated headless profile: the plugin activated and emitted UserPromptSubmit/PreToolUse/SessionEnd to a test pipe.

## Scope

This scope statement applies only to the delegated plugin author: that author modified only this plugin directory. The overall coordinated change also includes a separately assigned receiver implementation by the output owner; those C# changes are outside this author’s write scope. No live profile, installed plugin, node_modules, real GpuLcdEvents pipe, hardware, launch/restart, installation, commit/push or publication was used. Read-only inspection of the installed app.asar and receiver source is not live plugin activation. Temporary compiler packages, npm cache and copied legacy code were confined to fresh system temp directories and removed afterward.

The repository is C:/Users/Sun_Local/Projects/igame-vulcan-lcd-tool. Early command/history tools retained an old default working directory despite the session cwd; all subsequent commands used explicit absolute workdir. The only early git error (128) concerned that old non-repository directory. No old-copy source file was modified.

## Actual installed contracts

Read-only source: C:/Users/Sun_Local/AppData/Local/Programs/DeepSeek Harness/resources/app.asar, internal prefix dsh/node_modules/@deepseek-ai/. Audited Cordis 4.0.5 and DSH modules 0.2.1-rc.2. Run node test/inspect-contracts.mjs for exact selected snippets, versions and SHA-256 hashes. Override DSH_CONTRACT_ASAR to inspect a different installation.

| Module / installed lib/index.js line | Observed contract | Consequence |
| --- | --- | --- |
| cordis:317–325 | waterfall consumes a callback queue, appends next, returns outer callback result | Every observer middleware must call next and preserve its result/error |
| dsh-tools:3321–3322 | await waterfall(carrier, tools/pre-execute, exec, () => Promise.resolve({kind:allow})); immediate gate.kind | undefined is fatal; forcing allow would bypass deny/ask/cancel |
| dsh-user-approval:176 | approval/request receives req,next; terminal returns unavailable; service maps cancellation separately | Forward result/error; remove wait token on abort even when provider remains pending |
| dsh-user-questions:681–685 | user-questions/request receives request,next; agent may be absent; terminal rejects NO_PROVIDER | No agent means no telemetry, but next still executes |
| dsh-agent:243–246; 549–554 | agent dispatcher injects agent into payload; disposed receives {agent} | Never treat disposed argument as a bare Agent |
| dsh-agent:614–616 | agents.list() returns registered agent instances | Initialize only their currently running instances |
| dsh-agent-loop:790–798 | status=idle for idle OR maintenance; emit status only on projection change | idle alone is not successful completion |
| dsh-agent-loop:1029–1032 | session.append(turn/end,{turn,reason:turnEnds}) | Completion requires reason.kind=completed, and this plugin also requires observing step/start to exclude empty wakes |
| dsh-session:1584–1592 | session/event callback arguments are session,event, with the exact Session instance | Match exact Session identity and numeric turn, not a fabricated {agent} payload |
| receiver Integrations.cs:128–147 (read-only) | Reads JSON line, checks size/id, queues BeginInvoke, writes OK newline | Full OK proves parsing and queued dispatch, NOT UI execution or physical rendering |

Representative module hashes:

- cordis: 6a9394c0877ff45218818c6e815edd038f8057e1a1deb390a8d43ec81c57691e
- dsh-tools: 1e53932e7c4213ea24a7d87f06960b7a7702cb21f8a5741c97242b4a470a442a
- dsh-agent-loop: cf57e3ecd487ac5c2553455aaa5cef2c6fb41e6bd61be5aa455675d1d7193033
- dsh-session: 2f5dd07cb5c744a29abbb8003a8a4cefd9e582c6e2538074ca299a0ddd02a3bf

These are runtime JS contracts, not claims that stripped declarations were typechecked. The installed archive omits Node type declarations. Old broad ambient shims were removed; local projections contain only consumed fields and named signatures. Strict standalone TypeScript checks use actual Node types in an isolated temporary environment.

## Red before green

Before replacing shipped lib, node --test dsh-plugin/test/contract.test.mjs exited **1**, **0/2 passing**:

~~~text
TypeError: Cannot read properties of undefined (reading 'kind')
AssertionError: Missing expected rejection.
~~~

The new regression extracts and executes the installed Cordis waterfall method. The callback registry is an explicit test adapter; no DSH process is started. It verifies the installed dsh-tools default allow and gate.kind call-site signature instead of silently accepting a fabricated terminal/default value.

Reproducible committed-legacy probe: from repository root run node dsh-plugin/test/legacy-regression.mjs. It reads HEAD's original lib files via git, copies them only to a fresh temp directory and invokes them with the installed waterfall. **Expected exit 1** (an intentional failure, not part of normal passing suites):

~~~text
legacy terminal nextCalls=0
TypeError: Cannot read properties of undefined (reading 'kind')
~~~

The same real-contract positive suite passes after the fix, checking allow/deny/ask/cancel object identity, tool Promise identity, terminal next execution, synchronous exception identity, async rejection identity and approval/question result identity.

## New verification commands

From repository root, with Node v24.19.0:

~~~bash
node dsh-plugin/build.mjs
node dsh-plugin/build.mjs --check
node --test dsh-plugin/test/contract.test.mjs dsh-plugin/test/index.test.mjs dsh-plugin/test/bridge.test.mjs
node dsh-plugin/test/typecheck.mjs
~~~

The first three use no installed development dependencies. The final strict noEmit check installs only typescript@5.9.3 and @types/node@24.10.1 plus the Node types' transitive undici-types inside one fresh temporary directory, including its npm cache, then removes it. No repository or profile node_modules are changed.

Coverage: real waterfall transparency (2 tests); lifecycle/concurrent waits/abort listener removal/idle and empty wake classification/actual completion/repeated turns/reloaded IDs/stale finally/disposed and reused session identity/heartbeat and telemetry rejection containment (11 tests); fake named pipe full/split OK, rejection/close/partial/oversized reply/deadline, FIFO single-flight, overflow, queue age, recovery, disposal, connect timeout, late errors, sync connect/write errors, serialization and payload bounds (9 tests). Fake pipe names always begin gpulcd-isolated-, never the real pipe. No old seven-test result is counted as acceptance.

The native TypeScript eraser emits an ExperimentalWarning; isolated npm currently emits a shell invocation deprecation warning on Windows. Neither is suppressed or confused with a typecheck failure. Build output is deterministic within this Node runtime; --check detects any src/lib mismatch.

## Initial verification results (superseded by Rework #1 below)

| Command | Actual result |
| --- | --- |
| node dsh-plugin/test/legacy-regression.mjs | Expected exit 1; nextCalls=0; exact reading-kind TypeError |
| node dsh-plugin/build.mjs | Exit 0; both shipped JS files regenerated |
| node dsh-plugin/build.mjs --check | Exit 0; exact src/lib match (repeated after final normalization) |
| node --test dsh-plugin/test/contract.test.mjs dsh-plugin/test/index.test.mjs dsh-plugin/test/bridge.test.mjs | Exit 0; 22 passed, 0 failed, 0 skipped/cancelled |
| node dsh-plugin/test/typecheck.mjs | Exit 0; strict noEmit with real Node types, repeated successfully after implementation changes |
| git diff --check -- dsh-plugin | Exit 0 after normalizing native eraser padding; only Git LF/CRLF informational warnings remain |

The initial all-suite rerun briefly reported 21/22 because an old test expected a rejected Promise when the strengthened middleware now correctly throws a synchronous downstream exception synchronously. The assertion was corrected to assert.throws; no production behavior was weakened. The fake named-pipe close test was also corrected to use destroy instead of Windows half-close semantics; both cases now have explicit passing coverage.

Manifest/package compatibility: the existing plugin ID dsh-external/dsh-gpulcd-status, export name dsh-gpulcd-status, version 0.1.0, entry ./lib/index.js, bundle row and patch file are unchanged. Package scripts and an explicitly conservative Node >=24 runtime/development requirement were added; no root version changed. Existing dsh >=0.1.7-0 metadata was retained, but **only the audited 0.2.1-rc.2 runtime contracts were validated**; that lower bound is not a claim of tested backwards compatibility. No package was installed or published under the unchanged version. A release/compatibility-metadata decision remains with the coordinator.

## Remaining risks / not claimed

- No live DSH profile activation, end-to-end desktop unload/HMR or hardware rendering validation has occurred. A later independently approved disposable-profile test is required before real installation.
- Existing waits and step/start events that began before plugin attachment cannot be reconstructed from agents.list(); conservatively, a success with no observed step is not shown as completed.
- Telemetry is bounded best-effort, not durable. Full queues, timeouts, unload and process death may lose transitions. Unacknowledged starts are reattempted at bounded heartbeat cadence using the atomic start field described below; normal state refresh rebuilds cleared receiver entries. This is conditional recovery, not unconditional next-heartbeat delivery; historical Stop events are not retried. Plugin disposal cancels pending delivery/resources; the receiver's own expiry handles a state whose final SessionEnd could not arrive.
- True public signatures may change in later DSH builds. The explicit real-contract suite should fail when its audited gate signature changes; it is not a substitute for full runtime compatibility certification.
- Colors, state priority, UI and model selection are unchanged. No release/version promotion was performed.

## Rework #1: lost start versus the production turn gate

The first implementation's unconditional “next heartbeat restores active state” claim was wrong: production LinkState.Apply retains an old entry (including closed entries) and rejects any different-turn non-start event. The review's real pressure case dropped 7 pending items, including the new UserPromptSubmit, leaving only PreToolUse that could never cross that gate. New receiver-model regression initially failed **17/17, exit 1** against that implementation. A draft wildcard SessionEnd-reset strategy was rejected because per-event TickLinks could project idle/client switching; it was fully removed and is not the accepted solution.

The coordinator approved a minimal backward-compatible state field and dispatched the receiver owner, not this plugin author, to implement it. Wire contract:

~~~json
{"client":"dsh","hook_event_name":"UserPromptSubmit","session_id":"...","turn_id":"...","state":"waiting"}
~~~

For DSH starts only: omitted/empty/working means legacy working; waiting atomically means waiting; invalid nonempty values are rejected. Other clients ignore the extension. **Old GpuLcd 1.0.4 ignores this field and cannot guarantee no working intermediate state when recovering waiting. The plugin and next receiver must be delivered together after separate authorization.**

Accepted recovery invariants:

1. One start-ACK bit and at most one in-flight start per exact Turn object. A failed/dropped/expired start becomes eligible at the next 15-second heartbeat. There is no internal retry loop, extra timer or accumulated retry queue. An indefinitely active offline session can continue bounded-rate advertisements; it does not get a durable-delivery guarantee. End/dispose stops them.
2. ACKed starts never repeat on healthy heartbeats: no periodic lease, wildcard reset, unnecessary new-turn event, artificial idle, or client-hold reset.
3. Both original and replacement starts are guarded event suppliers. At socket connect immediately before JSON write they re-check exact Turn identity and read the latest waits set. An obsolete generation yields undefined and writes nothing. A late old ACK/finally cannot confirm or mutate a new generation.
4. Receiver restart or disabling the source clears its entry via the existing Clear/ClearClient path; normal current-state heartbeats can repopulate an empty entry even after an earlier start was ACKed. OK still proves reception/queueing only. ACK-without-Apply while retaining an old gate is unobservable to this protocol and not claimed recoverable; no periodic forced start is used to hide it.
5. Permanent network failure, sustained queue overload, receiver bugs or process exit can defeat best-effort delivery. No terminal Stop replay or cross-generation recovery occurs.

Recovery evidence adds a fresh verbatim C# receiver check, not only a translated mock. test/real-receiver.mjs copies the exact current production LinkState.cs into a new TEMP directory, compiles it with a tiny console harness, reads the captured fake-pipe JSON, calls Apply then Current after EVERY event, and deletes that directory. No installed app, UI, real pipe or hardware is opened. Source SHA-256 during accepted recovery verification:

~~~text
30cddfaae197bd24cdb378860dd5fe584407206781e9f9170b373c87853d4fe5
~~~

Eight real-transport/C# cases cover overflow/TTL × old closed/working × new working/waiting. Overflow uses the production default 64 pending limit, a held unrelated in-flight ACK and 70 downstream-denied tool callbacks: exactly 7 items drop, no new start reaches the receiver before recovery, and downstream deny object identity remains intact. For every captured protocol event the real Current projection never enters artificial idle; after recovery it never lowers waiting to working. A newer Claude same-priority candidate confirms DSH remains held, and reflection of the real private clientSince field verifies its value never resets across healthy recovery frames. Each fresh receiver process exits 0.

Additional recovery-model cases cover connection failure, start expiry/overflow, old working/closed gates, waiting/working, restart/source toggle ClearClient, acknowledged-start no-replay, one-in-flight bounds, stale supplier and stale ACK. One bridge test explicitly changes working to waiting while awaiting connect and verifies waiting is serialized only at actual write; stale supplier produces zero writes. Normal next/approval/question semantics remain under the original real-waterfall regressions.

Package correction: files now includes CONTRACTS.md, so the README link exists in the declared distributable. Node >=24 is intentionally a supported runtime AND development baseline (desktop host 24.21.0 and system test runtime 24.19.0), not a misleading build-only claim. The plugin ID, entry, version and bundle registration remain unchanged and no installation/publication was performed.

### Rework #1 verification (superseded by Rework #2 below)

From the absolute plugin working directory, the combined command below completed with **exit 0**:

~~~bash
node build.mjs && node build.mjs --check && node test/typecheck.mjs && node --test test/index.test.mjs test/bridge.test.mjs test/contract.test.mjs test/recovery.test.mjs test/recovery-pipe.test.mjs && git diff --check -- .
~~~

Results: source/lib exact match; strict TypeScript noEmit passed using only fresh TEMP dependencies/cache (gpulcd-plugin-types-L9HCRH, removed afterward); **46 tests passed, zero failed/skipped/cancelled**. This is the original 22 safety tests plus one deferred-serialization bridge test, 15 recovery-model tests and 8 real-transport/verbatim-C# tests. All 8 freshly compiled pure receiver executables exited 0. The default-queue pressure cases assert exactly dropped=7 and unchanged deny identity. There are no active test jobs/processes or live installation steps.

Only plugin files were written by this author. Receiver source changes implementing the coordinator-approved atomic field belong to the separately dispatched output owner; this author only read/copied/compiled that source into independent TEMP directories. Historical red runs and the rejected wildcard draft are documented honestly above, but the accepted implementation contains **no wildcard SessionEnd resynchronization and no periodic start lease**. CONTRACTS.md is now included in package.files. All implementation and generated outputs are frozen for independent re-review.

## Rework #2: all queued active states must be current at delivery

Independent re-review disproved the first rework's no-downgrade claim when the initial connection itself was delayed without any loss: although the lazy start correctly serialized waiting, a previously enqueued static PreToolUse could still arrive next and project working before the queued permission event. A reverse answered-wait case could similarly reenter waiting after its answer.

Before changing production code, the new no-loss delayed-first-connect test suite produced **0/5 passing, exit 1**, using actual createBridge + attach, random fake named pipes and freshly compiled verbatim production LinkState.cs. Exact real receiver projections:

~~~text
working enqueued before approval: waiting -> working -> waiting
waiting enqueued before answer:  working -> waiting -> working
~~~

The red suite also caught stale active snapshots reviving an ended generation, old queued refreshes reaching a replacement generation, and wrong intermediate cross-session projection. No queue overflow/expiry was involved (dropped=0).

The fix makes **ordinary refreshes lazy and generation-guarded too**, not only starts. Every active working/waiting heartbeat/tool/wait-state observation enters the bounded FIFO as a supplier. Immediately before socket write, it validates exact active Turn identity plus plugin lifetime and reads that Turn's current waits. Invalid old generations yield undefined and produce zero writes. Consequently both stale directions are corrected, and different sessions cannot borrow each other's waits. Actual lifecycle Stop/SessionEnd events remain fixed to their original turn and retain FIFO finalization semantics; this change does not turn them into dynamic observations.

The same real-receiver delayed-first-connect tests then pass with:

~~~text
working enqueued before approval: waiting -> waiting -> waiting
waiting enqueued before answer:  working -> working -> working
~~~

A sixth finalization case additionally confirms that a completed turn whose active snapshots became obsolete delivers only its legitimate Stop, leaving the actual receiver closed/done, never resurrected by a late start/work/wait event. The original lifecycle/end/replacement tests also remain in the full suite. README and package scripts now cover this queued-state suite; package.files still includes CONTRACTS.md. No receiver source changes were made by the plugin author for Rework #2.

### Rework #2 final frozen evidence

- New delayed-first-connect regression: red 0/5, exit 1, followed by green 5/5, exit 0. A sixth explicit completed-Stop case was then added and passes.
- Full final command from the absolute plugin directory:

~~~bash
node --test test/index.test.mjs test/bridge.test.mjs test/contract.test.mjs test/recovery.test.mjs test/recovery-pipe.test.mjs test/queued-state.test.mjs && node build.mjs --check && git diff --check -- .
~~~

Actual exit **0**: **52 tests passed**, zero failed/skipped/cancelled; src/lib match; whitespace check clean. This retains the earlier 46 cases and adds six ordinary-delay/no-loss snapshot cases. The first combined rerun briefly reported 51/52 because a start-only fake sender counted every newly lazy refresh function as a pending start; the mock was corrected to classify by its hook event name. No production guarantee was weakened.

- node test/typecheck.mjs independently completed with exit **0** against the final production source, using TEMP/gpulcd-plugin-types-pahjbb for pinned dependencies and cache; that directory was removed. Only the fake test accounting/documents changed afterward.
- All 14 actual C# projection probes (eight recovery-pressure plus six queued-state cases) freshly compile the verbatim current receiver source hash 30cddfaae197bd24cdb378860dd5fe584407206781e9f9170b373c87853d4fe5 in independent temporary directories; all receiver processes exit 0.
- No additional C#/Vue/output file was edited by the plugin author. The scope preface now explicitly distinguishes plugin-author writes from the coordinated receiver-owner changes. Implementation/generated output are frozen pending coordinator independent acceptance, not declared live-enabled or independently accepted by this author.
