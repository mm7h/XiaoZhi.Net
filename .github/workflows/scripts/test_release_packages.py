import io
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import release_packages as release

TAG = 'v0.2.0-rc.1'
SHA = 'a' * 40


def binary(rid):
    data = bytearray(128)
    if rid.startswith('win-'):
        data[:2] = b'MZ'
        struct.pack_into('<I', data, 60, 64)
        data[64:68] = b'PE\0\0'
        struct.pack_into('<H', data, 68, 0x8664 if rid.endswith('x64') else 0xAA64)
    else:
        data[:6] = b'\x7fELF\x02\x01'
        struct.pack_into('<H', data, 18, 62 if rid.endswith('x64') else 183)
    return bytes(data)


def package(directory, rid, change=None):
    version, assembly = release.parse_version(TAG)
    extension = '.exe' if rid.startswith('win-') else ''
    files = dict.fromkeys(('LICENSE', 'README.md', 'server/Configs/config.json',
                          'server/Configs/assets/bind_codes/0.wav', 'ota/appsettings.json',
                          'docker/compose.yaml', 'docker/Dockerfile.release', 'docker/deploy.sh',
                          'scripts/Download-SherpaModels.sh', 'scripts/SherpaModels.psv',
                          'docs/03.server-development.md'), b'test')
    files['package.json'] = json.dumps(dict(Version=version, RuntimeIdentifier=rid, Tag=TAG, SourceCommit=SHA)).encode()
    files['assembly-versions.json'] = json.dumps([
        dict(Name=name, AssemblyVersion=assembly, FileVersion=assembly, InformationalVersion=version)
        for name in ('XiaoZhi.Net.Sample.Server', 'XiaoZhi.Net.Sample.OTA.Server', 'XiaoZhi.Net.Server')]).encode()
    for name in ('server/XiaoZhi.Net.Sample.Server', 'ota/XiaoZhi.Net.Sample.OTA.Server'):
        files[name + extension] = binary(rid)
    if change:
        change(files)
    archive = directory / f'XiaoZhi.Net-{version}-{rid}.zip'
    with zipfile.ZipFile(archive, 'w') as stream:
        for name, data in files.items():
            stream.writestr(name, data)
    Path(str(archive) + '.sha256').write_text(f'{release.sha256(archive)}  {archive.name}\n', encoding='utf-8')
    return archive


class VersionTests(unittest.TestCase):
    def test_release_and_prerelease_mapping(self):
        for tag, version in [('0.2.0', '0.2.0'), ('v0.2.0', '0.2.0'), (TAG, '0.2.0-rc.1')]:
            self.assertEqual(release.parse_version(tag), (version, '0.2.0.0'))

    def test_invalid_versions(self):
        for tag in ('resources', 'ci-release-test-1', '', '0.2', '01.2.3', '0.2.3.4',
                    '0.2.0-01', '0.2.0-rc.01', '0.2.0+', '0.2.0+abc', '65535.0.0',
                    '0.2.0\n', '--help', '../0.2.0', '0.2.0;echo'):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.parse_version(tag)

    def test_test_tag_is_a_valid_version(self):
        self.assertEqual(release.parse_version('0.0.0-ci-release-test.20261013.1')[1], '0.0.0.0')


