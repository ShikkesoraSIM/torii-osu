import argparse
import hashlib


VERSION_CODE_BASE = 2_030_000_000
MAX_VERSION_CODE = 2_100_000_000


def android_version_code(run_number: str) -> int:
    if not run_number.isascii() or not run_number.isdecimal() or int(run_number) <= 0:
        raise ValueError("run number must be a positive integer")
    # torii: el piso supera los APK viejos, incluido el 2026101912.
    code = VERSION_CODE_BASE + int(run_number)
    if code > MAX_VERSION_CODE:
        raise ValueError("Android version code limit reached")
    return code


def android_version_hash(display_version: str) -> str:
    if not display_version or display_version != display_version.strip() or display_version.startswith("v"):
        raise ValueError("use ApplicationDisplayVersion (semver), not the release tag")
    return hashlib.md5(f"{display_version}-Android".encode()).hexdigest()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=["version-code", "version-hash"])
    parser.add_argument("value")
    args = parser.parse_args()
    try:
        print(android_version_code(args.value) if args.operation == "version-code" else android_version_hash(args.value))
    except ValueError as error:
        parser.error(str(error))
