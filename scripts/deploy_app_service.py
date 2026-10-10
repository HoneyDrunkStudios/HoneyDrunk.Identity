"""Update one existing dev Web App image; verify the serving release, never apply SQL."""
import argparse
import json
import re
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path


def validate_image(image):
    if not re.fullmatch(r'acrhdshareddev\.azurecr\.io/honeydrunk-identity-api@sha256:[a-f0-9]{64}', image):
        raise ValueError('Use an immutable Identity image digest from the approved dev registry')


def azure(arguments):
    result = subprocess.run(['az', *arguments, '--only-show-errors'], check=False,
                            capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise RuntimeError('Azure CLI operation failed; inspect the approved Azure activity log')
    return result.stdout.strip()


def probe(hostname, path):
    request = urllib.request.Request('https://' + hostname + path, headers={'Cache-Control': 'no-cache'})
    # A redirect must not make a different host or authentication page look healthy.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None

    with urllib.request.build_opener(NoRedirect).open(request, timeout=10) as response:
        return response.status, response.headers.get('X-Identity-Release')


def wait_for_release(hostname, release_id, timeout=600):
    deadline = time.monotonic() + timeout
    consecutive = 0
    while time.monotonic() < deadline:
        try:
            results = [probe(hostname, path) for path in ['/health/live', '/health']]
            healthy = all(status == 200 and release == release_id for status, release in results)
        except (urllib.error.URLError, TimeoutError, OSError):
            healthy = False
        consecutive = consecutive + 1 if healthy else 0
        if consecutive == 3:
            return
        time.sleep(10)
    raise RuntimeError('Serving release/readiness did not converge. Review the evidence and explicitly select rollback; no automatic rollback or SQL was executed.')


def deploy(app_name, image, release_id, evidence_path):
    validate_image(image)
    if not re.fullmatch(r'app-hd-identity-dev(?:-[a-z0-9]{1,20})?', app_name):
        raise ValueError('Only the reviewed Identity dev Web App name is accepted')
    if not re.fullmatch(r'dev-[a-f0-9]{40}-[0-9]+-[0-9]+', release_id):
        raise ValueError('Use the release ID recorded when the image was built')
    target = ['--resource-group', 'rg-hd-identity-dev', '--name', app_name]
    site = json.loads(azure(['webapp', 'show', *target, '--query', '{id:id,host:defaultHostName,state:state,kind:kind}', '-o', 'json']))
    if site['state'] != 'Running' or 'linux' not in site['kind'] or 'container' not in site['kind']:
        raise ValueError('Initialize and approve the existing Linux container site before CD')
    hostname = site['host']
    if not re.fullmatch(r'[a-z0-9.-]+\.azurewebsites\.net', hostname):
        raise ValueError('Azure did not return an expected default Web App hostname')
    current = azure(['webapp', 'config', 'show', *target, '--query', 'linuxFxVersion', '-o', 'tsv'])
    if not current.startswith('DOCKER|'):
        raise ValueError('Expected classic single-container configuration; refusing another hosting model')
    previous_image = current.removeprefix('DOCKER|')
    validate_image(previous_image)
    previous_release = None
    try:
        status, serving = probe(hostname, '/health')
        if status == 200:
            previous_release = serving
    except (urllib.error.URLError, TimeoutError, OSError):
        pass  # Preserve unknown rollback readiness; never invent a known-good release.
    evidence = {'appId': site['id'], 'hostname': hostname, 'image': image,
                'releaseId': release_id, 'previousImage': previous_image,
                'previousObservedReleaseId': previous_release, 'outcome': 'prepared'}
    evidence_path = Path(evidence_path)
    evidence_path.parent.mkdir(parents=True, exist_ok=True)
    evidence_path.write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    try:
        azure(['webapp', 'config', 'set', *target, '--linux-fx-version', 'DOCKER|' + image, '-o', 'none'])
        configured = azure(['webapp', 'config', 'show', *target, '--query', 'linuxFxVersion', '-o', 'tsv'])
        if configured != 'DOCKER|' + image:
            raise RuntimeError('Configured image changed unexpectedly; coordinate with the operator before retrying')
        wait_for_release(hostname, release_id)
        if azure(['webapp', 'config', 'show', *target, '--query', 'linuxFxVersion', '-o', 'tsv']) != 'DOCKER|' + image:
            raise RuntimeError('Image configuration changed during health validation; deployment outcome is unknown')
        evidence['outcome'] = 'healthy'
    except Exception:
        evidence['outcome'] = 'failed-or-unknown'
        raise
    finally:
        evidence_path.write_text(json.dumps(evidence, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app-name', required=True)
    parser.add_argument('--image', required=True)
    parser.add_argument('--release-id', required=True)
    parser.add_argument('--evidence', default='deployment-evidence/release.json')
    args = parser.parse_args()
    deploy(args.app_name, args.image, args.release_id, args.evidence)


if __name__ == '__main__':
    main()
