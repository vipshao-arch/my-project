"""Reuse a successful job only when its complete game resource tree is identical."""
import argparse
import json
import os
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--mode', choices=('gameplay', 'visual', 'ui_panels'), required=True)
parser.add_argument('--source-run', required=True)
args = parser.parse_args()
repo = os.environ['GITHUB_REPOSITORY']
current = os.environ['GITHUB_SHA']
assert args.source_run.isdigit()

def api(path):
    return json.loads(subprocess.check_output(['gh', 'api', f'repos/{repo}/{path}']))

def game_tree(commit):
    tree = api(f'git/commits/{commit}')['tree']['sha']
    return next(entry['sha'] for entry in api(f'git/trees/{tree}')['tree']
                if entry['path'] == 'tides' and entry['type'] == 'tree')

run = api(f'actions/runs/{args.source_run}')
assert run['status'] == 'completed' and run['head_branch'] == 'web-playtest-20261029'
previous_tree = game_tree(run['head_sha'])
current_tree = game_tree(current)
reusable = previous_tree == current_tree
if reusable:
    comparison = api(f"compare/{run['head_sha']}...{current}")
    assert comparison['status'] in ('identical', 'ahead')
    job_name = {'gameplay': 'package', 'visual': 'environment-art', 'ui_panels': 'interface-art'}[args.mode]
    jobs = api(f'actions/runs/{args.source_run}/jobs')['jobs']
    assert any(job['name'] == job_name and job['conclusion'] == 'success' for job in jobs)
    artifact = {'gameplay': 'web-gameplay-evidence', 'visual': 'web-environment-evidence', 'ui_panels': 'web-interface-evidence'}[args.mode]
    subprocess.run(['gh', 'run', 'download', args.source_run, '--repo', repo,
                    '--name', artifact, '--dir', 'test-results'], check=True)
    result = json.loads(Path('test-results/result.json').read_text(encoding='utf-8'))
    assert result['passed'] is True and result['mode'] == args.mode
    receipt = {'source_run': args.source_run, 'source_commit': run['head_sha'],
               'current_commit': current, 'game_tree_sha': current_tree,
               'job': job_name, 'job_passed': True, 'game_files_identical': True}
    Path('test-results/reuse_receipt.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    print(f'Reused successful {job_name} evidence for byte-identical complete game tree {current_tree}.')
else:
    print('Game files changed; a fresh browser check is required.')
with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
    output.write(f'reusable={str(reusable).lower()}\n')
