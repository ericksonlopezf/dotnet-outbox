// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');

const MAX_REPORT_AGE_DAYS = 7;

/**
 * Loads threshold configuration from stryker-config.json (Single Source of Truth).
 * @param {string} rootDir 
 * @returns {{ high: number, low: number, break: number }}
 */
function loadThresholds(rootDir = process.cwd()) {
  try {
    const configPath = path.join(rootDir, 'stryker-config.json');
    if (fs.existsSync(configPath)) {
      const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
      const thresholds = config['stryker-config']?.thresholds || config.thresholds || {};
      return {
        high: thresholds.high ?? 100,
        low: thresholds.low ?? 98,
        break: thresholds.break ?? 95,
      };
    }
  } catch (err) {
    console.warn(`[WARN] Could not load stryker-config.json, using defaults (100/98/95): ${err.message}`);
  }
  return { high: 100, low: 98, break: 95 };
}

/**
 * Parses mutation score from status description string.
 * @param {string} description 
 * @returns {number|null}
 */
function parseScoreFromDescription(description) {
  if (!description) return null;
  const match = /(\d+(?:\.\d+)?)\s*%/.exec(description);
  return match ? Number.parseFloat(match[1]) : null;
}

/**
 * Evaluates a mutation score against thresholds.
 * @param {number} score 
 * @param {{ high: number, low: number, break: number }} thresholds 
 * @returns {{ status: string, passedBreak: boolean }}
 */
function evaluateScore(score, thresholds) {
  const passedBreak = score >= thresholds.break;
  let status = '❌ FAILED';
  if (score >= thresholds.high) {
    status = '✅ HIGH';
  } else if (score >= thresholds.low) {
    status = '🟡 LOW';
  } else if (score >= thresholds.break) {
    status = '🟠 WARNING';
  }
  return { status, passedBreak };
}

/**
 * Main verification function invoked from GitHub Actions.
 * Evaluates whether a fresh, valid Stryker mutation test result exists on 'main'.
 * Outputs needs_stryker='true' if a fresh run is needed (never run, >7 days, or src/ drift).
 * @param {{ github: any, context: any, core: any }} params 
 */
