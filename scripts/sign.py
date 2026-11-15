"""Sign and verify a bundle on its native host. No credentials are stored in the repository."""
import os
import platform
import subprocess
from pathlib import Path


def run(*args):
    subprocess.run([str(a) for a in args], check=True)


def sign(bundle: Path, system: str):
    if system == 'win':
        certificate = os.environ.get('PICKERHUB_WINDOWS_CERT_SHA1')
        if not certificate:
            raise RuntimeError('Release requires PICKERHUB_WINDOWS_CERT_SHA1 for a certificate in the Windows certificate store.')
        for name in ('PickerHub.exe', 'picker_capture.dll', 'pickerhub_core.dll'):
            file = bundle / name
            run('signtool', 'sign', '/sha1', certificate, '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256', file)
            run('signtool', 'verify', '/pa', '/all', file)
    elif system == 'osx':
        identity = os.environ.get('PICKERHUB_APPLE_IDENTITY')
        profile = os.environ.get('PICKERHUB_NOTARY_PROFILE')
        if not identity or not profile:
            raise RuntimeError('Release requires PICKERHUB_APPLE_IDENTITY and PICKERHUB_NOTARY_PROFILE in the macOS keychain.')
        entitlement = Path(__file__).parent / 'macos.entitlements'
        runtime = bundle / 'Contents/MacOS'
        # Sign nested code before the enclosing app. Preserve runtime's existing third-party code.
        for file in sorted(runtime.rglob('*')):
            if file.is_file() and (file.suffix == '.dylib' or file.name == 'PickerHub'):
                run('codesign', '--force', '--timestamp', '--options', 'runtime', '--entitlements', entitlement, '--sign', identity, file)
        run('codesign', '--force', '--timestamp', '--options', 'runtime', '--entitlements', entitlement, '--sign', identity, bundle)
        run('codesign', '--verify', '--deep', '--strict', '--verbose=2', bundle)
        archive = bundle.with_suffix('.notarize.zip')
        try:
            run('ditto', '-c', '-k', '--keepParent', bundle, archive)
            run('xcrun', 'notarytool', 'submit', archive, '--keychain-profile', profile, '--wait')
            run('xcrun', 'stapler', 'staple', bundle)
            run('spctl', '--assess', '--type', 'execute', '--verbose', bundle)
        finally:
            archive.unlink(missing_ok=True)
