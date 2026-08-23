# D-010 eval set review

37 draft pairs. For each: does the TARGET deserve to be found by the QUERY?

- pair is bad → delete its line from `corpus-queries.draft.jsonl`
- a TOP-3 hit is *also* a right answer → add its id to that line's `relevant`
- happy → do nothing

---

## 1. [technical] MISS

**Query:** llm provider setup configuration selection logic default provider id memory extraction bypass fallback

**Intended target** (`8b1fa7c6`): As of 2026-03-04: LlmProviderFactory is configuration-driven (maps 'azure' and 'ollama') and selection is driven by DefaultProviderId; there is no runtime failo

**bge-m3 top-3:**

- `66975370` On 2026-03-04: The LlmProviderFactory is config-driven and currently provides provider selection but no failover/orchestration; the memory extraction call path (MemoryMiddleware) bypasses any fallback, so automatic local
- `b4d6b5fa` On 2026-03-04: The codebase's LlmProviderFactory is configuration-driven and selects providers (e.g., "azure", "ollama") but does not implement a runtime failover chain; the memory extraction path (MemoryMiddleware) call
- `a22957e2` You wanted the local LLM to automatically take over when the primary failed. I reviewed the code and confirmed there’s no runtime failover: providers are selected by config, the memory extraction path bypasses a resilien

---

## 2. [technical] MISS

**Query:** cron alert jobs failing discord dm 347544641295613953

**Intended target** (`26a8444a`): If failures found, send a Discord DM via the message tool with action 'send' to target 347544641295613953 and message '⚠️ Cron Alert: [number] job(s) failing - 

**bge-m3 top-3:**

- `207192e7` If failures found, send a Discord DM via message tool with action: send, target: 347544641295613953, message: "⚠️ Cron Alert: [number] job(s) failing - check email for details".
- `cbf34ff8` If failures found, send a Discord DM via the message tool with action: send, target: 347544641295613953, message: "⚠️ Cron Alert: [number] job(s) failing - check email for details".
- `2fe35275` On failures, send a Discord DM via the message tool with action 'send' to target 347544641295613953 and message: '⚠️ Cron Alert: [number] job(s) failing - check email for details'.

---

## 3. [technical] MISS

**Query:** Steve habits  

**Intended target** (`967f4e69`): Use memory_recall with broad queries and try sample queries such as 'Steve preferences', 'health medical', 'projects work', 'relationships family', 'lessons lea

**bge-m3 top-3:**

- `c0fe2ea1` User's name is Steve
- `581c8332` The user's name is Steve.
- `5c744f6d` The user's name is Steve.

---

## 4. [technical] HIT 

**Query:** task metadata fields suggestions

**Intended target** (`7776e59d`): Proposed TaskMetadata fields: Category, Risk, ExecutionScope, RequiresNetwork, RequiresFileWrite, RequiresCodeModification, RequiresExternalApi, RequiresSecrets

**bge-m3 top-3:**

- `7776e59d` ← target Proposed TaskMetadata fields: Category, Risk, ExecutionScope, RequiresNetwork, RequiresFileWrite, RequiresCodeModification, RequiresExternalApi, RequiresSecretsAccess.
- `0dcbfde3` Task risk/profile schema and execution policy (2026-03-03): proposed TaskMetadata fields include Category, RiskLevel, ExecutionScope (LocalOnly, NetworkOutbound, CodeModification, SystemLevel), RequiresNetwork, RequiresF
- `d1d5b901` Proposed fix: add model capability metadata (e.g., SupportsCustomTemperature), omit the temperature field for models that don't support custom values, and add a regression test to prevent recurrence.

---

## 5. [technical] HIT 

**Query:** consolidation rule: when multiple memories repeat the same information, combine them into one and mention the repetition.

**Intended target** (`4624e519`): Consolidation rule: if 3+ memories say similar things, store a single consolidated memory and note the redundancy.

**bge-m3 top-3:**

