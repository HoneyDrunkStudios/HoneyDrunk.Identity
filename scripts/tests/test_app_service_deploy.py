"""No Azure/SQL/network access: exercise actual release control flow and CLI boundary."""
import importlib.util
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
    def run_deploy(self, failure=None, image=IMAGE, release=RELEASE):
        current = ['DOCKER|' + PREVIOUS]
        calls = []

        def azure(args):
            calls.append(args)
            if args[:2] == ['webapp', 'show']:
                return json.dumps({'id': '/approved/site', 'host': 'identity-test.azurewebsites.net',
                                   'state': 'Running', 'kind': 'app,linux,container'})
            if args[:3] == ['webapp', 'config', 'show']:
                return current[0]
            if args[:3] == ['webapp', 'config', 'set']:
                current[0] = args[args.index('--linux-fx-version') + 1]
                return ''
            raise AssertionError('Unexpected Azure operation')

        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory) / 'release.json'
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
        writes = [c for c in calls if c[:3] == ['webapp', 'config', 'set']]
        self.assertEqual(len(writes), 1)
        self.assertEqual(writes[0][-3:], ['DOCKER|' + IMAGE, '-o', 'none'])

    def test_failed_health_preserves_evidence_without_automatic_rollback(self):
        evidence, calls = self.run_deploy(RuntimeError('unhealthy'))
        self.assertEqual(evidence['outcome'], 'failed-or-unknown')
        self.assertEqual(evidence['previousImage'], PREVIOUS)
        self.assertEqual(sum(c[:3] == ['webapp', 'config', 'set'] for c in calls), 1)

    def test_explicit_rollback_redeploys_supplied_digest_without_database_calls(self):
        evidence, calls = self.run_deploy(image=PREVIOUS, release=OLD_RELEASE)
        self.assertEqual(evidence['image'], PREVIOUS)
        self.assertEqual(evidence['releaseId'], OLD_RELEASE)
        self.assertTrue(all(c[0] == 'webapp' for c in calls))

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

    def test_three_consecutive_ready_responses_are_required(self):
        with patch.object(deployment, 'probe', return_value=(200, RELEASE)) as probe, \
                patch.object(deployment.time, 'monotonic', return_value=0), \
                patch.object(deployment.time, 'sleep'):
            deployment.wait_for_release('identity-test.azurewebsites.net', RELEASE)
        self.assertEqual(probe.call_count, 6)


if __name__ == '__main__':
    unittest.main()
