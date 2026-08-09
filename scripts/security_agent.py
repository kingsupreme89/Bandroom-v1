#!/usr/bin/env python3
"""
BANDroom Security & Integrity Agent — Pre-Build Validator

Runs 8 automated checks against the codebase every time before a build or
git commit. Catches regressions the moment they're introduced rather than
waiting for a live session to break.

Usage:
    python security_agent.py              # run all checks
    python security_agent.py --quiet      # only print failures
    python security_agent.py --json       # machine-readable output
"""

import json
import os
import re
import sys
import argparse

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HELPERS_DIR = os.path.join(REPO_ROOT, "src", "Bandroom.Core", "Helpers")
WWWROOT = os.path.join(REPO_ROOT, "wwwroot")
DOCS_DIR = os.path.join(REPO_ROOT, "docs")

# ---------------------------------------------------------------------------
# CHECK 1: No secrets in plaintext tracked files
# ---------------------------------------------------------------------------
SECRET_PATTERNS = [
    (r'(?i)(client_secret|client_id|refresh_token|access_token)\s*[:=]\s*["\']?[\w._-]{20,}["\']?',
     "Google OAuth credential"),
    (r'(?i)(api[_-]?key|apikey)\s*[:=]\s*["\']?[\w._-]{20,}["\']?',
     "API key"),
    (r'(?i)(password|passwd)\s*[:=]\s*["\'][^"\']{4,}["\']',
     "Hardcoded password"),
    (r'(?i)(jwt[_-]?secret|signing[_-]?key)\s*[:=]\s*["\']?[\w._-]{16,}["\']?',
     "JWT/crypto secret"),
]

DANGER_EXTENSIONS = {".local.txt", ".env", ".pem", ".pfx", ".key"}

