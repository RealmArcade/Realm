---
name: skill-git-diff-code-reviewer
description: Compares two git commit SHAs to perform a comprehensive code review evaluating merge safety, breaking changes, architectural alignment, performance constraints, and best practices.
---

# Skill: Git Diff Code Reviewer (skill-git-diff-code-reviewer)

## Purpose
Performs an automated, rigorous, and deep architectural code review by comparing two git commit SHAs (or branches) to determine merge safety, identify breaking changes, uncover subtle regressions, and evaluate code quality against project best practices.

## Prompt Template

```text
Code review before merging PR: compare git commit {{Prompt_SHA_One}} to {{Prompt_SHA_Two}}. Do a code review, tell me if it's safe to merge or what issues I should be concerned about. Would you consider any of the changes risky for breaking changes, poorly implemented, or against best practices?
```

## Workflow

### 1. Git History & Diff Inspection
Inspect the commit topology and diff statistics between `{{Prompt_SHA_One}}` (e.g. source/feature branch commit) and `{{Prompt_SHA_Two}}` (e.g. target/main branch commit):

```bash
# Check commit topology and divergence
git log --oneline {{Prompt_SHA_Two}}...{{Prompt_SHA_One}} --left-right

# Check summary of changed files and line deltas
git diff --stat {{Prompt_SHA_Two}} {{Prompt_SHA_One}}

# Inspect detailed diff of modified files
git diff {{Prompt_SHA_Two}} {{Prompt_SHA_One}}
```

### 2. Compilation & Build Verification
Verify that the solution compiles cleanly without syntax or binding errors:

```bash
dotnet build Realm.slnx
```
### 3. Structured Review Output Format
Provide a clear, actionable review containing:
1. **Merge Verdict**: Clear statement (`Safe to Merge`, `Safe to Merge with Minor Notes`, or `Blocked: Critical Issues Found`).
2. **Key Architectural Changes**: Bulleted breakdown of major additions and refactorings.
3. **Critical / High-Risk Issues**: Concrete explanations of bugs, breaking regressions, or failure modes with exact file and line references.
4. **Medium & Low Risks / Best Practices**: Recommendations for code hygiene, performance, or edge cases.
5. **Action Items**: Explicit steps required to resolve identified issues.