class GitTests(unittest.TestCase):
    def test_lightweight_annotated_and_missing_remote_tags(self):
        with tempfile.TemporaryDirectory() as directory:
            origin, checkout = Path(directory) / 'origin', Path(directory) / 'checkout'
            origin.mkdir()
            release.command('git', 'init', str(origin))
            release.command('git', '-C', str(origin), 'config', 'user.name', 'Release tests')
            release.command('git', '-C', str(origin), 'config', 'user.email', 'release-tests@example.invalid')
            release.command('git', '-C', str(origin), 'commit', '--allow-empty', '-m', 'Initial')
            sha = release.command('git', '-C', str(origin), 'rev-parse', 'HEAD')
            release.command('git', '-C', str(origin), 'tag', '0.2.0')
            release.command('git', '-C', str(origin), 'tag', '-a', 'v0.2.0', '-m', 'Annotated')
            release.command('git', 'clone', '--no-tags', str(origin), str(checkout))
            self.assertEqual(release.resolve_tag('0.2.0', checkout), sha)
            self.assertEqual(release.resolve_tag('v0.2.0', checkout), sha)
            with self.assertRaises(subprocess.CalledProcessError):
                release.resolve_tag('0.9.0', checkout)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_all_four_architectures(self):
        for rid in release.RIDS:
            package(self.directory, rid)
        self.assertEqual(len(release.verify_all(self.directory, TAG, SHA)), 4)

    def test_missing_architecture_or_extra_file(self):
        for rid in release.RIDS[:3]:
            package(self.directory, rid)
        with self.assertRaises(ValueError):
            release.verify_all(self.directory, TAG, SHA)
        package(self.directory, release.RIDS[3])
        (self.directory / 'unexpected.txt').touch()
        with self.assertRaises(ValueError):
            release.verify_all(self.directory, TAG, SHA)

    def test_corrupt_archive_and_checksum(self):
        archive = package(self.directory, 'linux-x64')
        with open(archive, 'ab') as stream:
            stream.write(b'changed')
        with self.assertRaises(ValueError):
            release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_source_or_version_mismatch(self):
        for field in ('Version', 'Tag', 'SourceCommit', 'RuntimeIdentifier'):
            def change(files):
                data = json.loads(files['package.json'])
                data[field] = 'wrong'
                files['package.json'] = json.dumps(data).encode()
            package(self.directory, 'linux-x64', change)
            with self.subTest(field=field), self.assertRaises(ValueError):
                release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_wrong_assembly_version(self):
        def change(files):
            records = json.loads(files['assembly-versions.json'])
            records[0]['AssemblyVersion'] = '0.1.0.0'
            files['assembly-versions.json'] = json.dumps(records).encode()
        package(self.directory, 'linux-x64', change)
        with self.assertRaises(ValueError):
            release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_wrong_architecture(self):
        def change(files):
            files['server/XiaoZhi.Net.Sample.Server'] = binary('linux-arm64')
        package(self.directory, 'linux-x64', change)
        with self.assertRaises(ValueError):
            release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_user_data_and_unsafe_paths(self):
        for name in ('server/models/model.onnx', 'server/data/user.db', 'server/logs/user.log', '../escape', 'C:/escape'):
            package(self.directory, 'linux-x64', lambda files: files.update({name: b'bad'}))
            with self.subTest(name=name), self.assertRaises(ValueError):
                release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_missing_required_content(self):
        package(self.directory, 'linux-x64', lambda files: files.pop('server/Configs/config.json'))
        with self.assertRaises(ValueError):
            release.verify_package(self.directory, TAG, SHA, 'linux-x64')

    def test_duplicate_entries(self):
        archive = package(self.directory, 'linux-x64')
        with zipfile.ZipFile(archive, 'a') as stream:
            stream.writestr('LICENSE', b'duplicate')
        Path(str(archive) + '.sha256').write_text(f'{release.sha256(archive)}  {archive.name}')
        with self.assertRaises(ValueError):
            release.verify_package(self.directory, TAG, SHA, 'linux-x64')


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        for rid in release.RIDS:
            package(self.directory, rid)
        self.draft = dict(id=42, tag_name=TAG, draft=True, prerelease=True, name='Title', body='Edited notes', assets=[], html_url='https://example.com/draft')

    def tearDown(self):
        self.temp.cleanup()

    def api(self, path):
        return self.draft.copy()

    def command(self, *args, **kwargs):
        if args[:3] == ('gh', 'release', 'upload'):
            path = Path(args[4])
            self.draft['assets'] = [a for a in self.draft['assets'] if a['name'] != path.name]
            self.draft['assets'].append(dict(name=path.name, state='uploaded', size=path.stat().st_size, digest='sha256:' + release.sha256(path)))
        return ''

    def test_find_draft_from_authenticated_paginated_listing(self):
        with patch.object(release, 'api', side_effect=[[dict(tag_name='other')] * 100, [self.draft]]):
            self.assertEqual(release.find_release('owner/repo', TAG)['id'], 42)

    def test_api_errors_are_not_treated_as_absent(self):
        with patch.object(release, 'api', side_effect=RuntimeError('Forbidden')):
            with self.assertRaises(RuntimeError):
                release.find_release('owner/repo', TAG)

    def test_published_release_rejected_without_write(self):
        self.draft['draft'] = False
        with patch.object(release, 'find_release', return_value=self.draft), patch.object(release, 'command') as writes, patch.object(release, 'resolve_tag', return_value=SHA):
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            writes.assert_not_called()

    def test_moved_tag_rejected_without_write(self):
        with patch.object(release, 'resolve_tag', return_value='b' * 40), patch.object(release, 'command') as writes:
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            writes.assert_not_called()

    def test_missing_architecture_rejected_without_write(self):
        next(self.directory.glob('*win-arm64.zip')).unlink()
        with patch.object(release, 'command') as writes:
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            writes.assert_not_called()

    def test_create_draft_and_upload_five_assets(self):
        with patch.object(release, 'resolve_tag', return_value=SHA), patch.object(release, 'unpublished', return_value=None) as lookup, patch.object(release, 'create_draft', return_value=self.draft) as create, patch.object(release, 'api', side_effect=self.api), patch.object(release, 'command', side_effect=self.command):
            result, _ = release.upload('owner/repo', TAG, SHA, self.directory, '.')
            self.assertTrue(result['draft'])
            self.assertEqual(len(result['assets']), 5)
            lookup.assert_called_once()
            create.assert_called_once_with('owner/repo', TAG, SHA, False)

    def test_draft_creation_uses_returned_id_and_generated_notes(self):
        with patch.object(release, 'command', side_effect=[json.dumps({'name':'Generated title','body':'Generated notes'}), json.dumps(self.draft)]) as calls:
            result = release.create_draft('owner/repo', TAG, SHA, False)
            self.assertEqual(result['id'], 42)
            payload = json.loads(calls.call_args_list[1].kwargs['input'])
            self.assertTrue(payload['draft'])
            self.assertTrue(payload['prerelease'])
            self.assertEqual(payload['tag_name'], TAG)
            self.assertEqual(payload['target_commitish'], SHA)
            self.assertEqual(payload['body'], 'Generated notes')

    def test_creation_response_must_identify_an_unpublished_draft(self):
        with patch.object(release, 'command', side_effect=[json.dumps({}),json.dumps({**self.draft,'draft':False})]):
            with self.assertRaises(ValueError):
                release.create_draft('owner/repo', TAG, SHA, False)

    def test_rerun_preserves_metadata_and_unrelated_assets(self):
        self.draft['assets'].append(dict(name='user-notes.txt'))
        with patch.object(release, 'resolve_tag', return_value=SHA), patch.object(release, 'unpublished', return_value=self.draft), patch.object(release, 'api', side_effect=self.api), patch.object(release, 'command', side_effect=self.command):
            for _ in range(2):
                result, _ = release.upload('owner/repo', TAG, SHA, self.directory, '.')
                self.assertEqual(len(result['assets']), 6)
                self.assertEqual((result['name'], result['body']), ('Title', 'Edited notes'))
                self.assertTrue(result['draft'])

    def test_upload_failure_leaves_unpublished_draft(self):
        with patch.object(release, 'resolve_tag', return_value=SHA), patch.object(release, 'unpublished', return_value=self.draft), patch.object(release, 'api', side_effect=self.api), patch.object(release, 'command', side_effect=RuntimeError('upload interrupted')):
            with self.assertRaises(RuntimeError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            self.assertTrue(self.draft['draft'])

    def test_tag_moved_during_upload_stops_subsequent_writes(self):
        with patch.object(release, 'resolve_tag', side_effect=[SHA, SHA, 'b' * 40]), patch.object(release, 'unpublished', return_value=self.draft), patch.object(release, 'api', side_effect=self.api), patch.object(release, 'command', side_effect=self.command) as writes:
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            self.assertEqual(writes.call_count, 1)
            self.assertTrue(self.draft['draft'])

    def test_release_published_before_upload_stops_writes(self):
        with patch.object(release, 'resolve_tag', return_value=SHA), patch.object(release, 'unpublished', return_value=self.draft), patch.object(release, 'api', return_value={**self.draft, 'draft': False}), patch.object(release, 'command') as writes:
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')
            writes.assert_not_called()

    def test_incomplete_upload_detected(self):
        with patch.object(release, 'resolve_tag', return_value=SHA), patch.object(release, 'unpublished', return_value=self.draft), patch.object(release, 'api', side_effect=self.api), patch.object(release, 'command', return_value=''):
            with self.assertRaises(ValueError):
                release.upload('owner/repo', TAG, SHA, self.directory, '.')


if __name__ == '__main__':
    unittest.main()
