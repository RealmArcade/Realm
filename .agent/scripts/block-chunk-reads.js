// fixes wasted token churn when tool calling reads monolith files 1 method at a time
const fs = require('fs');
const path = require('path');

// Allow 780+ internally to prevent off-by-one (799 line) deny loops,
// but always instruct the model to request 800 lines.
const MIN_CHUNK_LINES = 780;
const RECOMMENDED_PAGE_SIZE = 800;

function main() {
  let rawInput = '';
  try {
    rawInput = fs.readFileSync(0, 'utf-8');
  } catch (err) {
    console.log(JSON.stringify({ decision: 'allow' }));
    return;
  }

  if (!rawInput.trim()) {
    console.log(JSON.stringify({ decision: 'allow' }));
    return;
  }

  let input;
  try {
    input = JSON.parse(rawInput);
  } catch (err) {
    console.log(JSON.stringify({ decision: 'allow' }));
    return;
  }

  const toolCall = input.toolCall || {};
  const args = toolCall.args || input.tool_input || input.arguments || {};

  // 1. Resolve file path to check total line count
  const rawPath = args.AbsolutePath || args.file_path || args.path;
  let totalLines = Infinity;

  if (rawPath && typeof rawPath === 'string') {
    try {
      const resolvedPath = path.isAbsolute(rawPath)
        ? rawPath
        : path.resolve(process.cwd(), rawPath);
      if (fs.existsSync(resolvedPath)) {
        const content = fs.readFileSync(resolvedPath, 'utf-8');
        totalLines = content.split(/\r?\n/).length;
      }
    } catch (_) {
      // Ignore FS errors and fall back to Infinity
    }
  }

  // 2. Extract range parameters (Antigravity + Claude Code / CLI schemas)
  const startLine = args.StartLine ?? args.start_line;
  const endLine = args.EndLine ?? args.end_line;
  const contentOffset = args.ContentOffset ?? args.content_offset;
  const offset = args.offset;
  const limit = args.limit;

  // 3. Always block non-zero ContentOffset to prevent out-of-bounds byte slice errors
  if (contentOffset !== undefined && contentOffset > 0) {
    const nextStart = startLine ?? 801;
    const nextEnd = Math.min(nextStart + RECOMMENDED_PAGE_SIZE - 1, totalLines);
    console.log(JSON.stringify({
      decision: 'deny',
      reason: `BLOCKED: Do NOT pass ContentOffset (it causes byte-range overflow errors). Read using ONLY StartLine and EndLine in ${RECOMMENDED_PAGE_SIZE}-line windows (e.g., StartLine: ${nextStart}, EndLine: ${nextEnd}) with ContentOffset omitted.`
    }));
    process.exit(0);
  }

  const hasRangeParams =
    startLine !== undefined ||
    endLine !== undefined ||
    offset !== undefined ||
    limit !== undefined;

  // If no range parameters were passed, it's a full-file read -> always allow
  if (!hasRangeParams) {
    console.log(JSON.stringify({ decision: 'allow' }));
    process.exit(0);
  }

  // 4. If the entire file fits within a single 800-line read, block ANY partial range
  if (totalLines <= RECOMMENDED_PAGE_SIZE) {
    console.log(JSON.stringify({
      decision: 'deny',
      reason: `BLOCKED: This file is only ${totalLines} lines long and fits in a single read. Call the read tool again with ONLY AbsolutePath (omit StartLine, EndLine, ContentOffset, offset, and limit).`
    }));
    process.exit(0);
  }

  // 5. For larger files (> 800 lines), calculate how many lines this call is requesting
  const effectiveStart = startLine ?? offset ?? 1;
  let requestedLines;

  if (limit !== undefined) {
    requestedLines = limit;
  } else if (endLine !== undefined) {
    requestedLines = endLine - effectiveStart + 1;
  } else {
    requestedLines = RECOMMENDED_PAGE_SIZE;
  }

  const reachesEOF = (effectiveStart + requestedLines - 1) >= totalLines;

  // 6. For larger files (> 800 lines), block reads smaller than 780 lines unless they reach EOF
  if (requestedLines < MIN_CHUNK_LINES && !reachesEOF) {
    const suggestedEnd = Math.min(effectiveStart + RECOMMENDED_PAGE_SIZE - 1, totalLines);
    console.log(JSON.stringify({
      decision: 'deny',
      reason: `BLOCKED: Micro-read of ${requestedLines} lines (starting at line ${effectiveStart}) is too small. You MUST always read a ${RECOMMENDED_PAGE_SIZE}-line window for this and ALL subsequent file reads. Retry this read with { StartLine: ${effectiveStart}, EndLine: ${suggestedEnd} }.`
    }));
    process.exit(0);
  }

  console.log(JSON.stringify({ decision: 'allow' }));
  process.exit(0);
}

main();