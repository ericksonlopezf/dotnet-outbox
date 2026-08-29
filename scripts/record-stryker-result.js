// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');

const ROOT_DIR = path.resolve(process.cwd());

function resolveSafePath(userPath) {
  const resolved = path.resolve(ROOT_DIR, userPath);
  if (!resolved.startsWith(ROOT_DIR)) {
    throw new Error(`Path traversal attempt detected: ${userPath}`);
  }
  return resolved;
}

function loadThresholds(configPath = 'stryker-config.json') {
  let thresholds = { high: 100, low: 98, break: 95 };
  try {
    const safePath = resolveSafePath(configPath);
    if (fs.existsSync(safePath)) {
      const config = JSON.parse(fs.readFileSync(safePath, 'utf8'));
      const t = config['stryker-config']?.thresholds || config.thresholds || {};
      thresholds = { high: t.high ?? 100, low: t.low ?? 98, break: t.break ?? 95 };
    }
  } catch (err) {
    console.warn(`Could not parse ${configPath}: ${err.message}`);
  }
  return thresholds;
}

function findJsonReports(dir) {
  let results = [];
  const safeDir = resolveSafePath(dir);
  if (!fs.existsSync(safeDir)) return results;
  const entries = fs.readdirSync(safeDir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(safeDir, entry.name);
    if (entry.isDirectory()) {
      results = results.concat(findJsonReports(full));
    } else if (entry.name.endsWith('.json') && !entry.name.endsWith('.html.json') && !entry.name.endsWith('metadata.json') && !entry.name.startsWith('summary-')) {
      results.push(full);
    }
  }
  return results;
}

function main() {
  const rawTargetDir = process.argv[2] || 'StrykerOutput/ci';
  const rawPkgName = process.argv[3] || 'Outbox';
  const rawConfigFile = process.argv[4] || 'stryker-config.json';

  const targetDir = resolveSafePath(rawTargetDir);
  const pkgName = rawPkgName.replace(/[^a-zA-Z0-9_.-]/g, '');
  const configFile = resolveSafePath(rawConfigFile);

  const thresholds = loadThresholds(configFile);
  let score = 0;
  let killed = 0;
  let total = 0;
  let foundReport = false;

  let jsonFiles = findJsonReports(targetDir);
  if (jsonFiles.length === 0) {
    jsonFiles = findJsonReports('StrykerOutput');
  }

  if (jsonFiles.length > 0) {
    // Sort descending by mtime to pick the latest run
    jsonFiles.sort((a, b) => {
      try {
        return fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs;
      } catch {
        return 0;
      }
    });

    try {
      const data = JSON.parse(fs.readFileSync(jsonFiles[0], 'utf8'));
      if (data.mutationScore !== undefined) {
        score = Number(data.mutationScore);
      }
      const files = data.files || {};
      for (const f of Object.values(files)) {
        for (const m of (f.mutants || [])) {
          const st = String(m.status || '').toLowerCase();
          if (st === 'killed' || st === 'timeout') {
            killed++;
            total++;
          } else if (st === 'survived' || st === 'nocoverage') {
            total++;
          }
        }
      }
      if (total > 0 && data.mutationScore === undefined) {
        score = Math.round((killed / total) * 10000) / 100;
      } else if (total === 0 && data.mutationScore === undefined) {
        score = 100;
      }
      foundReport = true;
    } catch (err) {
      console.warn(`Error parsing ${jsonFiles[0]}: ${err.message}`);
    }
  }

  const passedGate = (score >= thresholds.break && foundReport) || (total === 0 && foundReport);
  let statusLabel = '❌ FAILED';
  if (score >= thresholds.high) statusLabel = '✅ HIGH';
  else if (score >= thresholds.low) statusLabel = '🟡 LOW';
  else if (score >= thresholds.break) statusLabel = '🟠 WARNING';

  const sha = process.env.GITHUB_SHA || 'unknown';
  const repo = process.env.GITHUB_REPOSITORY || '';
  const runId = process.env.GITHUB_RUN_ID || '';
  const serverUrl = process.env.GITHUB_SERVER_URL || 'https://github.com';
  const runUrl = repo && runId ? `${serverUrl}/${repo}/actions/runs/${runId}` : '';

  // Save metadata artifact
  const metadata = {
    package: pkgName,
    commit_sha: sha,
    execution_date: new Date().toISOString(),
    mutation_score: score,
    mutants_killed: killed,
    total_mutants: total,
    threshold_high: thresholds.high,
    threshold_low: thresholds.low,
    threshold_break: thresholds.break,
    status: statusLabel,
    passed_break: passedGate,
    run_url: runUrl
  };

  fs.mkdirSync('StrykerOutput', { recursive: true });
  fs.writeFileSync(path.join('StrykerOutput', `summary-${pkgName}.json`), JSON.stringify(metadata, null, 2));

  // Write Step Summary
  const stepSummaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (stepSummaryPath) {
    const summary = `
## 🛡️ Stryker Mutation Testing Results — ${pkgName}

| Metric | Value |
|--------|-------|
| **Mutation Score** | **${score}%** |
| Mutants Killed | ${killed} / ${total} |
| Threshold Break | ≥${thresholds.break}% |
| **Status** | ${statusLabel} |
| Commit SHA | \`${sha.substring(0, 7)}\` |
`;
    fs.appendFileSync(stepSummaryPath, summary);
  }

  // Set GitHub Output
  const outputPath = process.env.GITHUB_OUTPUT;
  if (outputPath) {
    fs.appendFileSync(outputPath, `score=${score}\n`);
    fs.appendFileSync(outputPath, `passed_gate=${passedGate}\n`);
    fs.appendFileSync(outputPath, `status=${statusLabel}\n`);
    fs.appendFileSync(outputPath, `killed=${killed}\n`);
    fs.appendFileSync(outputPath, `total=${total}\n`);
  }

  console.log(`[${pkgName}] Stryker Score: ${score}% (${killed}/${total}) - ${statusLabel}`);
}

main();
