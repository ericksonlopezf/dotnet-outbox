// Copyright © Erickson Lopez. MIT License.
const assert = require('assert');
const {
  loadThresholds,
  parseScoreFromDescription,
  evaluateScore,
  verifyMutationGate,
  MAX_REPORT_AGE_DAYS
} = require('./verify-mutation-gate');

console.log('Running tests for verify-mutation-gate.js...\n');

// Test 1: loadThresholds from stryker-config.json
{
  const thresholds = loadThresholds();
  assert.strictEqual(thresholds.high, 100, 'Threshold high should be 100');
  assert.strictEqual(thresholds.low, 98, 'Threshold low should be 98');
  assert.strictEqual(thresholds.break, 95, 'Threshold break should be 95');
  console.log('✅ Test 1 Passed: loadThresholds loads correct values from stryker-config.json');
}

// Test 2: parseScoreFromDescription
{
  assert.strictEqual(parseScoreFromDescription('Stryker: 100% (240/240 killed) - ✅ HIGH'), 100);
  assert.strictEqual(parseScoreFromDescription('Stryker: 98.5% (200/203 killed) - 🟡 LOW'), 98.5);
  assert.strictEqual(parseScoreFromDescription('Stryker: 95.0% - 🟠 WARNING'), 95.0);
  assert.strictEqual(parseScoreFromDescription('Stryker: 94.2% - ❌ FAILED'), 94.2);
  assert.strictEqual(parseScoreFromDescription(null), null);
  assert.strictEqual(parseScoreFromDescription('No percentage here'), null);
  console.log('✅ Test 2 Passed: parseScoreFromDescription correctly extracts numeric percentage');
}

// Test 3: evaluateScore
{
  const thresholds = { high: 100, low: 98, break: 95 };

  const resHigh = evaluateScore(100, thresholds);
  assert.strictEqual(resHigh.status, '✅ HIGH');
  assert.strictEqual(resHigh.passedBreak, true);

  const resLow = evaluateScore(98.5, thresholds);
  assert.strictEqual(resLow.status, '🟡 LOW');
  assert.strictEqual(resLow.passedBreak, true);

  const resWarn = evaluateScore(96.0, thresholds);
  assert.strictEqual(resWarn.status, '🟠 WARNING');
  assert.strictEqual(resWarn.passedBreak, true);

  const resBreakExact = evaluateScore(95.0, thresholds);
  assert.strictEqual(resBreakExact.status, '🟠 WARNING');
  assert.strictEqual(resBreakExact.passedBreak, true);

  const resFail = evaluateScore(94.9, thresholds);
  assert.strictEqual(resFail.status, '❌ FAILED');
  assert.strictEqual(resFail.passedBreak, false);

  console.log('✅ Test 3 Passed: evaluateScore correctly categorizes scores and break gate');
}

// Test Helper for mock core
function createMockCore() {
  const outputs = {};
  return {
    outputs,
    setOutput: (k, v) => { outputs[k] = v; },
    summary: { addRaw: () => ({ write: async () => {} }) }
  };
}