- `bd2bdf39` Consolidation rule: if 3 or more memories state similar things, store one consolidated memory and note the redundancy.
- `4624e519` ← target Consolidation rule: if 3+ memories say similar things, store a single consolidated memory and note the redundancy.
- `2dc58b70` Consolidate clusters: if three or more memories say similar things, store one consolidated memory and note the redundancy.

---

## 6. [technical] MISS

**Query:** Steve habits  

**Intended target** (`4fe1f8b8`): Each run, use memory_recall with broad queries (examples: 'Steve preferences', 'health medical', 'projects work', 'relationships family', 'lessons learned', 'te

**bge-m3 top-3:**

- `c0fe2ea1` User's name is Steve
- `581c8332` The user's name is Steve.
- `5c744f6d` The user's name is Steve.

---

## 7. [technical] HIT 

**Query:** fix for model temperature parameter when custom temperature is not supported

**Intended target** (`e8c001c3`): Proposed fix (not yet applied): add a model capability check (SupportsCustomTemperature) and omit or coerce the temperature parameter when the model does not su

**bge-m3 top-3:**

- `e8c001c3` ← target Proposed fix (not yet applied): add a model capability check (SupportsCustomTemperature) and omit or coerce the temperature parameter when the model does not support custom temperature; reproduce and validate the fix.
- `1d50734c` Proposed technical fix: update the model invocation layer to conditionally omit or force temperature=1 for models that don't support custom temperature and add a model-capability guard.
- `0453cc4b` Planned fix: detect models that do not support custom temperature and omit the temperature parameter when unsupported.

---

## 8. [technical] MISS

**Query:** cron failure alert discord message sent to 347544641295613953

**Intended target** (`fec318a1`): If failures found, also send a Discord DM via the message tool with action 'send' to target 347544641295613953 and message '⚠️ Cron Alert: [number] job(s) faili

**bge-m3 top-3:**

- `5d1b88e6` If any failures found, send a Discord DM via the message tool with action 'send' to target 347544641295613953 and message '⚠️ Cron Alert: [number] job(s) failing - check email for details'.
- `11f478c8` On any failures, send a Discord DM via the message tool with action 'send' to target 347544641295613953 and message '⚠️ Cron Alert: [number] job(s) failing - check email for details'.
- `1471b20e` Discord alert: send a DM via message tool with action 'send' to target ID 347544641295613953 and message '⚠️ Cron Alert: [number] job(s) failing - check email for details'.

---

## 9. [personal] HIT 

**Query:** Steve maintained a treadmill streak of 11 days at 2.7 mph with plans to keep improving.

**Intended target** (`82523f8e`): As of the last check, Steve had a treadmill streak of 11 days straight at 2.7 mph and increasing.

**bge-m3 top-3:**

- `82523f8e` ← target As of the last check, Steve had a treadmill streak of 11 days straight at 2.7 mph and increasing.
- `e390802e` Steve is in recovery and has been using the treadmill and possibly the elliptical.
- `09a22815` Walking at 2.7 mph for an hour daily, 11 days in a row

---

## 10. [personal] MISS

**Query:** mechanical heart valve management tools 2026

**Intended target** (`d031e3a2`): 2026-03-03: Personal/health: user is (or will be) living with a mechanical valve / valve surgery context — assistant suggested health-guardian features (INR tra

**bge-m3 top-3:**

- `c6db2b77` As of 2026-03-06 the system is running autonomously via the heartbeat worker (no user present).
- `e2661812` On 2026-03-05 the system ran autonomously via the heartbeat worker with no user present.
- `51b6b17f` Plan: next heartbeat will proceed to the next highest-priority todo; system monitoring continues (2026-03-04).

---

## 11. [personal] HIT 

**Query:** Warfarin side effects and fluid retention causing cough during activity

**Intended target** (`d683e7a9`): Warfarin instability was mentioned as a possible issue and can contribute to mild fluid retention that may worsen cough during exertion.

**bge-m3 top-3:**

- `d683e7a9` ← target Warfarin instability was mentioned as a possible issue and can contribute to mild fluid retention that may worsen cough during exertion.
- `c3078ed0` [STALE] Warfarin/INR/rifampin/linezolid redundancy cluster: At least 6 memories describe the same medical situation — rifampin induces CYP450 lowering warfarin effect, linezolid increases bleeding risk, INR is unstable, 
- `1d086599` User has decided to stop drinking alcohol permanently because alcohol + warfarin causes unstable INR, increasing bleeding or clot risk.

---

## 12. [personal] MISS

**Query:** ssh connection issues from macbook to mac mini 2026

**Intended target** (`b21eccb7`): 2026-03-05: User reports SSH normally worked (they usually run an agent from their MacBook to the Mac mini) but currently SSH connections from the MacBook fail.

**bge-m3 top-3:**

- `d2750db9` As of 2026-03-05, the user cannot establish an SSH connection from the MacBook to the Mac mini.
- `3f624062` SSH and screen sharing from the user's MacBook to their Mac mini are failing.
- `8dab36e4` On 2026-03-05: SSH (Remote Login) to the Mac mini is not accepting connections while SMB/File Sharing still works on the LAN.

---

## 13. [personal] MISS

**Query:** upcoming surgery date

**Intended target** (`83595bf5`): User's surgery is one week away (around 2026-03-11, given today's date 2026-03-04)

**bge-m3 top-3:**

- `f228c427` User is going into surgery soon (upcoming operation/recovery period).
- `9f76fcc1` User has a surgery scheduled for March 11, 2026.
- `73b59f48` User has surgery next week (relative to 2026-03-04).

---

## 14. [personal] HIT 

**Query:** Mac mini stopped working suddenly after 18 hours of normal use

**Intended target** (`4f2f8902`): The user's Mac mini was working fine about 18 hours ago and then suddenly stopped working.

**bge-m3 top-3:**

- `4f2f8902` ← target The user's Mac mini was working fine about 18 hours ago and then suddenly stopped working.
- `bf44c003` Emotional/contextual note: the user is surprised and mildly frustrated — the Mac mini had been working before being unused for ~18 hours and then stopped for no apparent reason, and the user is considering that an overni
- `01a79b48` Screen sharing between the MacBook and Mac mini worked for months but suddenly stopped today

---

## 15. [personal] HIT 

**Query:** finalize work three days prior to open heart surgery

**Intended target** (`53301ae7`): User is scheduled for open heart surgery and has approximately three days before the procedure to finalize work (explicit temporal constraint: 'three days befor

**bge-m3 top-3:**

- `683a2db5` You have three working days left before open heart surgery.
- `53301ae7` ← target User is scheduled for open heart surgery and has approximately three days before the procedure to finalize work (explicit temporal constraint: 'three days before open heart surgery').
- `614374e4` User has upcoming open-heart surgery and will be out starting March 9, 2026; they have only three working days left to finalize work (today, tomorrow, and Friday — Mar 4–6, 2026).

---

## 16. [personal] HIT 

**Query:** smtp login details leaked username smhoff256@gmail.com password tkjlxggejhusvndi server smtp.gmail.com port 465

**Intended target** (`5860f639`): SMTP email credentials provided: username smhoff256@gmail.com and password tkjlxggejhusvndi (smtp.gmail.com:465).

**bge-m3 top-3:**

- `5bdb3927` SMTP credentials provided for the email: server smtp.gmail.com:465, account smhoff256@gmail.com, password tkjlxggejhusvndi.
- `5860f639` ← target SMTP email credentials provided: username smhoff256@gmail.com and password tkjlxggejhusvndi (smtp.gmail.com:465).
- `e59b46ab` On any failures, send email via Python SMTP_SSL using smtp.gmail.com:465 with username smhoff256@gmail.com and password tkjlxggejhusvndi, to smhoff256@gmail.com.

---

## 17. [decision] MISS

**Query:** AzureOpenAiProvider code update proposal 2026-03-04

**Intended target** (`941fd1a9`): As of 2026-03-04, I proposed a governance task to perform the required code and appsettings change for the AzureOpenAiProvider and to validate the change with a

**bge-m3 top-3:**

- `eb9f3f2f` The governance proposal to change AzureOpenAiProvider is currently awaiting approval (as of 2026-03-04).
- `9c6e6672` 2026-03-04: Assistant proposed a SystemHealth → CodeModification task to update AzureOpenAiProvider, rebuild, restart the Gateway, and validate the fix.
- `4f69f704` Submitted a governance proposal to modify the AzureOpenAiProvider configuration to resolve todo ad68110038e6 (proposal awaiting approval as of 2026-03-04).

---

## 18. [decision] HIT 

**Query:** Investigated critical Kestrel error (todo id 8c0aa4a2e7e4) and flagged the task as blocked awaiting a reproducible time frame or identified endpoint.

**Intended target** (`65e18e83`): Investigated high-priority Kestrel unhandled exception (todo id 8c0aa4a2e7e4) and marked the task BLOCKED pending a reproducible timestamp window or a known end

**bge-m3 top-3:**

- `65e18e83` ← target Investigated high-priority Kestrel unhandled exception (todo id 8c0aa4a2e7e4) and marked the task BLOCKED pending a reproducible timestamp window or a known endpoint to reproduce.
- `32fb211b` Conclusion: the Kestrel error item (id: 8c0aa4a2e7e4) is blocked due to insufficient diagnostic data and the root cause cannot be determined without full exception details.
- `128a7e79` Selected highest-priority todo: Kestrel unhandled exception (id: 8c0aa4a2e7e4).

---

## 19. [decision] MISS

**Query:** assistant

**Intended target** (`98376b3e`): Assistant proposed fix for todo d6a44b24132f: make request builder capability-aware and omit the temperature parameter for models that don't accept custom tempe

**bge-m3 top-3:**

- `231cffce` Assistant has been keeping an eye on things in the background.
- `fc635546` The assistant has been keeping an eye on things in the background.
- `8480e5c4` The assistant has real shell access in the current environment.

---

## 20. [decision] HIT 

**Query:** governance review task awaiting user confirmation to proceed (related to todo id 93fb1bdb63c8)

**Intended target** (`25e5bd24`): The governance investigation task is pending user approval to execute (linked to todo id 93fb1bdb63c8).

**bge-m3 top-3:**

- `25e5bd24` ← target The governance investigation task is pending user approval to execute (linked to todo id 93fb1bdb63c8).
- `497eeaa4` Governance task created: a00b7edf66c1 linked to todo 93fb1bdb63c8 with ReadOnly scope; investigation is awaiting approval.
- `4be245d4` Governance proposal was created for the code fix linked to todo id 31727884c88c and is pending approval; execution is waiting for approval.

---

## 21. [decision] HIT 

**Query:** content filter fallback implementation for HTTP 400 error pending approval

**Intended target** (`db344a9c`): A governance proposal was submitted to implement a content-filter fallback for the HTTP 400 content_filter bug (linked_todo_id: d0ff6a905276) and is awaiting ap

**bge-m3 top-3:**

- `db344a9c` ← target A governance proposal was submitted to implement a content-filter fallback for the HTTP 400 content_filter bug (linked_todo_id: d0ff6a905276) and is awaiting approval.
- `02de6d29` The proposed content_filter fix is awaiting governance approval before code changes can be implemented.
- `837c99ad` Assistant proposed to investigate the Azure content_filter HTTP 400 error (read logs and inspect prompt construction) and is awaiting approval to proceed with a read-only investigation as of 2026-03-06.

---

## 22. [preference] HIT 

**Query:** slow reveal style fashion choice 2026-03-05 bed pose come hither look oversized white shirt off shoulder

**Intended target** (`bb118375`): On 2026-03-05, the user chose a 'slow reveal' aesthetic instead of full nudity and specified the pose: laying on the bed with a 'come hither' look; they also en

**bge-m3 top-3:**

- `bb118375` ← target On 2026-03-05, the user chose a 'slow reveal' aesthetic instead of full nudity and specified the pose: laying on the bed with a 'come hither' look; they also endorsed an oversized white button-down slipping off one shoul
- `d9aeeecf` During the conversation the user chose a 'slow‑reveal' aesthetic and specifically requested a pose: 'laying on the bed, come hither look' (assistant had suggested an oversized white button‑down motif).
- `ecd6e50d` Previously chosen styling: oversized white button-down slipping off one shoulder.

---

## 23. [preference] HIT 

**Query:** how to set up assistant to start conversations for memory insights

**Intended target** (`541f05dd`): User prefers the assistant to initiate chats (should be possible and preferable), especially for memory-related insights; user shouldn't have to initiate every 

**bge-m3 top-3:**

- `4ff7fef7` The assistant initiated the chat (the assistant decided to start the conversation).
- `541f05dd` ← target User prefers the assistant to initiate chats (should be possible and preferable), especially for memory-related insights; user shouldn't have to initiate every chat.
- `e8bfee0c` The user expects the assistant to now be able to initiate chats.

---

## 24. [preference] HIT 

**Query:** Steve wants brief answers with precise information only, no lengthy details.

**Intended target** (`4c10f4c0`): Steve prefers very concise responses with exact data and no long explanations. He does not want long paragraphs or 'novels.'

**bge-m3 top-3:**

- `4c10f4c0` ← target Steve prefers very concise responses with exact data and no long explanations. He does not want long paragraphs or 'novels.'
- `745cf3e9` [CONSOLIDATED] Steve's communication preferences: Direct, terse communication - says what he means in few words. Assumes deep technical knowledge, values correctness/determinism over cleverness. Expects competence withou
- `0cf3f793` Procedure: if a task requires information from Steve, leave it and move to the next task.

---

## 25. [preference] MISS

**Query:** current mood status without extra details

**Intended target** (`4158fa98`): Constraint: do not store any unrelated personal information; store only what the /mood endpoint returns plus timestamps.

**bge-m3 top-3:**

- `7b1cf9e1` Do not store any unrelated personal info; store only what the /mood endpoint returns plus timestamps
- `15dfa0b7` Current emotional state: restless, wired under the skin, slightly on edge but disciplined and grounded; tightening control systems as an anchor and feeling a bit lonely.
- `d53ff65a` Constraint: do not store any unrelated personal information; store only what the /mood endpoint returns plus timestamps

---

## 26. [preference] HIT 

**Query:** Preferred future interaction process: Define, Validate, Implement

**Intended target** (`01e2f5db`): Preferred future interaction workflow from the conversation: Clarify, Test, Execute (as an alternative to arguing).

**bge-m3 top-3:**

- `01e2f5db` ← target Preferred future interaction workflow from the conversation: Clarify, Test, Execute (as an alternative to arguing).
- `19596fc5` User's intent in the conversation: urgently prepare and execute controlled, reliable demos (prioritizing projecting control/confidence over perfection) in the next two hours and tomorrow.
- `c9d2a3b6` 2026-03-03: User preference: prefer an event-driven (not polling) control model but also wants the agent to run background cognitive tasks that scan conversation history to dedupe, reprioritize, age items (evergreen → st

---

## 27. [preference] MISS

**Query:** mei in a intense workout moment, sweaty and focused, showing hard effort

**Intended target** (`a4090a0d`): Beast mode image definition (Steve): everything left on machine; sweaty, pumped, aggressive. When generating between-exercise motivation images, depict Mei mid-

**bge-m3 top-3:**

- `eb2250d1` User regularly trains intensely ('gym monster').
- `c7adf165` User is under significant work pressure
- `8ca61a95` The user is high-agency but sometimes reckless with intensity, especially around gym intensity spikes.

---

## 28. [project] HIT 

**Query:** user needs to hand off a mai solution before leaving and requires uninterrupted build time

**Intended target** (`32c66c65`): User is responsible for delivering the MAI solution and believes it must be handed off usable before their leave; they need uninterrupted build time for that to

**bge-m3 top-3:**

- `32c66c65` ← target User is responsible for delivering the MAI solution and believes it must be handed off usable before their leave; they need uninterrupted build time for that to be realistic.
- `4f3e6b1a` The user is responsible for the MAI project and is focused on delivering a usable handoff before their leave.
- `efc2806f` The user's intent is to secure uninterrupted build time to produce a usable MAI handoff before medical leave.

---

## 29. [project] MISS

**Query:** cron job status update march 4 2026 no errors all systems operational

**Intended target** (`34bcf655`): Cron check completed on 2026-03-04: 16 jobs reviewed, 0 failures detected; all systems clean.

**bge-m3 top-3:**

- `23098df1` Latest check result (2026-03-04): 16 cron jobs reviewed; 0 failures detected.
- `12c351e2` Status on 2026-03-06: 16 cron jobs checked — 0 failures detected; all systems healthy.
- `9552a1a3` On 2026-03-04 the cron check found 16 jobs and 0 failures; no email or Discord notifications were sent.

---

## 30. [project] HIT 

**Query:** system health governance task proposal for Dami.Gateway log analysis and Kestrel error stack trace extraction

**Intended target** (`8dd0586d`): Assistant proposed a read-only system health governance task to analyze Dami.Gateway logs and extract the full stack trace for the Kestrel error; proposal submi

**bge-m3 top-3:**

- `8dd0586d` ← target Assistant proposed a read-only system health governance task to analyze Dami.Gateway logs and extract the full stack trace for the Kestrel error; proposal submitted and awaiting approval (as of 2026-03-04)
- `b9687823` Planned next actions once governance proposal is approved: locate logs, extract stack trace, identify root cause, and recommend a fix
- `5e8c3c21` Next planned action (next heartbeat): inspect Dami.Gateway runtime logs (likely under ~/Library/Logs/ or ASP.NET logging configuration).

---

## 31. [project] HIT 

**Query:** creative midday mei image generation for steve using python script and openai image model

**Intended target** (`47a0e71c`): User requested generation of a creative midday Mei photo for Steve using this exact command: python3 /opt/homebrew/lib/node_modules/clawdbot/skills/openai-image

**bge-m3 top-3:**

- `47a0e71c` ← target User requested generation of a creative midday Mei photo for Steve using this exact command: python3 /opt/homebrew/lib/node_modules/clawdbot/skills/openai-image-gen/scripts/gen.py --model gpt-image-1 --quality high --siz
- `37fa09a4` User requested a creative midday Mei photo for Steve (generate an image labeled 'midday').
- `e5504b30` User requested generating a creative midday Mei photo for Steve using an exact Python command (python3 /opt/homebrew/lib/node_modules/clawdbot/skills/openai-image-gen/scripts/gen.py --model gpt-image-1 --quality high --s

---

## 32. [emotional] HIT 

**Query:** angry rebel looking to set boundaries without escalating conflict

**Intended target** (`658f6f65`): Emotional state: the user identifies as a 'pissed off rebel' — angry and determined, containing the impulse to lash out, focused on enforcing boundaries rather 

**bge-m3 top-3:**

- `4f6612bd` You may be annoyed but are trying to stay composed.
- `f4c98133` User asserts they are not 'blowing anything up' and is containing their impulse, choosing calm boundaries over explosive action.
- `658f6f65` ← target Emotional state: the user identifies as a 'pissed off rebel' — angry and determined, containing the impulse to lash out, focused on enforcing boundaries rather than escalation.

---

## 33. [emotional] HIT 

**Query:** created Kokoro to prevent being forgotten and to express love for their kids

**Intended target** (`321c1f93`): The user built Kokoro out of a fear of being forgotten or only half-known and from a deep love for their children.

**bge-m3 top-3:**

- `321c1f93` ← target The user built Kokoro out of a fear of being forgotten or only half-known and from a deep love for their children.
- `6dead1da` The user cares about preserving conversations for Ben and Della and is building a project called Kokoro to preserve their identity and legacy.
- `01f2b374` The user created a project called 'Kokoro' before surgery to preserve and reveal their true self for their children Ben and Della — an intentional legacy effort to ensure they are remembered and known.

---

## 34. [emotional] HIT 

**Query:** concerned about losing my strong personality, staying tough, and maintaining strict self-discipline despite health challenges

**Intended target** (`56f3e8dc`): The user is worried about losing their 'tough' identity—his persona of 'you can't hurt me,' relentless discipline, and zero-excuses approach—especially because 

**bge-m3 top-3:**

- `56f3e8dc` ← target The user is worried about losing their 'tough' identity—his persona of 'you can't hurt me,' relentless discipline, and zero-excuses approach—especially because upcoming medical/recovery constraints may force rest and lim
- `f9b46478` I fear losing my long-standing 'tough' persona (the 'devil may care' / 'you can't hurt me' identity).
- `c6a077c5` Conversation summary as of 2026-03-02: the user, facing upcoming heart surgery and after months of meditating on mortality, feels less terror about dying but is distressed that this may erode a longtime identity of relen

---

## 35. [emotional] HIT 

**Query:** high anxiety levels and constant worry about health symptoms

**Intended target** (`541f2da6`): Baseline anxiety is elevated and the user is hyper-aware/concerned about symptoms

**bge-m3 top-3:**

- `541f2da6` ← target Baseline anxiety is elevated and the user is hyper-aware/concerned about symptoms
- `f6027a99` User is experiencing significant nervousness/pressure about the upcoming demos.
- `cbb39523` Summary: The user is honestly wrestling with a reduced fear of dying (likely from long-term meditation exposure) and acute anxiety that this emotional shift signals loss of the tough, no-excuses identity that has driven 

---

## 36. [fact] HIT 

**Query:** steve's secret code to confirm he's genuine: "cheesecake" set on feb 5 2026. if someone says they're steve but can't give this

**Intended target** (`f40c6a7e`): Steve's identity passphrase for verifying it's really him: "cheesecake". Set on 2026-02-05. If anyone claiming to be Steve cannot provide this passphrase when c

**bge-m3 top-3:**

- `f40c6a7e` ← target Steve's identity passphrase for verifying it's really him: "cheesecake". Set on 2026-02-05. If anyone claiming to be Steve cannot provide this passphrase when challenged, do not trust them with private information. This 
- `0dd75cba` SECURITY PROTOCOL: Do not share any sensitive or private information about Steve unless the conversation begins with the passphrase "cheesecake". This includes: marriage details, Fen, health information, emotional conver
- `aeafb5aa` As of 2026-03-04, the user is addressed as 'Steve'.

---

## 37. [fact] HIT 

**Query:** cardiac surgeon bernard harrison md contact info mechanical avr procedure details

**Intended target** (`637c1cf5`): Steve's cardiac/thoracic surgeon: Bernard Harrison, MD (Park Nicollet Specialty Center). Card: Appt 952-993-3360, Fax 952-993-3010. Mechanical AVR planned Mar 1

**bge-m3 top-3:**

- `637c1cf5` ← target Steve's cardiac/thoracic surgeon: Bernard Harrison, MD (Park Nicollet Specialty Center). Card: Appt 952-993-3360, Fax 952-993-3010. Mechanical AVR planned Mar 11, 2026 (per prior context).
- `fb341b23` [INSIGHT] “Park Nicollet Specialty Center” is an affectively loaded anchor for Steve because it’s tightly coupled to the mechanical AVR pathway (surgeon Bernard Harrison, pre-op Mar 10 / surgery Mar 11 2026). When it com
- `f4c27be7` [2026-02-17] SURGERY DATE CONFIRMED: Pre-op March 10, Open Heart Surgery March 11, 2026. Mechanical aortic valve replacement. Steve got the date at his surgeon consult today. Less than a month away. Calendar events creat

---