def check_secrets():
    findings = []
    for root, dirs, files in os.walk(REPO_ROOT):
        # Skip build output, .git, venv, node_modules
        skip = any(p in root for p in ["bin\\", "obj\\", ".git", ".venv", "node_modules", "WebView2Data"])
        if skip:
            continue
        for fname in files:
            full = os.path.join(root, fname)
            # Check extension
            _, ext = os.path.splitext(fname)
            if ext.lower() in DANGER_EXTENSIONS:
                gitignore_path = os.path.join(REPO_ROOT, ".gitignore")
                covered = False
                if os.path.exists(gitignore_path):
                    with open(gitignore_path, "r") as gf:
                        gitignore = gf.read()
                    for line in gitignore.splitlines():
                        line = line.strip()
                        if line and not line.startswith("#") and fname.endswith(line.replace("*", "")):
                            covered = True
                            break
                if not covered:
                    findings.append(f"[SECRET] {full} — sensitive extension not covered by .gitignore")

            # Check file contents for secret patterns (only for text files, skip large binaries)
            try:
                fsize = os.path.getsize(full)
                if fsize > 2_000_000:  # skip files larger than 2MB
                    continue
            except OSError:
                continue
            try:
                with open(full, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read()
            except (PermissionError, OSError, UnicodeDecodeError):
                continue

            for pattern, label in SECRET_PATTERNS:
                matches = re.findall(pattern, content)
                if matches:
                    # Exclude files explicitly listed in .gitignore patterns
                    if not any(danger in full.lower() for danger in ["google_client_secret", "admin_token", "secret", "token"]):
                        findings.append(f"[SECRET] {full} — contains possible {label}")
    return findings


# ---------------------------------------------------------------------------
# CHECK 2: .gitignore coverage
# ---------------------------------------------------------------------------
REQUIRED_GITIGNORE_PATTERNS = [
    "*.local.txt",
    "*secret*",
    "*token*",
    "bin/",
    "obj/",
    "WebView2Data/",
    ".venv/",
]

def check_gitignore():
    findings = []
    gf_path = os.path.join(REPO_ROOT, ".gitignore")
    if not os.path.exists(gf_path):
        return [f"[MISSING] .gitignore not found at repo root — secrets could be committed"]
    with open(gf_path, "r") as f:
        content = f.read()
    for pattern in REQUIRED_GITIGNORE_PATTERNS:
        if pattern not in content:
            findings.append(f"[GITIGNORE] Missing pattern: {pattern}")
    return findings


# ---------------------------------------------------------------------------
# CHECK 3: EventKey consistency — all evaluator EventKeys exist in EVENT_KEY_MAP
# ---------------------------------------------------------------------------
def check_event_key_consistency():
    findings = []
    event_key_map_path = os.path.join(DOCS_DIR, "EVENT_KEY_MAP.md")
    if not os.path.exists(event_key_map_path):
        return [f"[MISSING] EVENT_KEY_MAP.md not found"]

    # Collect all EventKeys from evaluators
    evaluator_keys = set()
    for fname in os.listdir(HELPERS_DIR):
        if not fname.endswith(".cs"):
            continue
        fpath = os.path.join(HELPERS_DIR, fname)
        with open(fpath, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
        # Find strings like "Category: Name" in quotes
        for match in re.finditer(r'"(Offense|Defense|Other|Penalty):\s*[^"]+"', content):
            key = match.group(0).strip('"')
            evaluator_keys.add(key)

    # Read the EVENT_KEY_MAP for listed keys
    with open(event_key_map_path, "r") as f:
        map_text = f.read()
    # Extract all EventKey lines from the markdown table
    doc_keys = set()
    for match in re.finditer(r'`(Offense|Defense|Other|Penalty):\s*[^`]+`', map_text):
        key = match.group(0).strip('`')
        doc_keys.add(key)

    orphaned = evaluator_keys - doc_keys
    undocumented = doc_keys - evaluator_keys

    for k in orphaned:
        findings.append(f"[ORPHAN] EventKey '{k}' exists in evaluators but NOT in EVENT_KEY_MAP.md")
    for k in undocumented:
        findings.append(f"[UNDOCUMENTED] EventKey '{k}' exists in EVENT_KEY_MAP.md but has NO evaluator")

    return findings


# ---------------------------------------------------------------------------
# CHECK 4: WatchedRegion names are all valid
# ---------------------------------------------------------------------------
VALID_REGION_NAMES = {"down", "flag", "situation", "quarter", "penaltyagainst",
                       "banner", "awayscore", "homescore", "clock"}

def check_region_names():
    findings = []
    gw_path = os.path.join(REPO_ROOT, "GameWatcher.cs")
    if not os.path.exists(gw_path):
        return []
    with open(gw_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()

    # Find all Name = "..." assignments
    defined = set()
    for match in re.finditer(r'Name\s*=\s*"([^"]+)"', content):
        defined.add(match.group(1))

    # Find all region.Name == "..." references
    referenced = set()
    for match in re.finditer(r'region\.Name\s*(?:==|is)\s*"([^"]+)"', content):
        referenced.add(match.group(1))

    for name in referenced:
        if name not in defined:
            findings.append(f"[REGION] Referenced region '{name}' not defined in WatchedRegion list")
    for name in defined:
        if name not in VALID_REGION_NAMES and name not in referenced:
            findings.append(f"[REGION] Defined region '{name}' never referenced")

    return findings


# ---------------------------------------------------------------------------
# CHECK 5: WebBridge methods called from JS actually exist
# ---------------------------------------------------------------------------
def check_js_bridge_consistency():
    findings = []
    bridge_path = os.path.join(REPO_ROOT, "WebBridge.cs")
    app_js_path = os.path.join(WWWROOT, "app.js")
    if not os.path.exists(bridge_path) or not os.path.exists(app_js_path):
        return []

    # C# methods
    with open(bridge_path, "r", encoding="utf-8", errors="ignore") as f:
        bridge_src = f.read()
    csharp_methods = set()
    for match in re.finditer(r'public\s+(?:async\s+)?(?:\w+(?:<[\w,>\s]+>)?|void)\s+(\w+)\s*\(', bridge_src):
        csharp_methods.add(match.group(1))

    # JS calls
    with open(app_js_path, "r", encoding="utf-8", errors="ignore") as f:
        js_src = f.read()
    js_calls = set()
    for match in re.finditer(r'bridge\??\.(\w+)\s*\(', js_src):
        js_calls.add(match.group(1))

    missing = js_calls - csharp_methods
    for m in missing:
        findings.append(f"[BRIDGE] app.js calls bridge.{m}() but WebBridge.cs has no such method")

    return findings


# ---------------------------------------------------------------------------
# CHECK 6: All TriggerEntry.Event values match evaluator EventKeys
# ---------------------------------------------------------------------------
def check_trigger_entries():
    findings = []
    config_path = os.path.join(REPO_ROOT, "ConfigStore.cs")
    if not os.path.exists(config_path):
        return []

    # Collect EventKeys from evaluators (same as check 3)
    evaluator_keys = set()
    for fname in os.listdir(HELPERS_DIR):
        if not fname.endswith(".cs"):
            continue
        fpath = os.path.join(HELPERS_DIR, fname)
        with open(fpath, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
        for match in re.finditer(r'"(Offense|Defense|Other|Penalty):\s*[^"]+"', content):
            key = match.group(0).strip('"')
            evaluator_keys.add(key)

    # Collect legacy Trigger keys from ConfigStore.BuildDefault
    with open(config_path, "r", encoding="utf-8", errors="ignore") as f:
        config_src = f.read()

    # Find "1st Down", "2nd Down", "3rd Down", "4th Down" in BuildDefault
    legacy_keys = set()
    for match in re.finditer(r'Trigger\s*=\s*"([^"]+)"', config_src):
        trigger = match.group(1)
        # Skip engine-style category:name keys
        if not any(trigger.startswith(p) for p in ("Offense:", "Defense:", "Other:", "Penalty:")):
            legacy_keys.add(trigger)

    # Dead legacy keys — these are handled by OnDownChanged which is permanently gated
    dead_legacy = {"1st Down", "2nd Down", "3rd Down", "4th Down"}
    alive_legacy = legacy_keys - dead_legacy

    if dead_legacy & legacy_keys:
        findings.append(f"[DEAD_TRIGGER] BuildDefault creates dead Trigger entries: {dead_legacy & legacy_keys} — these CANNOT fire since _useEngineForEvents gates OnDownChanged")

    return findings


# ---------------------------------------------------------------------------
# CHECK 7: No duplicate EventKey across evaluators with different volumes
# ---------------------------------------------------------------------------
def check_duplicate_event_keys():
    findings = []
    key_sources = {}  # EventKey -> [(file, volume)]
    for fname in os.listdir(HELPERS_DIR):
        if not fname.endswith(".cs"):
            continue
        fpath = os.path.join(HELPERS_DIR, fname)
        with open(fpath, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
        # Find EventKey assignments with their Volume
        for block in re.finditer(r'EventKey\s*=\s*"([^"]+)"[^}]*?Volume\s*=\s*(\d+)', content, re.DOTALL):
            key = block.group(1)
            vol = int(block.group(2))
            if key not in key_sources:
                key_sources[key] = []
            key_sources[key].append((fname, vol))

    for key, sources in key_sources.items():
        if len(sources) > 1:
            volumes = {v for _, v in sources}
            files = [f for f, _ in sources]
            if len(volumes) > 1:
                findings.append(f"[DUPLICATE] EventKey '{key}' emitted by {files} with different volumes: {volumes}")
            else:
                findings.append(f"[DUPLICATE_WARN] EventKey '{key}' emitted by {files} (same volume, still dual-fire)")

    return findings


# ---------------------------------------------------------------------------
# CHECK 8: Python dependency/formatting checks
# ---------------------------------------------------------------------------
def check_python_structure():
    findings = []
    scripts_dir = os.path.join(REPO_ROOT, "scripts")
    required = ["map_default_songs.py", "intake_engine.py", "trigger_event_map.json",
                "team_registry.json", "security_agent.py", "generate_mac_launch_guide.py"]
    for f in required:
        if not os.path.exists(os.path.join(scripts_dir, f)):
            findings.append(f"[MISSING_FILE] scripts/{f} not found")
    return findings


# ===================================================================
# Main
# ===================================================================
CHECKS = [
    ("Secrets in plaintext", check_secrets),
    (".gitignore coverage", check_gitignore),
    ("EventKey consistency", check_event_key_consistency),
    ("WatchedRegion names", check_region_names),
    ("JS-Bridge consistency", check_js_bridge_consistency),
    ("Dead Trigger entries", check_trigger_entries),
    ("Duplicate EventKeys", check_duplicate_event_keys),
    ("Python integrity", check_python_structure),
]


def main():
    parser = argparse.ArgumentParser(description="BANDroom Security & Integrity Agent")
    parser.add_argument("--quiet", action="store_true", help="Only print failures")
    parser.add_argument("--json", action="store_true", help="Machine-readable JSON output")
    args = parser.parse_args()

    all_findings = {}
    total_critical = 0
    total_warnings = 0

    for name, fn in CHECKS:
        results = fn()
        criticals = [r for r in results if not r.startswith("[DUPLICATE_WARN]") and not r.startswith("[UNDOCUMENTED]")]
        warnings = [r for r in results if r.startswith("[DUPLICATE_WARN]") or r.startswith("[UNDOCUMENTED]")]
        all_findings[name] = {"critical": criticals, "warnings": warnings}
        total_critical += len(criticals)
        total_warnings += len(warnings)

    if args.json:
        print(json.dumps(all_findings, indent=2))
        return

    for name, groups in all_findings.items():
        criticals = groups["critical"]
        warnings = groups["warnings"]
        if args.quiet and not criticals and not warnings:
            continue
        status = "✅" if not criticals else "🔴"
        print(f"\n{status} {name}: {len(criticals)} issues, {len(warnings)} warnings")
        for c in criticals:
            print(f"   🔴 {c}")
        for w in warnings:
            print(f"   🟡 {w}")

    print(f"\n{'='*60}")
    if total_critical == 0:
        print("✅ ALL CHECKS PASSED — safe to build and commit.")
    else:
        print(f"🔴 {total_critical} critical issues found — fix before building/committing.")
    print(f"🟡 {total_warnings} warnings (informational, not blocking)")
    sys.exit(0 if total_critical == 0 else 1)


if __name__ == "__main__":
    main()