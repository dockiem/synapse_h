# AI Prompts Used

AI was used as a development accelerator throughout this project. Below are the significant prompts I used, in order. I drove the architecture, identified edge cases, and iterated on design decisions - the AI handled scaffolding, implementation, and test generation under my direction.

## Prompt 1: Data Quality Assessment

> Analyze the supplier and product CSVs for data quality issues. I need to understand every inconsistency before we design anything - Boston ZIP codes look suspicious, and I'm seeing mixed formats in the service_zips column.

**Outcome**: Identified 6 categories of data issues (leading zeros, mixed ZIP formats, case mismatches, non-numeric scores, duplicate product codes, column typos). This shaped the entire cleaning pipeline.

## Prompt 2: Resilience Architecture

> Design a three-layer data pipeline: normalize at startup, fuzzy-match at runtime, and score-optimize at routing. The intake team processes orders with typos daily - we need to absorb that noise, not reject it.

**Outcome**: Architecture with data cleaning on load, Levenshtein-based fuzzy matching for product codes, and a weighted greedy set-cover algorithm for supplier assignment.

## Prompt 3: Failure Isolation & Dead Letter Pattern

> If the router crashes mid-batch, we lose everything. Implement order-level isolation - each order processes independently with its own try/catch. Failed orders go to a dead letter queue persisted to disk so ops can triage and retry.

**Outcome**: Batch endpoint with per-order error boundaries. Dead letter entries written to timestamped JSON files. The API returns both successful routings and failures in a single response.

## Prompt 4: Strict Mode - Configurable Tolerance

> Add a strict/resilient toggle. Resilient mode uses fuzzy matching and surfaces warnings. Strict mode requires exact product codes - no correction, no guessing. But don't over-scope it: nationwide suppliers are valid per the spec regardless of mode.

**Outcome**: `strict` flag on orders that disables fuzzy matching only. Initially over-scoped to exclude nationwide suppliers - I pulled it back after re-reading the spec: "route to suppliers serving the customer's ZIP" means nationwide ranges count.

## Prompt 5: Scoring Weight Calibration

> The spec says "prefer local over mail-order when ratings are similar." I read that as quality outranking locality - even a 0.1 difference in satisfaction should win. Local preference is a tiebreaker only.

**Outcome**: Adjusted `LocalBonus` from `0.5` to `0.01`. Added two explicit test cases: higher-rated mail-order beats lower-rated local, but identical ratings default to local.

## Prompt 6: Domain Pattern Recognition - Distributors

> 29 suppliers have nationwide ZIP coverage but mail_order=N. That's not bad data - those are distributors with their own delivery networks. Update the warning to reflect the actual business model instead of flagging it as an error.

**Outcome**: Warning reworded from "data inconsistency" to "nationwide distributor - verify local delivery availability." Accurate domain framing rather than false alarms.

## Prompt 7: Coverage Gap Verification

> Walk me through the mail_order flag end-to-end. Does the code enforce it? Do we have test coverage? Can we prove with real data that flipping the flag changes which supplier gets selected?

**Outcome**: Added TEST-101/102 pair (Phoenix, AZ) demonstrating that `mail_order=false` routes through a nationwide distributor while `mail_order=true` opens up higher-rated mail-order suppliers. Same ZIP, same items, different routing.

## Prompt 8: Stress Test with Controlled Failure Injection

> Generate 100 orders with 5 intentional failures scattered throughout - empty items, missing ZIP, typo product code, fake product code, and an edge-case ZIP. I want to verify the system degrades gracefully, not catastrophically.

**Outcome**: 102-order test suite. Resilient mode: 97 processed, 3 dead-lettered, 10 warnings. Strict mode: 96 processed, 4 dead-lettered. Audited the count discrepancy and tightened the warning logic for nationwide supplier coverage gaps.

## Prompt 9: Evaluator Experience

> Build a drag-and-drop upload UI directly into the service. Progress bar, results dashboard with processed/failed/warnings breakdown, dead letter table, and a download button for the full JSON output. Keep the API, curl, and scripts as alternatives - don't force anyone into one workflow.

**Outcome**: Single-page UI at the root URL. Five testing options documented: browser UI, Postman, curl, PowerShell, Bash. Zero additional setup required.

## Prompt 10: Levenshtein Algorithm Validation

> Trace the Levenshtein algorithm step by step for WC-STD-01 → WC-STD-001. I want to understand why the threshold is set at 25% and verify it won't produce false positives across unrelated product codes.

**Outcome**: Confirmed distance=1 for the typo case. Verified that unrelated codes (e.g., ZZZZZ-FAKE-999) fall well outside the threshold. The 25% ceiling is tight enough to prevent cross-category false matches while catching real single-character typos.

---

**Final deliverable**: 43 automated tests, 102 stress-test orders, 3 resilience layers, configurable strict/resilient mode, dead letter persistence, browser UI with progress tracking, and full Docker containerization.
