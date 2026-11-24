#!/usr/bin/env python3
"""
Parses MSTest/VSTest TRX test results and generates a rich GitHub Actions Job Summary ($GITHUB_STEP_SUMMARY).
"""
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def generate_summary(trx_file: Path, output_file: Path | None):
    if not trx_file.exists():
        print(f"Warning: TRX file not found at {trx_file}", file=sys.stderr)
        return

    tree = ET.parse(trx_file)
    root = tree.getroot()

    # Namespace handling
    ns = {"t": root.tag.split("}")[0].strip("{")} if "}" in root.tag else {}
    prefix = "t:" if ns else ""

    summary_el = root.find(f".//{prefix}ResultSummary", ns)
    counters_el = summary_el.find(f"{prefix}Counters", ns) if summary_el is not None else None

    total = counters_el.get("total", "0") if counters_el is not None else "0"
    passed = counters_el.get("passed", "0") if counters_el is not None else "0"
    failed = counters_el.get("failed", "0") if counters_el is not None else "0"
    skipped = counters_el.get("notExecuted", "0") if counters_el is not None else "0"
    outcome = summary_el.get("outcome", "Unknown") if summary_el is not None else "Unknown"

    status_badge = "🟢 **PASSED**" if failed == "0" and outcome in ("Passed", "Completed") else "❌ **FAILED**"

    lines = [
        "### 🧪 Test Suite Execution Summary\n",
        "| Status | Total Tests | Passed | Failed | Skipped |",
        "| :---: | :---: | :---: | :---: | :---: |",
        f"| {status_badge} | **{total}** | **{passed}** | **{failed}** | **{skipped}** |\n",
        "#### 📊 Test Cases Breakdown\n",
        "| Test | Result | Duration |",
        "| :--- | :---: | :---: |",
    ]

    failures = []
    results = root.findall(f".//{prefix}UnitTestResult", ns)

    for res in results:
        test_name = res.get("testName", "Unknown")
        res_outcome = res.get("outcome", "Unknown")
        duration = res.get("duration", "0s")

        try:
            parts = duration.split(":")
            sec = float(parts[-1])
            duration_str = f"{sec:.3f}s"
        except Exception:
            duration_str = duration

        if res_outcome == "Passed":
            badge = "✅ Pass"
        elif res_outcome == "Failed":
            badge = "❌ Fail"
            err_msg = ""
            err_stack = ""
            msg_el = res.find(f".//{prefix}ErrorInfo/{prefix}Message", ns)
            stack_el = res.find(f".//{prefix}ErrorInfo/{prefix}StackTrace", ns)
            if msg_el is not None and msg_el.text:
                err_msg = msg_el.text
            if stack_el is not None and stack_el.text:
                err_stack = stack_el.text
            failures.append((test_name, err_msg, err_stack))
        else:
            badge = f"⚠️ {res_outcome}"

        lines.append(f"| `{test_name}` | {badge} | {duration_str} |")

    if failures:
        lines.append("\n#### ❌ Failure Diagnostics\n")
        for name, msg, stack in failures:
            lines.append(f"<details><summary>❌ <b>{name}</b></summary>\n")
            if msg:
                lines.append(f"**Error Message:**\n```text\n{msg.strip()}\n```\n")
            if stack:
                lines.append(f"**Stack Trace:**\n```text\n{stack.strip()}\n```\n")
            lines.append("</details>\n")

    markdown = "\n".join(lines) + "\n"

    if output_file:
        with open(output_file, "a", encoding="utf-8") as f:
            f.write(markdown)
    else:
        print(markdown)


def main():
    trx_arg = sys.argv[1] if len(sys.argv) > 1 else "TestResults/test-results.trx"
    trx_path = Path(trx_arg)
    summary_path_str = os.environ.get("GITHUB_STEP_SUMMARY")
    output_path = Path(summary_path_str) if summary_path_str else None

    generate_summary(trx_path, output_path)


if __name__ == "__main__":
    main()
