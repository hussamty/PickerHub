"""Run after a local package build: clean extraction, content hashes and signing gates."""
import hashlib
import importlib.util
import json
import os
import platform
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ph_sign', ROOT / 'scripts/sign.py')
signing = importlib.util.module_from_spec(spec); spec.loader.exec_module(signing)

class ReleaseChecks(unittest.TestCase):
    def test_signing_requires_credentials(self):
        with patch.dict(os.environ, {}, clear=True):
            for host in ('win', 'osx'):
                with self.assertRaises(RuntimeError): signing.sign(Path('/unused'), host)
    def test_windows_signing_verifies_each_binary(self):
        with patch.dict(os.environ, {'PICKERHUB_WINDOWS_CERT_SHA1':'test-thumbprint'}), patch.object(signing, 'run') as run:
            signing.sign(Path('/unused'), 'win')
            self.assertEqual(len(run.call_args_list), 6)
            self.assertEqual(sum(call.args[1] == 'verify' for call in run.call_args_list), 3)
    @unittest.skipUnless(platform.system() == 'Linux', 'Linux archive test')
    def test_clean_package(self):
        version = json.loads((ROOT/'dist/native/PickerHub-0.5.0-linux-x64.manifest.json').read_text())
        with tempfile.TemporaryDirectory(prefix='pickerhub-package-') as tmp:
            with tarfile.open(ROOT/'dist/native/PickerHub-0.5.0-linux-x64.tar.gz') as archive:
                archive.extractall(tmp, filter='data')
            runtime = Path(tmp)/'PickerHub'
            for name, checksum in version['files'].items():
                self.assertEqual(hashlib.sha256((runtime/name).read_bytes()).hexdigest(), checksum, name)
            subprocess.run([runtime/'PickerHub','--self-test'], check=True, timeout=30)

if __name__ == '__main__': unittest.main()