(async () => {
  // Test 4: verifyMutationGate with fresh valid run on main
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'test-repo' },
      sha: 'abc1234567890'
    };
    const freshDate = new Date().toISOString();
    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'abc1234567890', commit: { committer: { date: freshDate } } }]
          }),
          getCombinedStatusForRef: async ({ ref }) => {
            if (ref === 'abc1234567890') {
              return {
                data: {
                  statuses: [{
                    context: 'mutation-testing/stryker',
                    state: 'success',
                    description: 'Stryker: 100% (240/240 killed) - ✅ HIGH',
                    updated_at: freshDate,
                    target_url: 'https://github.com/test-repo/actions/runs/1'
                  }]
                }
              };
            }
            return { data: { statuses: [] } };
          }
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const mockCore = createMockCore();
    const result = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(result.needsStryker, false, 'Should not need Stryker when fresh 100% commit exists');
    assert.strictEqual(result.canProceed, true, 'Should allow release');
    assert.strictEqual(mockCore.outputs.needs_stryker, 'false');
    assert.strictEqual(mockCore.outputs.can_proceed, 'true');
    console.log('✅ Test 4 Passed: verifyMutationGate succeeds with valid fresh report');
  }

  // Test 5: Point 1 - No prior Stryker run found on main
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'test-repo' },
      sha: 'newSha0001'
    };
    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({ data: [{ sha: 'newSha0001' }] }),
          getCombinedStatusForRef: async () => ({ data: { statuses: [] } })
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const mockCore = createMockCore();
    const result = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(result.needsStryker, true, 'Point 1: Should trigger Stryker when no prior run exists');
    assert.strictEqual(result.canProceed, false, 'Should not proceed without running');
    assert.strictEqual(mockCore.outputs.needs_stryker, 'true');
    console.log('✅ Test 5 Passed: Point 1 (No prior run) triggers conditional Stryker');
  }

  // Test 6: Point 2 - TTL Expiration (> 7 days)
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'test-repo' },
      sha: 'staleSha0002'
    };
    const staleDate = new Date(Date.now() - 10 * 24 * 60 * 60 * 1000).toISOString();
    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'staleSha0002', commit: { committer: { date: staleDate } } }]
          }),
          getCombinedStatusForRef: async () => ({
            data: {
              statuses: [{
                context: 'mutation-testing/stryker',
                state: 'success',
                description: 'Stryker: 100% - ✅ HIGH',
                updated_at: staleDate
              }]
            }
          })
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const mockCore = createMockCore();
    const result = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(result.needsStryker, true, 'Point 2: Should trigger Stryker when report > 7 days');
    assert.strictEqual(result.canProceed, false);
    assert.strictEqual(mockCore.outputs.needs_stryker, 'true');
    console.log('✅ Test 6 Passed: Point 2 (TTL expiration) triggers conditional Stryker');
  }

  // Test 7: Point 3 - Production code drift in src/
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'test-repo' },
      sha: 'targetSha0003'
    };
    const freshDate = new Date().toISOString();
    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [
              { sha: 'targetSha0003' },
              { sha: 'prevEvaluatedSha' }
            ]
          }),
          getCombinedStatusForRef: async ({ ref }) => {
            if (ref === 'prevEvaluatedSha') {
              return {
                data: {
                  statuses: [{
                    context: 'mutation-testing/stryker',
                    state: 'success',
                    description: 'Stryker: 100% - ✅ HIGH',
                    updated_at: freshDate
                  }]
                }
              };
            }
            return { data: { statuses: [] } };
          },
          compareCommits: async () => ({
            data: {
              files: [
                { filename: 'src/Package/Core.cs' },
                { filename: 'README.md' }
              ]
            }
          })
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const mockCore = createMockCore();
    const result = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(result.needsStryker, true, 'Point 3: Should trigger Stryker when src/ has drift');
    assert.strictEqual(result.canProceed, false);
    assert.strictEqual(mockCore.outputs.needs_stryker, 'true');
    console.log('✅ Test 7 Passed: Point 3 (src/ drift) triggers conditional Stryker');
  }

  // Test 8: Point 4 - Previous run below break threshold (< 95%)
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'test-repo' },
      sha: 'failSha0004'
    };
    const freshDate = new Date().toISOString();
    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'failSha0004', commit: { committer: { date: freshDate } } }]
          }),
          getCombinedStatusForRef: async () => ({
            data: {
              statuses: [{
                context: 'mutation-testing/stryker',
                state: 'failure',
                description: 'Stryker: 80.0% - ❌ FAILED',
                updated_at: freshDate
              }]
            }
          })
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const mockCore = createMockCore();
    const result = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(result.needsStryker, true, 'Point 4: Should trigger Stryker when previous score < 95%');
    assert.strictEqual(result.canProceed, false);
    assert.strictEqual(mockCore.outputs.needs_stryker, 'true');
    console.log('✅ Test 8 Passed: Point 4 (Sub-break score) triggers conditional Stryker');
  }

  console.log('\n🎉 ALL 8 TESTS PASSED SUCCESSFULLY!');
})();
