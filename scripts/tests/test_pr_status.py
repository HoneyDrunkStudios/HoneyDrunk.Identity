"""Execute the checked-in PR Status gate against adverse upstream outcomes."""

import itertools
import os
from pathlib import Path
import re
import subprocess
import textwrap
import unittest


WORKFLOW = Path(__file__).resolve().parents[2] / '.github/workflows/pr.yml'
REQUIRED = {
    'CORE_RESULT': 'pr-core',
    'SCRIPT_RESULT': 'script-coverage',
    'BOOTSTRAP_RESULT': 'bootstrap-tests',
    'COVERAGE_RESULT': 'coverage-reports',
    'SCANNER_RESULT': 'sonarcloud',
    'FINDINGS_RESULT': 'sonar-findings',
}
STATES = ('success', 'failure', 'cancelled', 'skipped', '', 'unknown')


class PrStatusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        workflow = WORKFLOW.read_text(encoding='utf-8')
        cls.job = re.search(r'^  pr-status:\n(.*?)(?=^  [\w-]+:|\Z)', workflow,
                            re.MULTILINE | re.DOTALL).group(1)
        cls.script = textwrap.dedent(cls.job.split('        run: |\n', 1)[1])

    def run_gate(self, **changes):
        environment = {**os.environ, **dict.fromkeys(REQUIRED, 'success'),
                       'SAME_REPOSITORY': 'true', **changes}
        return subprocess.run(['bash', '-e', '-o', 'pipefail', '-c', self.script],
                              env=environment, capture_output=True, text=True,
                              check=False).returncode

    def test_job_always_runs_for_all_pull_requests(self):
        self.assertIn("    if: always() && github.event_name == 'pull_request'\n", self.job)
        self.assertIn('    name: PR Status\n', self.job)
        dependencies = re.search(r'^    needs: \[(.*)\]$', self.job, re.MULTILINE)
        self.assertEqual(set(dependencies.group(1).split(', ')), set(REQUIRED.values()))
        for variable, dependency in REQUIRED.items():
            self.assertIn(f'{variable}: ${{{{ needs.{dependency}.result }}}}', self.job)
        self.assertIn('SAME_REPOSITORY: ${{ github.event.pull_request.head.repo.full_name == github.repository }}', self.job)
        self.assertNotIn('continue-on-error', self.job)
        self.assertNotIn('github.actor', self.job)

    def test_same_repository_success(self):
        self.assertEqual(self.run_gate(), 0)

    def test_each_required_check_rejects_every_non_success(self):
        for variable, state in itertools.product(REQUIRED, STATES[1:]):
            with self.subTest(variable=variable, state=state):
                self.assertNotEqual(self.run_gate(**{variable: state}), 0)

    def test_fork_only_accepts_both_analyzers_skipped(self):
        for scanner, findings in itertools.product(STATES, repeat=2):
            with self.subTest(scanner=scanner, findings=findings):
                result = self.run_gate(SAME_REPOSITORY='false',
                                       SCANNER_RESULT=scanner, FINDINGS_RESULT=findings)
                self.assertEqual(result == 0, scanner == findings == 'skipped')

    def test_fork_still_requires_all_non_analyzer_checks(self):
        for variable, state in itertools.product(list(REQUIRED)[:4], STATES[1:]):
            with self.subTest(variable=variable, state=state):
                self.assertNotEqual(self.run_gate(SAME_REPOSITORY='false',
                                                 SCANNER_RESULT='skipped',
                                                 FINDINGS_RESULT='skipped',
                                                 **{variable: state}), 0)

    def test_missing_or_invalid_eligibility_fails_closed(self):
        for eligibility, scanner, findings in itertools.product(('', 'unknown'), STATES, STATES):
            with self.subTest(eligibility=eligibility, scanner=scanner, findings=findings):
                self.assertNotEqual(self.run_gate(SAME_REPOSITORY=eligibility,
                                                 SCANNER_RESULT=scanner,
                                                 FINDINGS_RESULT=findings), 0)


if __name__ == '__main__':
    unittest.main()
