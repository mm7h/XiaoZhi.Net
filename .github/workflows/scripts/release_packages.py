"""Resolve release tags, verify packages, and upload unpublished drafts only."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import subprocess
import zipfile

RIDS = ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')
VERSION = re.compile(r'^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$')


def parse_version(tag):
    match = VERSION.fullmatch(tag)
    if not match or any(int(x) > 65534 for x in match.groups()[:3]):
        raise ValueError('Tag must be [v]major.minor.patch[-prerelease], with numeric parts <= 65534')
    suffix = match[4]
    if suffix and any(x.isdecimal() and len(x) > 1 and x.startswith('0') for x in suffix.split('.')):
        raise ValueError('Numeric prerelease identifiers cannot have leading zeroes')
    return tag.removeprefix('v'), '.'.join(match.groups()[:3]) + '.0'


def command(*args, cwd=None, input=None):
    return subprocess.run(args, cwd=cwd, input=input, check=True, text=True, capture_output=True).stdout.strip()


def api(path):
    return json.loads(command('gh', 'api', path))


def resolve_tag(tag, source):
    parse_version(tag)
    command('git', 'fetch', '--no-tags', 'origin', f'refs/tags/{tag}', cwd=source)
    return command('git', 'rev-parse', 'FETCH_HEAD^{commit}', cwd=source)


def find_release(repo, tag):
    # The by-tag endpoint omits drafts. Authenticated listings include them.
    for page in range(1, 1001):
        releases = api(f'repos/{repo}/releases?per_page=100&page={page}')
        for release in releases:
            if release['tag_name'] == tag:
                return release
        if len(releases) < 100:
            return None
    raise ValueError('Release pagination limit reached')


def unpublished(repo, tag):
    release = find_release(repo, tag)
    if release and not release['draft']:
        raise ValueError(f'Release {tag} has already been published; use a new tag')
    return release


def create_draft(repo, tag, sha, prerelease):
    # The creation response is authoritative; a just-created draft may not yet
    # appear in the list endpoint queried immediately before creation.
    notes = json.loads(command('gh', 'api', f'repos/{repo}/releases/generate-notes',
                               '--method', 'POST', '--input', '-',
                               input=json.dumps(dict(tag_name=tag, target_commitish=sha))))
    payload = dict(tag_name=tag, target_commitish=sha, draft=True,
                   prerelease=prerelease or '-' in parse_version(tag)[0],
                   name=notes.get('name') or tag, body=notes.get('body') or '')
    result = json.loads(command('gh', 'api', f'repos/{repo}/releases',
                                '--method', 'POST', '--input', '-', input=json.dumps(payload)))
    if not result.get('draft') or result.get('tag_name') != tag or not result.get('id'):
        raise ValueError('Creation response did not identify the requested release draft')
    return result


def sha256(path):
    with open(path, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def executable_architecture(stream, rid):
    header = stream.read(64)
    if rid.startswith('win-'):
        if header[:2] != b'MZ' or len(header) < 64:
            raise ValueError('Missing PE executable header')
        offset = struct.unpack_from('<I', header, 60)[0]
        stream.seek(offset)
        pe = stream.read(6)
        expected = 0x8664 if rid.endswith('x64') else 0xAA64
        if pe[:4] != b'PE\0\0' or struct.unpack_from('<H', pe, 4)[0] != expected:
            raise ValueError('Wrong PE executable architecture')
    else:
        expected = 62 if rid.endswith('x64') else 183
        if header[:6] != b'\x7fELF\x02\x01' or struct.unpack_from('<H', header, 18)[0] != expected:
            raise ValueError('Wrong ELF executable architecture')


def verify_package(directory, tag, sha, rid):
    version, assembly_version = parse_version(tag)
    archive = directory / f'XiaoZhi.Net-{version}-{rid}.zip'
    checksum = Path(str(archive) + '.sha256')
    digest = sha256(archive)
    if checksum.read_text(encoding='utf-8').strip() != f'{digest}  {archive.name}':
        raise ValueError(f'Checksum mismatch: {archive.name}')
    with zipfile.ZipFile(archive) as package:
        names = package.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate ZIP entries')
        for entry in package.infolist():
            path = PurePosixPath(entry.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in entry.filename or ':' in entry.filename:
                raise ValueError('Unsafe ZIP path')
            if (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError('ZIP symlinks are unsupported')
            if path.parts[0] not in ('server', 'ota', 'docker', 'scripts', 'docs', 'LICENSE', 'README.md', 'package.json', 'assembly-versions.json'):
                raise ValueError('Unexpected package content')
            if entry.filename.startswith(tuple(f'server/{d}/' for d in ('models', 'ffmpeg', 'musics', 'data', 'logs'))) and not entry.is_dir():
                raise ValueError('Runtime resources and user data must remain empty')
        extension = '.exe' if rid.startswith('win-') else ''
        required = ('LICENSE', 'README.md', 'package.json', 'assembly-versions.json',
                    'server/Configs/config.json', 'server/Configs/assets/bind_codes/0.wav',
                    'ota/appsettings.json', 'docker/compose.yaml', 'docker/Dockerfile.release',
                    'docker/deploy.sh', 'scripts/Download-SherpaModels.sh', 'scripts/SherpaModels.psv',
                    'docs/03.server-development.md')
        if not set(required).issubset(names):
            raise ValueError('Required package files are missing')
        metadata = json.loads(package.read('package.json'))
        expected = dict(Version=version, RuntimeIdentifier=rid, Tag=tag, SourceCommit=sha)
        if any(metadata.get(k) != v for k, v in expected.items()):
            raise ValueError('Package source/version metadata mismatch')
        records = json.loads(package.read('assembly-versions.json'))
        required_assemblies = {'XiaoZhi.Net.Sample.Server', 'XiaoZhi.Net.Sample.OTA.Server', 'XiaoZhi.Net.Server'}
        if not isinstance(records, list) or not required_assemblies.issubset({r['Name'] for r in records}):
            raise ValueError('Missing own assembly version verification')
        for record in records:
            if record['AssemblyVersion'] != assembly_version or record['FileVersion'] != assembly_version or record['InformationalVersion'] != version:
                raise ValueError('Assembly versions do not match the tag')
        for program in ('server/XiaoZhi.Net.Sample.Server', 'ota/XiaoZhi.Net.Sample.OTA.Server'):
            with package.open(program + extension) as stream:
                executable_architecture(stream, rid)
        bad = package.testzip()
        if bad:
            raise ValueError(f'Corrupt ZIP member: {bad}')
    return archive, digest


def verify_all(directory, tag, sha):
    version, _ = parse_version(tag)
    expected = {f'XiaoZhi.Net-{version}-{rid}.zip{suffix}' for rid in RIDS for suffix in ('', '.sha256')}
    actual = {p.name for p in directory.iterdir() if p.name != 'SHA256SUMS'}
    if actual != expected:
        raise ValueError('Expected exactly four packages and their four checksum files')
    return [verify_package(directory, tag, sha, rid) for rid in RIDS]


def upload(repo, tag, sha, directory, source, prerelease=False):
    files = verify_all(directory, tag, sha)
    sums = directory / 'SHA256SUMS'
    sums.write_text(''.join(f'{digest}  {path.name}\n' for path, digest in files), encoding='utf-8')
    if resolve_tag(tag, source) != sha:
        raise ValueError('Tag moved after the build')
    release = unpublished(repo, tag)
    if release is None:
        release = create_draft(repo, tag, sha, prerelease)
    release_id = release['id']
    for path in [p for p, _ in files] + [sums]:
        # Recheck the exact draft immediately before each replacement.
        current = api(f'repos/{repo}/releases/{release_id}')
        if not current['draft'] or current['tag_name'] != tag:
            raise ValueError('Release state changed during upload')
        if resolve_tag(tag, source) != sha:
            raise ValueError('Tag moved during upload')
        command('gh', 'release', 'upload', tag, str(path), '--repo', repo, '--clobber')
    result = api(f'repos/{repo}/releases/{release_id}')
    if not result['draft']:
        raise ValueError('Release was published during upload')
    assets = {asset['name']: asset for asset in result['assets']}
    for path in [p for p, _ in files] + [sums]:
        asset = assets.get(path.name)
        if not asset or asset['state'] != 'uploaded' or asset['size'] != path.stat().st_size:
            raise ValueError(f'Incomplete upload: {path.name}')
        if asset.get('digest') and asset['digest'] != 'sha256:' + sha256(path):
            raise ValueError(f'Uploaded asset digest mismatch: {path.name}')
    return result, files


def output(values):
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as stream:
            for key, value in values.items():
                stream.write(f'{key}={value}\n')
    print(json.dumps(values))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('prepare', 'verify', 'draft'))
    parser.add_argument('--source', default='.')
    parser.add_argument('--directory', type=Path, default=Path('packages'))
    parser.add_argument('--rid', choices=RIDS)
    args = parser.parse_args()
    tag = os.environ['RELEASE_TAG']
    version, assembly = parse_version(tag)
    repo = os.environ.get('GH_REPO', '')
    if args.mode == 'prepare':
        sha = resolve_tag(tag, args.source)
        unpublished(repo, tag)
        output(dict(sha=sha, version=version, assembly_version=assembly))
    elif args.mode == 'verify':
        sha = os.environ['RELEASE_SHA']
        if args.rid:
            path, digest = verify_package(args.directory, tag, sha, args.rid)
            output(dict(package=path.name, sha256=digest))
        else:
            verify_all(args.directory, tag, sha)
    else:
        result, files = upload(repo, tag, os.environ['RELEASE_SHA'], args.directory, args.source,
                               os.environ.get('RELEASE_PRERELEASE', '').lower() == 'true')
        summary = f'## Release draft\n\nTag: `{tag}`\n\nSource: `{os.environ["RELEASE_SHA"]}`\n\nVersion: `{version}`\n\n[Review draft]({result["html_url"]})\n\n'
        summary += '| Package | SHA256 |\n| --- | --- |\n' + ''.join(f'| {p.name} | `{h}` |\n' for p, h in files)
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as stream:
                stream.write(summary)
        output(dict(draft_url=result['html_url']))


if __name__ == '__main__':
    main()
