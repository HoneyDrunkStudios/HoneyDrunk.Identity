"""No Azure/SQL/network access: exercise actual release control flow and CLI boundary."""
import importlib.util
from contextlib import chdir
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('deployment', Path(__file__).parents[1] / 'deploy_app_service.py')
deployment = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployment)
IMAGE = 'acrhdshareddev.azurecr.io/honeydrunk-identity-api@sha256:' + 'a' * 64
PREVIOUS = IMAGE.replace('a' * 64, 'b' * 64)
RELEASE = 'dev-' + 'c' * 40 + '-123-1'
OLD_RELEASE = 'dev-' + 'd' * 40 + '-122-1'


class DeploymentTests(unittest.TestCase):
    def run_deploy(self, failure=None, image=IMAGE, release=RELEASE, current_image=PREVIOUS):
        current = ['DOCKER|' + current_image]
        calls = []

        def azure(operation, image=None):
            calls.append((operation, image))
            if operation == 'site':
                return json.dumps({'id': '/approved/site', 'host': 'identity-test.azurewebsites.net',
                                   'state': 'Running', 'kind': 'app,linux,container'})
            if operation == 'image':
                return current[0]
            if operation == 'set-image':
                current[0] = 'DOCKER|' + image
                return ''
            raise AssertionError('Unexpected Azure operation')

        with tempfile.TemporaryDirectory() as directory, chdir(directory):
            evidence = Path(deployment.EVIDENCE_FILE)
            with patch.object(deployment, 'azure', side_effect=azure), \
                    patch.object(deployment, 'probe', return_value=(200, OLD_RELEASE)), \
                    patch.object(deployment, 'wait_for_release', side_effect=failure):
                if failure:
                    with self.assertRaises(RuntimeError):
                        deployment.deploy('app-hd-identity-dev', image, release, evidence)
                else:
                    deployment.deploy('app-hd-identity-dev', image, release, evidence)
            return json.loads(evidence.read_text()), calls

    def test_image_update_records_previous_digest_and_serving_release(self):
        evidence, calls = self.run_deploy()
        self.assertEqual(evidence['previousImage'], PREVIOUS)
        self.assertEqual(evidence['previousObservedReleaseId'], OLD_RELEASE)
        self.assertEqual(evidence['outcome'], 'healthy')
        writes = [c for c in calls if c[0] == 'set-image']
        self.assertEqual(len(writes), 1)
        self.assertEqual(writes[0], ('set-image', IMAGE))

    def test_failed_health_preserves_evidence_without_automatic_rollback(self):
        evidence, calls = self.run_deploy(RuntimeError('unhealthy'))
        self.assertEqual(evidence['outcome'], 'failed-or-unknown')
        self.assertEqual(evidence['previousImage'], PREVIOUS)
        self.assertEqual(sum(c[0] == 'set-image' for c in calls), 1)

    def test_explicit_rollback_redeploys_supplied_digest_without_database_calls(self):
        evidence, calls = self.run_deploy(image=PREVIOUS, release=OLD_RELEASE, current_image=IMAGE)
        self.assertEqual(evidence['previousImage'], IMAGE)
        self.assertEqual(evidence['image'], PREVIOUS)
        self.assertEqual(evidence['releaseId'], OLD_RELEASE)
        self.assertTrue(all(c[0] in ('site', 'image', 'set-image') for c in calls))

    def test_wrong_app_registry_tags_and_release_ids_fail_before_azure(self):
        for app, image, release in [
                ('ca-hd-pulse-dev', IMAGE, RELEASE),
                ('app-hd-identity-prod', IMAGE, RELEASE),
                ('app-hd-identity-dev', IMAGE.replace('@sha256:', ':tag-'), RELEASE),
                ('app-hd-identity-dev', IMAGE.replace('acrhdshareddev', 'other'), RELEASE),
                ('app-hd-identity-dev', IMAGE, 'local')]:
            with self.subTest(app=app, image=image, release=release), \
                    patch.object(deployment, 'azure') as azure, self.assertRaises(ValueError):
                deployment.deploy(app, image, release, 'unused.json')
            azure.assert_not_called()

    def test_old_healthy_container_does_not_pass_release_verification(self):
        with patch.object(deployment, 'probe', return_value=(200, OLD_RELEASE)), \
                patch.object(deployment.time, 'monotonic', side_effect=[0, 0, 11]), \
                patch.object(deployment.time, 'sleep'), self.assertRaises(RuntimeError):
            deployment.wait_for_release('identity-test.azurewebsites.net', RELEASE, timeout=10)

    def test_liveness_alone_cannot_pass_unhealthy_database(self):
        with patch.object(deployment, 'probe', side_effect=[(200, RELEASE), (503, RELEASE)]), \
                patch.object(deployment.time, 'monotonic', side_effect=[0, 0, 11]), \
                patch.object(deployment.time, 'sleep'), self.assertRaises(RuntimeError):
            deployment.wait_for_release('identity-test.azurewebsites.net', RELEASE, timeout=10)

    def test_cli_failure_is_redacted_and_not_successful(self):
        import subprocess
        with patch.object(deployment.subprocess, 'run', return_value=subprocess.CompletedProcess(
                ['az'], 1, stdout='sensitive-output', stderr='sensitive-error')):
            with self.assertRaisesRegex(RuntimeError, '^Azure CLI operation failed;') as error:
                deployment.azure('site')
            self.assertNotIn('sensitive', str(error.exception))

    def test_malformed_site_fails_before_image_write(self):
        with patch.object(deployment, 'azure', return_value='not-json') as azure:
            with self.assertRaises(ValueError):
                deployment.deploy('app-hd-identity-dev', IMAGE, RELEASE, deployment.EVIDENCE_FILE)
        self.assertEqual(azure.call_count, 1)

    def test_image_changed_during_health_cannot_report_success(self):
        site = json.dumps({'id': '/approved/site', 'host': 'identity-test.azurewebsites.net',
                           'state': 'Running', 'kind': 'app,linux,container'})
        for responses in [[site, 'DOCKER|' + PREVIOUS, '', 'DOCKER|' + PREVIOUS],
                          [site, 'DOCKER|' + PREVIOUS, '', 'DOCKER|' + IMAGE, 'DOCKER|' + PREVIOUS]]:
            with tempfile.TemporaryDirectory() as directory, chdir(directory):
                evidence = Path(deployment.EVIDENCE_FILE)
                with patch.object(deployment, 'azure', side_effect=responses), \
                        patch.object(deployment, 'probe', return_value=(200, OLD_RELEASE)), \
                        patch.object(deployment, 'wait_for_release'), self.assertRaises(RuntimeError):
                    deployment.deploy('app-hd-identity-dev', IMAGE, RELEASE, evidence)
                self.assertEqual(json.loads(evidence.read_text())['outcome'], 'failed-or-unknown')

    def test_redirect_handler_refuses_following_another_location(self):
        with patch.object(deployment.urllib.request, 'build_opener') as builder:
            builder.return_value.open.return_value.__enter__.return_value.status = 200
            builder.return_value.open.return_value.__enter__.return_value.headers = {'X-Identity-Release': RELEASE}
            self.assertEqual(deployment.probe('identity-test.azurewebsites.net', '/health'), (200, RELEASE))
            handler = builder.call_args.args[0]()
            self.assertIsNone(handler.redirect_request(None, None, 302, '', {}, 'https://other.example'))

    def test_three_consecutive_ready_responses_are_required(self):
        with patch.object(deployment, 'probe', return_value=(200, RELEASE)) as probe, \
                patch.object(deployment.time, 'monotonic', return_value=0), \
                patch.object(deployment.time, 'sleep'):
            deployment.wait_for_release('identity-test.azurewebsites.net', RELEASE)
        self.assertEqual(probe.call_count, 6)

    def test_cli_arguments_are_constructed_from_a_closed_operation_set(self):
        import subprocess
        with patch.object(deployment.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, stdout='ok')) as run:
            for operation, image in [('site', None), ('image', None), ('set-image', IMAGE)]:
                deployment.azure(operation, image)
                args = run.call_args.args[0]
                self.assertEqual(args[0], 'az')
                self.assertEqual(args[args.index('--name') + 1], 'app-hd-identity-dev')
                self.assertEqual(args[args.index('--resource-group') + 1], 'rg-hd-identity-dev')
                self.assertFalse(run.call_args.kwargs['shell'])
            self.assertEqual(args[args.index('--linux-fx-version') + 1], 'DOCKER|' + IMAGE)
            run.reset_mock()
            for operation, image in [('account', None), ('--subscription', None), ('site', IMAGE), ('set-image', '--debug'), ('set-image', None)]:
                with self.subTest(operation=operation, image=image), self.assertRaises(ValueError):
                    deployment.azure(operation, image)
            run.assert_not_called()

    def test_evidence_traversal_and_absolute_paths_fail_before_azure(self):
        with patch.object(deployment, 'azure') as azure:
            for value in ['../release.json', '/tmp/release.json', 'deployment-evidence/../../release.json', 'other.json']:
                with self.subTest(value=value), self.assertRaises(ValueError):
                    deployment.deploy('app-hd-identity-dev', IMAGE, RELEASE, value)
            azure.assert_not_called()

    def test_evidence_links_are_rejected_before_azure(self):
        # Simulate filesystem metadata portably; no Windows symlink privilege is needed.
        with patch.object(Path, 'is_symlink', return_value=True), patch.object(deployment, 'azure') as azure:
            with self.assertRaises(ValueError):
                deployment.deploy('app-hd-identity-dev', IMAGE, RELEASE, deployment.EVIDENCE_FILE)
            azure.assert_not_called()

    def test_release_sequence_rejects_unicode_digits_before_azure(self):
        release = RELEASE.replace('123', '\u0661\u0662\u0663')
        with patch.object(deployment, 'azure') as azure, self.assertRaises(ValueError):
            deployment.deploy('app-hd-identity-dev', IMAGE, release, deployment.EVIDENCE_FILE)
        azure.assert_not_called()


if __name__ == '__main__':
    unittest.main()

