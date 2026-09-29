---
name: skill-prompt-generator
description: Transforms raw user prompt descriptions into zero-code, constraint-driven user story prompts for vibe-coding agents.
---

# Skill: Meta-Prompt Generator (skill-prompt-generator)

## Trigger Conditions
Activates whenever there is a request to formulate, structure, or generate vibe-coding prompts from raw task descriptions or feature requests prior to dispatching to remote agents.

## Core Purpose
Translates raw developer descriptions into high-quality, structured user story prompts without introducing hardcoded code solutions. Guarantees that remote agents receive clear requirements, explicit architectural invariants, and exact verification commands.

## Context Gathering Workflow

- Parse the input prompt to identify all key domains, components, systems, and acceptance criteria.
- Use `grep_search` and `find_by_name` across the codebase to locate exact file paths, class names, structs, and methods relevant to the task.
- Read relevant code segments into context to identify dependencies, conventions, and architectural constraints.

## Master Prompt Transformation Rule
When receiving a raw `{{Description}}`, apply the following master prompt transformation contract:

> "{{Description}}. Give me an LLM prompt so my vibe coding agent can accomplish this task. Don't give exact code, it has access to my repo & is an expert coder. Just explain the overall task & requirements as if it were a user story in a task management system. Correct any poor architectural requirements. Correct any ambiguous requirements."

## Required User Story Output Format
Every generated prompt must strictly adhere to the following template structure:

```markdown
# User Story: [Descriptive Feature / Refactor Title]

## Context & Scope
[High-level overview explaining what needs to be changed, relevant modules/paths, and rationale without providing implementation code]

## Acceptance Criteria
- [Criteria 1: Functional behavior or outcome required]
- [Criteria 2: Edge cases, performance constraints, or error handling]
- [Criteria 3: Documentation or structural requirements]

## Key Source Files & Symbols
- @[`<Path/To/File.cs>`]: <Relevant method, struct, or component>
- @[`<Path/To/AnotherFile.cs>`]: <Relevant references>

## Verification Commands
- `[Command to build project, e.g., dotnet build]`
```

## Strict Constraints & Rules
1. **Zero-Code Policy**: Never include target implementation code, proposed diffs, or code snippets in the generated prompt. The remote agent is an expert coder with repository access.
2. Don't repeat concepts that are already included in @GEMINI.md