import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET


def default_config_path():
    repo_root = Path(__file__).resolve().parents[1]
    net10 = repo_root / "satori-net10.json"
    net11 = repo_root / "satori-net11.json"
    if net10.is_file() and not net11.is_file():
        return net10
    if net11.is_file() and not net10.is_file():
        return net11
    if net10.is_file():
        return net10
    return net11


CONFIG = default_config_path()


def runtime_files(rid):
    if rid.startswith("win-"):
        return ("coreclr.dll", "clrjit.dll", "System.Private.CoreLib.dll")
    extension = "dylib" if rid.startswith("osx-") else "so"
    return (f"libcoreclr.{extension}", f"libclrjit.{extension}", "System.Private.CoreLib.dll")


def digest(path):
    with path.open("rb") as stream:
        if hasattr(hashlib, "file_digest"):
            return hashlib.file_digest(stream, "sha256").hexdigest()
        h = hashlib.sha256()
        while chunk := stream.read(65536):
            h.update(chunk)
        return h.hexdigest()


def check_architecture(path, rid):
    with path.open("rb") as stream:
        header = stream.read(64)
        arm = rid.endswith("-arm64")
        if rid.startswith("win-"):
            if header[:2] != b"MZ":
                raise ValueError(f"Not a PE file: {path}")
            stream.seek(struct.unpack_from("<I", header, 60)[0])
            signature = stream.read(6)
            valid = signature[:4] == b"PE\0\0" and struct.unpack_from("<H", signature, 4)[0] == (0xAA64 if arm else 0x8664)
        elif rid.startswith("linux-"):
            valid = header[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", header, 18)[0] == (183 if arm else 62)
        elif rid.startswith("osx-"):
            valid = header[:4] == b"\xcf\xfa\xed\xfe" and struct.unpack_from("<I", header, 4)[0] == (0x100000C if arm else 0x1000007)
        else:
            raise ValueError(f"Unsupported runtime: {rid}")
        if not valid:
            raise ValueError(f"Wrong native architecture for {rid}: {path}")


def validate(directory, rid, config):
    manifest = json.loads((directory / "satori-manifest.json").read_text())
    expected = {**config, "rid": rid, "schema": 1}
    for key, value in expected.items():
        if manifest.get(key) != value:
            raise ValueError(f"Satori manifest mismatch: {key}")
    names = runtime_files(rid)
    if set(manifest["files"]) != set(names):
        raise ValueError("Incomplete Satori runtime")
    for name in names:
        if digest(directory / name) != manifest["files"][name]:
            raise ValueError(f"Satori checksum mismatch: {name}")
    for name in names[:2]:
        check_architecture(directory / name, rid)


def prepare_source(source, config):
    global_json_path = source / "global.json"
    if global_json_path.is_file():
        global_json = json.loads(global_json_path.read_text())
        global_json["sdk"]["version"] = config["sdk"]
        if "tools" in global_json and "dotnet" in global_json["tools"]:
            global_json["tools"]["dotnet"] = config["sdk"]
        global_json_path.write_text(json.dumps(global_json, indent=2) + "\n")

    override_xml = """
  <PropertyGroup>
    <NuGetAudit>false</NuGetAudit>
    <NoWarn>$(NoWarn);NU1901;NU1902;NU1903;NU1904</NoWarn>
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
  </PropertyGroup>
</Project>"""
    for filename in ("Directory.Build.props", "Directory.Build.targets"):
        filepath = source / filename
        if filepath.is_file():
            content = filepath.read_text(encoding="utf-8")
            if "</Project>" in content and "<NuGetAudit>false</NuGetAudit>" not in content:
                content = content.replace("</Project>", override_xml)
                filepath.write_text(content, encoding="utf-8")


def pack(source, output, rid, config):
    commit = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if commit != config["commit"]:
        raise ValueError("Unexpected Satori source commit")
    versions = ET.parse(source / "eng" / "Versions.props")
    if int(versions.findtext(".//MajorVersion")) != config["runtime_major"]:
        raise ValueError("Wrong Satori runtime major version")
    platform = {"win": "windows", "linux": "linux", "osx": "osx"}[rid.split("-")[0]]
    binaries = source / "artifacts" / "bin" / "coreclr" / f"{platform}.{rid.split('-')[1]}.Release"
    names = runtime_files(rid)
    for name in names:
        if not (binaries / name).is_file():
            raise ValueError(f"Missing Satori output: {binaries / name}")
    output.mkdir(parents=True, exist_ok=True)
    for name in names:
        shutil.copy2(binaries / name, output / name)
    repo_root = Path(__file__).resolve().parents[2]
    shutil.copy2(repo_root / "satori-runtime" / "SATORI-LICENSE.txt", output / "SATORI-LICENSE.txt")
    manifest = {**config, "schema": 1, "rid": rid, "files": {name: digest(output / name) for name in names}}
    (output / "satori-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    validate(output, rid, config)


def overlay(directory, publish, rid, config):
    validate(directory, rid, config)
    configs = list(publish.glob("*.runtimeconfig.json"))
    expected_tfm = f"net{config['runtime_major']}.0"
    if len(configs) != 1 or json.loads(configs[0].read_text())["runtimeOptions"]["tfm"] != expected_tfm:
        raise ValueError(f"Refusing Satori {config['runtime_major']} overlay on a different runtime")
    for name in (*runtime_files(rid), "satori-manifest.json", "SATORI-LICENSE.txt"):
        shutil.copy2(directory / name, publish / name)
    validate(publish, rid, config)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("pack", "verify", "overlay", "prepare"))
    parser.add_argument("rid", choices=("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--config", type=Path)
    args = parser.parse_args()
    config_path = args.config if args.config else CONFIG
    configuration = json.loads(config_path.read_text())
    if args.action == "prepare":
        prepare_source(args.directory, configuration)
    elif args.action == "pack":
        pack(args.directory, args.output, args.rid, configuration)
    elif args.action == "overlay":
        overlay(args.directory, args.output, args.rid, configuration)
    else:
        validate(args.directory, args.rid, configuration)
    print(f"Satori {configuration['runtime_major']} {args.action}: {args.rid} OK")