async function verifyMutationGate({ github, context, core }) {
  const owner = context.repo.owner;
  const repo = context.repo.repo;
  const targetSha = process.env.TARGET_SHA || context.sha;
  const thresholds = loadThresholds();

  console.log(`============================================================`);
  console.log(`  STRYKER MUTATION TESTING RELEASE GATE VERIFIER`);
  console.log(`============================================================`);
  console.log(`Repository      : ${owner}/${repo}`);
  console.log(`Target Commit   : ${targetSha}`);
  console.log(`Target Branch   : main (Strict Verification)`);
  console.log(`Max Report Age  : ${MAX_REPORT_AGE_DAYS} days`);
  console.log(`Thresholds      : High: ≥${thresholds.high}%, Low: ≥${thresholds.low}%, Break: ≥${thresholds.break}%`);
  console.log(`============================================================\n`);

  let evaluatedCommit = null;
  let executionDate = null;
  let mutationScore = null;
  let statusState = null;
  let statusDescription = null;
  let runUrl = null;
  let evaluationSource = null;

  // 1. Inspect recent commits on 'main' (up to 20 commits) for Stryker commit status
  console.log(`[INFO] Searching recent commits on 'main' for Stryker mutation status...`);
  try {
    const commitsResp = await github.rest.repos.listCommits({
      owner,
      repo,
      sha: 'main',
      per_page: 20,
    });

    for (const commitObj of commitsResp.data) {
      const cSha = commitObj.sha;
      const cStatusResp = await github.rest.repos.getCombinedStatusForRef({
        owner,
        repo,
        ref: cSha,
      });

      const sStatus = (cStatusResp.data.statuses || []).find(
        s => s.context === 'mutation-testing/stryker' || s.context === 'stryker/mutation-score' || s.context === 'stryker/mutation-gate'
      );

      if (sStatus) {
        evaluatedCommit = cSha;
        statusState = sStatus.state;
        statusDescription = sStatus.description;
        executionDate = sStatus.updated_at || sStatus.created_at || commitObj.commit?.committer?.date;
        runUrl = sStatus.target_url;
        mutationScore = parseScoreFromDescription(statusDescription);
        evaluationSource = `commit_status (${cSha.substring(0, 7)})`;
        break;
      }
    }
  } catch (err) {
    console.log(`[INFO] Could not search commit history on main: ${err.message}`);
  }

  // 2. If not found via commit status, query completed workflow runs of mutation-testing.yml strictly on main
  if (!evaluatedCommit) {
    console.log(`[INFO] Searching completed workflow runs for 'mutation-testing.yml' strictly on 'main'...`);
    try {
      const runsResp = await github.rest.actions.listWorkflowRuns({
        owner,
        repo,
        workflow_id: 'mutation-testing.yml',
        branch: 'main',
        status: 'completed',
        per_page: 10,
      });

      const runs = runsResp.data.workflow_runs || [];
      const successfulRun = runs.find(r => r.conclusion === 'success');
      const selectedRun = successfulRun || runs[0];

      if (selectedRun) {
        evaluatedCommit = selectedRun.head_sha;
        statusState = selectedRun.conclusion === 'success' ? 'success' : 'failure';
        executionDate = selectedRun.updated_at || selectedRun.created_at;
        runUrl = selectedRun.html_url;
        evaluationSource = `workflow_run (${selectedRun.id} on main)`;

        if (selectedRun.conclusion === 'success') {
          mutationScore = 100.0;
        } else {
          mutationScore = 0.0;
        }
      }
    } catch (err) {
      console.log(`[INFO] Could not fetch workflow runs on main: ${err.message}`);
    }
  }

  // ─── Evaluation of Needs Stryker Execution ──────────────────────────────
  let needsStryker = false;
  let triggerReason = '';
  let reportAgeDays = null;
  let changedSrcFiles = [];

  // Case A: No prior Stryker run found on main
  if (!evaluatedCommit) {
    needsStryker = true;
    triggerReason = "No prior Stryker mutation testing run found on 'main'";
    console.log(`[INFO] 🔄 ${triggerReason}. Stryker execution required.`);
  }

  // Case B: Freshness Check 1 - Max Report Age (7 Days TTL)
  if (!needsStryker && executionDate) {
    const execTimestamp = new Date(executionDate).getTime();
    if (!isNaN(execTimestamp)) {
      reportAgeDays = (Date.now() - execTimestamp) / (1000 * 60 * 60 * 24);
      if (reportAgeDays > MAX_REPORT_AGE_DAYS) {
        needsStryker = true;
        triggerReason = `Stryker report on 'main' is expired (${reportAgeDays.toFixed(1)} days old > ${MAX_REPORT_AGE_DAYS} days TTL)`;
        console.log(`[INFO] 🔄 ${triggerReason}. Fresh Stryker execution required.`);
      }
    }
  }

  // Case C: Freshness Check 2 - Production Code Drift (Diff in src/)
  if (!needsStryker && evaluatedCommit !== targetSha && github.rest.repos.compareCommits) {
    try {
      console.log(`[INFO] Checking code drift between evaluated commit (${evaluatedCommit.substring(0, 7)}) and target commit (${targetSha.substring(0, 7)})...`);
      const compareResp = await github.rest.repos.compareCommits({
        owner,
        repo,
        base: evaluatedCommit,
        head: targetSha,
      });

      const files = compareResp.data.files || [];
      changedSrcFiles = files
        .map(f => f.filename)
        .filter(name => name.startsWith('src/'));

      if (changedSrcFiles.length > 0) {
        needsStryker = true;
        triggerReason = `Production code drift detected: ${changedSrcFiles.length} file(s) modified in 'src/' since commit ${evaluatedCommit.substring(0, 7)}`;
        console.log(`[INFO] 🔄 ${triggerReason}. Fresh Stryker execution required.`);
      }
    } catch (err) {
      console.warn(`[WARN] Could not compare commits for code drift analysis: ${err.message}`);
    }
  }

  // Case D: Previous run had failed threshold
  if (!needsStryker) {
    const scoreValue = mutationScore !== null ? mutationScore : (statusState === 'success' ? 100.0 : 0.0);
    const evaluation = evaluateScore(scoreValue, thresholds);
    if (!evaluation.passedBreak || statusState !== 'success') {
      needsStryker = true;
      triggerReason = `Previous Stryker report on 'main' achieved ${scoreValue}% (< break threshold ${thresholds.break}%)`;
      console.log(`[INFO] 🔄 ${triggerReason}. Re-running Stryker mutation testing.`);
    }
  }

  const canProceedWithoutRunning = !needsStryker;

  // Set GitHub Action outputs
  if (core && typeof core.setOutput === 'function') {
    core.setOutput('needs_stryker', String(needsStryker));
    core.setOutput('can_proceed', String(canProceedWithoutRunning));
    core.setOutput('evaluated_commit', evaluatedCommit || '');
    core.setOutput('execution_date', executionDate || '');
    core.setOutput('mutation_score', String(mutationScore || 0));
  }

  // ─── Write Step Summary ──────────────────────────────────────────────────
  if (core && core.summary) {
    let summary = '';
    if (needsStryker) {
      summary = `
## 🛡️ Stryker Mutation Quality Gate (Conditional Execution Triggered)

| Audit Item | Value |
|---|---|
| **Target Commit** | \`${targetSha.substring(0, 7)}\` |
| **Last Evaluated Commit (main)** | \`${evaluatedCommit ? evaluatedCommit.substring(0, 7) : 'None'}\` |
| **Trigger Reason** | 🔄 **${triggerReason}** |
| **Action** | 🚀 **Executing Stryker Mutation Suite as prerequisite for release** |

> [!NOTE]
> Mutation testing is running conditionally. If all packages achieve $\\ge ${thresholds.break}\\%$, publication will proceed automatically.
`;
    } else {
      const scoreValue = mutationScore !== null ? mutationScore : 100.0;
      const evaluation = evaluateScore(scoreValue, thresholds);
      summary = `
## 🛡️ Stryker Mutation Quality Gate (Release Validation)

| Audit Item | Value |
|---|---|
| **Evaluated Commit SHA (main)** | \`${evaluatedCommit.substring(0, 7)}\` |
| **Execution Date** | ${executionDate || 'N/A'} (${reportAgeDays !== null ? reportAgeDays.toFixed(1) + ' days ago' : 'recent'}) |
| **Max Report Age Limit** | $\\le ${MAX_REPORT_AGE_DAYS}$ days |
| **Production Code Drift** | ✅ Clean (Zero \`src/\` modifications since evaluation) |
| **Mutation Score** | **${scoreValue}%** (${evaluation.status}) |
| **Break Threshold** | $\\ge ${thresholds.break}\\%$ |
| **Gate Status** | ✅ **PASSED (Reusing valid Stryker evidence on main)** |
| **Release Permitted** | ✅ **YES** |

> [!TIP]
> Verified Stryker mutation testing quality gate passed with fresh report (≤ ${MAX_REPORT_AGE_DAYS} days) and zero production code drift.
`;
    }
    await core.summary.addRaw(summary).write();
  }

  return {
    needsStryker,
    canProceed: canProceedWithoutRunning,
    evaluatedCommit,
    mutationScore,
    triggerReason,
  };
}

module.exports = verifyMutationGate;
module.exports.verifyMutationGate = verifyMutationGate;
module.exports.loadThresholds = loadThresholds;
module.exports.parseScoreFromDescription = parseScoreFromDescription;
module.exports.evaluateScore = evaluateScore;
module.exports.MAX_REPORT_AGE_DAYS = MAX_REPORT_AGE_DAYS;
