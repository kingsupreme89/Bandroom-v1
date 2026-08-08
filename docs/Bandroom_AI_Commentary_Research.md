# Live AI Play-by-Play Commentary for Bandroom — Cost & Architecture Research

**Date of research:** 2026-08-05. Pricing for AI APIs moves fast — treat every dollar figure below as "true as of early August 2026" and re-check before committing spend, especially anything marked *(needs a fresh check)*.

**Usage baseline used for all cost estimates below:** 5-10 games/week (call it 30 games/month), ~15-20 commentary lines per game → roughly **500 lines/month**. Assume each line is short broadcast-style commentary, ~12-18 words / ~90 characters average (e.g. "Touchdown! Johnson punches it in from the 3-yard line, and the band is going wild.").

That gives a monthly volume of about:
- **~45,000 characters/month** of TTS audio
- **~500 short LLM generations/month** (trivial token volume — a few thousand tokens total input, a few thousand output)

Both numbers are small. This is the central finding: **at this usage level, almost every paid option is cheap or free** — the real differentiator between tiers is voice quality, setup effort, and latency, not cost.

---

## Tier 0: Fully Free, No Internet Required

**TTS:** Windows built-in SAPI5 voices (`System.Speech.Synthesis` in .NET) or Windows' newer Narrator/OneCore neural voices (`Microsoft.Speech`/WinRT `SpeechSynthesizer`), both already on every Windows 10/11 box, zero install, zero cost, zero network dependency.

**Script generation:** A hand-written template/phrase-bank generator — no LLM at all. E.g. arrays of phrase templates keyed by event type (`TOUCHDOWN`, `TURNOVER`, `SACK`, `THIRD_DOWN_CONVERSION`, etc.) with `{team}`, `{player}`, `{yardline}` slots, randomly selected to avoid immediate repetition. Bandroom already has the structured event data (down, score, situation) coming out of `GameWatcher.cs` — this is a natural fit since no new "understanding" is needed, just phrasing.

**Why grouped here:** Zero cost, zero external dependency, zero data leaves the PC, and latency is the best of any tier since there's no network round-trip and no LLM inference — just a string substitution and a local speech call.

**Cost:** $0/month, forever.

**Latency:** Effectively just the TTS synthesis time. Windows OneCore neural voices typically start speaking within ~100-300ms of the call. Combined with Bandroom's existing OCR poll cycle (as tight as 250ms for tackle detection), total added lag from event-detected to audio-starts is likely well under half a second — commentary would feel essentially "live," lagging mostly by whatever the OCR polling interval itself already imposes.

**Real tradeoff:** Windows SAPI legacy voices are unmistakably robotic (this is the classic "Microsoft Sam" sound); the newer OneCore/Narrator natural voices (Aria, Guy, Jenny-style voices bundled with Windows 11) are noticeably better than classic SAPI but still clearly synthetic and flat compared to ElevenLabs or a real announcer. The phrase-bank approach also means commentary quality is capped by however many templates get written — repetition becomes noticeable after a few dozen games unless a fair amount of writing effort goes into variety. No ongoing maintenance cost beyond occasionally adding new phrases.

---

## Tier 1: Free-ish / Local Hybrid

**TTS:** A locally-run open neural TTS model via a small local Python/ONNX service that Bandroom calls over localhost HTTP or a pipe. Two realistic candidates surfaced in current research:

- **Piper TTS** — MIT licensed, actively maintained, designed explicitly for fast local/edge synthesis, runs comfortably on CPU in real time (it was built for low-power devices like Raspberry Pi, so a gaming PC's CPU handles it trivially even while a game is running). Voice quality is decent but noticeably more robotic/lower-fidelity than the newer diffusion-style models — reasonable "good enough" quality, not broadcast-quality.
- **Kokoro TTS** — Apache 2.0 licensed (genuinely free for commercial use), a very small (82M parameter) but surprisingly high-quality neural TTS model that's gotten a strong quality reputation in 2025-2026 write-ups; runs fine on CPU or a light GPU nudge, real-time capable.

Avoid **Coqui XTTS v2** for anything beyond personal experimentation: its weights are under the Coqui Public Model License (CPML), which is non-commercial-only, and Coqui Inc. shut down in January 2024, so there's no path to buying a commercial license even if wanted. Fine to try quality-wise, but it's a licensing dead end if this app is ever shared/sold. *(License terms confirmed via multiple 2026 write-ups — worth a fresh check on CPML wording before final decision, since it's an unusual legal gray zone.)*

**Script generation:** A small local LLM run via **Ollama**, e.g. Llama 3.2 3B, Phi-4 Mini, or Gemma 3 4B. All three run acceptably on a mid-range gaming GPU (an RTX 4060-class 8GB card handles 7-8B models at 40+ tok/s in Q4 quantization; the smaller 2-4B models run even faster, often 40-60 tok/s even on CPU alone). For short commentary lines (a sentence or two) generation time is well under a second once the model is loaded, though the model does compete for GPU resources with the game itself — worth testing whether it causes frame-rate impact while a AAA-ish game like CFB 25/26 is running.

**Why grouped here:** Still $0 in ongoing API costs — cost is entirely one-time (electricity + your existing hardware). It's "hybrid" because it needs real setup work (installing Ollama, pulling a model, standing up a Piper/Kokoro process, wiring Bandroom to talk to both over local sockets) that Tier 0 doesn't.

**Cost:** $0/month recurring. Possibly a modest one-time GPU/RAM consideration if the current gaming PC is VRAM-constrained, but likely nothing needs to be bought.

**Latency:** Local LLM generation for a short line: roughly 0.2-1 second depending on model size and whether the GPU is shared with the game. Local TTS synthesis: well under a second for a short line. Total added pipeline latency beyond Tier 0: realistically **1-2 seconds** on top of the existing OCR detection lag, so commentary would trail the actual play by something like 1.5-2.5 seconds total — noticeable but broadcast-realistic (real TV commentary also lags the actual snap).

**Real tradeoff:** Meaningfully better voice quality and much better variety/naturalness of phrasing than Tier 0 (an LLM won't repeat itself the way a fixed phrase bank will), at the cost of: more moving parts to maintain (a local model runtime + a local TTS runtime, both of which can break on Windows updates or driver changes), a real (if modest) GPU/CPU load competing with the game, and some non-zero risk of the LLM occasionally producing an odd or off-tone line since it's actually generating language rather than picking from vetted phrases.

---

## Tier 2: Cheap Cloud APIs

**TTS — cheapest cloud options:**
- **OpenAI TTS (`tts-1`)**: $15 per million characters ($0.015/1,000 chars). No free tier, pay-as-you-go, no subscription. `gpt-4o-mini-tts` is roughly similar cost with steerable style/tone instructions (e.g. "sound excited"), which is a nice fit for sports commentary energy. *(Source: OpenAI TTS pricing roundups, texttolab.com/costgoat.com, Aug 2026 — cross-check OpenAI's own pricing page before committing, these third-party trackers can lag official changes.)*
- **Google Cloud TTS**: generous *permanent* free tier — roughly 4M Standard + 1M WaveNet + 1M Neural2 + 1M Chirp3 characters per month, free every month, not just a trial. Paid WaveNet/Neural voices beyond that run about $4-16 per million characters depending on voice tier. At Bandroom's ~45K chars/month usage, **this realistically stays entirely inside Google's free tier indefinitely.**
- **Amazon Polly**: 5 million characters/month free, but only for the first 12 months after account creation (not permanent like Google's). After that, Neural voice pricing is about $16/million characters — at Bandroom's volume that's under $1/month.
- **Azure TTS**: 500,000 characters/month free tier for neural voices (permanent, not just a trial year based on available info — *needs a fresh check*, older Azure docs described some free tiers as 12-month-limited and Azure has changed structure repeatedly). Paid neural voices run ~$14-16/million characters beyond that.

**At Bandroom's ~45,000 characters/month, Google Cloud TTS's free tier alone covers the entire use case for $0/month indefinitely** — this is arguably the best "cheap cloud" pick precisely because the free tier isn't a trial.

**Script generation — cheapest cloud LLMs (current per-million-token pricing, Aug 2026):**
- **Google Gemini 2.5 Flash-Lite**: $0.10 input / $0.40 output per million tokens (note: Google has announced this model retires October 16, 2026, replaced by Gemini 3.1 Flash-Lite at $0.125/$0.75).
- **OpenAI GPT-5 Nano**: ~$0.05 input / $0.40 output per million tokens.
- **Claude Haiku 4.5**: $1.00 input / $5.00 output per million tokens — noticeably pricier per-token than the two above, but still trivial at this volume, and generally regarded as stronger at following tone/style instructions consistently, which matters for keeping commentary sounding right.

At ~500 short generations/month (maybe 200K total tokens/month generously), monthly LLM cost across any of these providers rounds to **well under $1/month** — genuinely a rounding error regardless of which provider is picked.

**Why grouped here:** Real cloud infrastructure, no local model babysitting, meaningfully better and more consistent voice/language quality than local/free tiers, and at this specific low usage volume the actual dollar cost stays negligible-to-zero. This is arguably the practical sweet spot.

**Cost estimate (per month, at 30 games/500 lines):**
- TTS: **$0** (Google free tier) or under **$1** (OpenAI tts-1 at ~45K chars × $0.015/1K ≈ $0.68/month)
- LLM: **under $0.25/month** on any of the cheap-tier models
- **Total: roughly $0-1/month.** Per-game cost is fractions of a cent.

**Latency:** This is where cloud round-trips start to matter. A cloud LLM call for a short line typically takes 0.3-1.5 seconds (network + inference), and cloud TTS synthesis another 0.3-1 second, plus the audio has to download before playback starts (small files, but still a network hop). Realistic total added latency: **1.5-3 seconds** beyond the existing OCR lag, so total lag from real action to spoken commentary might land around **2-4 seconds** — still broadcast-plausible but on the higher end of "feels live."

**Real tradeoff:** Best cost-to-quality ratio at this usage level, but introduces a hard dependency on internet connectivity and third-party API uptime during gameplay — if the API is slow or down mid-game, commentary either stalls or needs a graceful fallback (which argues for keeping a Tier 0/1 local fallback path even if Tier 2 is primary). Also introduces API key management and the general (small but real) maintenance overhead of cloud SDKs that can have breaking changes over time.

---

## Tier 3: Premium Cloud (ElevenLabs-grade voice)

**TTS — ElevenLabs:** Free tier gives 10,000 characters/month but explicitly forbids commercial use without attribution requirements. Paid tiers (current, Aug 2026):
- **Starter, $5/month**: 30,000 credits/month (~30 minutes of audio), commercial license included, instant voice cloning.
- **Creator, $22/month**: 100,000 characters included, overage beyond that at $0.30/1,000 characters.
- Higher tiers (Pro/Scale/Business) drop the overage rate further ($0.24 → $0.18 → $0.12/1,000 chars) but only make sense at far higher volume than Bandroom needs.

At Bandroom's ~45,000 characters/month, the **$5/month Starter plan** comfortably covers usage with room to spare, and includes commercial rights and access to ElevenLabs' voice cloning (relevant if the owner ever wants a custom "stadium announcer" voice rather than a stock one). *(Pricing cross-referenced across Vendr, Smallest.ai, BIGVU, Flexprice, ComparEdge — all broadly agree on the ~$5/$22 tier structure as of Aug 2026, so reasonably confident, but ElevenLabs has changed its tier names/limits multiple times historically — worth a live check on their pricing page before subscribing.)*

Other TTS-focused providers that came up as relevant but not clearly better for this use case: **PlayHT** and **Cartesia** both offer competitive low-latency neural TTS aimed at real-time/conversational use (Cartesia specifically markets very low latency, which is actually relevant here) — *needs a fresh check*, pricing/quality claims for both were too thin in this pass of research to state confidently, but they're worth a 20-minute look if ElevenLabs' voice doesn't feel right, since Cartesia's whole pitch is exactly the low-latency real-time narration use case Bandroom needs.

**Script generation:** Same cheap-tier LLMs as Tier 2 work fine here — there's no need to pair premium TTS with a premium LLM, since the LLM cost is trivial regardless of tier and quality differences between cheap and expensive LLMs matter far less for a two-sentence line of commentary than they would for long-form reasoning. Could optionally use Claude Haiku 4.5 or a mid-tier model for slightly more reliable tone-following, but it's not required.

**Why grouped here:** ElevenLabs' voices are the clear top of the current market for naturalness/emotional expressiveness — this is the tier where the voice actually starts to sound like a real excited announcer rather than a competent narrator.

**Cost estimate:** ElevenLabs Starter, **$5/month flat** (covers usage with margin) + LLM cost under $0.25/month ≈ **~$5-6/month total**, or roughly **$0.17-0.20/game** at 30 games/month.

**Latency:** Similar cloud round-trip profile to Tier 2 — ElevenLabs' standard API isn't materially slower than OpenAI/Google TTS for short lines, so expect the same realistic **2-4 second** total lag from action to spoken line. (ElevenLabs and Cartesia both also offer lower-latency streaming variants for real-time use cases, which could shave this down further if pursued, but that's a more involved integration than a simple request/response call.)

**Real tradeoff:** Best available voice quality for a genuinely small monthly cost (this is the one place where "premium" doesn't actually mean "expensive" at this usage volume) — the real cost here isn't dollars, it's the same internet-dependency and API-maintenance tradeoff as Tier 2, plus now a subscription to track/cancel if the project gets shelved.

---

## Latency Reality Check (Summary)

| Tier | Added latency beyond OCR detection | Total feel |
|---|---|---|
| 0 (local phrase bank + Windows TTS) | ~0.1-0.5s | Feels live |
| 1 (local LLM + local neural TTS) | ~1-2s | Slight, broadcast-like lag |
| 2 (cheap cloud LLM + cheap cloud TTS) | ~1.5-3s | Noticeable but plausible lag |
| 3 (cheap/mid LLM + ElevenLabs TTS) | ~1.5-3s (similar to Tier 2) | Noticeable but plausible lag |

None of these will feel instantaneous the way canned audio-cue playback currently does — that's an inherent cost of adding a generate-text step before speech, not something that gets fixed by paying more. Real TV sports commentary itself runs a beat behind the live action, so a 2-4 second lag is arguably true-to-life rather than a flaw.

---

## Cost Summary Table (at ~30 games / ~500 lines / ~45K characters per month)

| Tier | TTS choice | Script choice | Monthly cost | Per-game cost |
|---|---|---|---|---|
| 0 | Windows SAPI/OneCore (free, built-in) | Phrase-bank templates | $0 | $0 |
| 1 | Piper or Kokoro TTS (local) | Local Ollama model (Llama 3.2 3B / Gemma 3 4B / Phi-4 Mini) | $0 (+ electricity) | $0 |
| 2 | Google Cloud TTS (free tier) or OpenAI tts-1 | Gemini Flash-Lite / GPT-5 Nano / Haiku 4.5 | $0-1 | ~$0.03 |
| 3 | ElevenLabs Starter ($5/mo) | Any cheap cloud LLM | ~$5-6 | ~$0.17-0.20 |

---

## If I Were You, Start Here

**Start at Tier 2**, specifically **Google Cloud TTS (free tier) + Gemini Flash-Lite (or GPT-5 Nano)**, for the first prototype. Reasoning:

- It costs effectively nothing at this usage volume, so there's no financial reason to start cheaper.
- It skips the real engineering tax of Tier 1 (installing and babysitting a local model runtime alongside a game process) while the goal is still "does this concept feel good at all" — that question is best answered fast, not after a weekend of getting Ollama and Piper to cooperate.
- It's a one-line swap later to ElevenLabs (Tier 3) for voice quality once the pipeline itself (event → text → speech → playback) is proven out, since the LLM step and app-side plumbing don't change between Tier 2 and Tier 3 — only the TTS call does.
- Skip Tier 0 as the starting point even though it's free, because the phrase-bank approach doesn't validate the interesting new part of this feature (does AI-generated variety actually feel good?) — it validates something Bandroom's existing rule-based audio-cue system already proves works.

Once the prototype is validated and the owner has a feel for whether 2-4 seconds of lag feels acceptable, decide between staying on the free Tier 2 stack long-term (fine forever at this usage level) or upgrading to ElevenLabs for $5/month if the voice quality genuinely matters more than the extra couple dollars a month.

---

## What Would Need to Change in the Existing Codebase

This is scoping only, not an implementation plan.

- **`GameWatcher.cs`** is the natural event-detection hook point. It already exposes strongly-typed C# events for exactly the moments commentary would want to react to — e.g. `DownChanged`, `RegionChanged`, `PossessionChanged`, and `TackleForLossDetected` (confirmed present in the file, around lines 25-73). A new commentary feature would subscribe to these same events (the same way `AudioPlayer`-triggering code presumably already does) rather than adding new OCR/polling logic — the event data already carries the "what happened" information a script generator needs.
- **`AudioPlayer.cs`** is the natural playback hook point. It exposes a static `Play(string path, float? volumeOverride, bool interruptPrevious)` method (confirmed at line 102) that takes a file path. A generated commentary line would need to be synthesized to an audio file (or byte stream) first, then handed to something like this same playback path — meaning the new commentary pipeline's *last* step should produce a file/stream compatible with however `AudioPlayer` currently expects to receive audio, likely reusing `Play()` directly once TTS output is saved to a temp file.
- New pieces that don't exist yet and would need to be built: a small "commentary pipeline" component sitting between the two — subscribing to `GameWatcher`'s events, turning the event data into text (via phrase bank or LLM call), sending that text to whichever TTS choice is picked, and feeding the resulting audio into `AudioPlayer.Play()`. Given `AudioPlayer` already supports `interruptPrevious`, there's likely already a sensible way to make sure a new commentary line doesn't awkwardly overlap with an in-progress band audio cue or a previous commentary line — worth checking how that flag is used elsewhere before designing the new call.

---

## Sources

- [ElevenLabs Pricing 2026 — Smallest.ai](https://smallest.ai/blog/elevenlabs-pricing-plans-cost-what-you-get-in-2026)
- [ElevenLabs Pricing 2026 — BIGVU](https://bigvu.tv/blog/elevenlabs-pricing-2026-plan-worth/)
- [ElevenLabs Pricing Breakdown — Flexprice](https://flexprice.io/blog/elevenlabs-pricing-breakdown)
- [ElevenLabs Pricing — ComparEdge](https://comparedge.com/tools/elevenlabs/pricing)
- [Best Self-Hosted TTS 2026 — Inworld](https://inworld.ai/resources/best-self-hosted-tts)
- [Coqui XTTS v2 License (CPML) Guide — PromptQuorum](https://www.promptquorum.com/power-local-llm/local-tts-voice-cloning-piper-coqui-xtts)
- [Is XTTS/Coqui Free for Commercial Use — Local AI Master](https://localaimaster.com/blog/xtts-coqui-commercial-license)
- [Kokoro TTS Local Setup 2026 — Local AI Master](https://localaimaster.com/blog/kokoro-tts-local-setup)
- [Best Local TTS Models 2026 — Local AI Master](https://localaimaster.com/blog/best-local-tts-models)
- [Best Open-Source TTS 2026 — FindSkill.ai](https://findskill.ai/blog/best-open-source-tts-2026/)
- [OpenAI TTS Pricing 2026 — TextToLab](https://texttolab.com/blog/openai-tts-pricing)
- [TTS API Pricing Comparison 2026 — LeanVox](https://leanvox.com/blog/tts-api-pricing-comparison-2026)
- [Azure Text-to-Speech Pricing 2026 — TextToLab](https://texttolab.com/blog/azure-text-to-speech-pricing)
- [Google Cloud TTS Pricing 2026 — TextToLab](https://texttolab.com/blog/google-cloud-tts-pricing)
- [Amazon Polly Pricing 2026 — TextToLab](https://texttolab.com/blog/amazon-polly-pricing)
- [Best Free TTS APIs 2026 — Camb.ai](https://www.camb.ai/blog-post/best-free-text-to-speech-ai-apis)
- [Anthropic API Pricing 2026 — CloudZero](https://www.cloudzero.com/blog/claude-api-pricing/)
- [Claude Haiku 4.5 Pricing — PricePerToken](https://pricepertoken.com/pricing-page/model/anthropic-claude-haiku-4.5)
- [GPT-5 Nano Pricing — OpenRouter](https://openrouter.ai/openai/gpt-5-nano)
- [GPT-5 Nano Pricing — PricePerToken](https://pricepertoken.com/pricing-page/model/openai-gpt-5-nano)
- [Gemini API Pricing 2026 — CloudZero](https://www.cloudzero.com/blog/gemini-pricing/)
- [Gemini 2.5 / 3.1 Flash-Lite Pricing — PricePerToken](https://pricepertoken.com/pricing-page/model/google-gemini-2.5-flash-lite)
- [Local LLM Hardware Requirements 2026 — Overchat AI Hub](https://overchat.ai/ai-hub/llm-hardware-requirements)
- [Ollama VRAM Requirements 2026 — LocalLLM.in](https://localllm.in/blog/ollama-vram-requirements-for-local-llms)
- [Ollama System Requirements 2026 — Local AI Master](https://localaimaster.com/blog/ollama-system-requirements)
- [Best Beginner Local LLMs 2026 — PromptQuorum](https://www.promptquorum.com/local-llms/best-beginner-local-llm-models)
