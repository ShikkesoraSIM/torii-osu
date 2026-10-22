import json
from pathlib import Path
import struct
import tempfile
import unittest

import satori_runtime as runtime


def native_header(rid):
    header = bytearray(128)
    arm = rid.endswith("-arm64")
    if rid.startswith("win-"):
        header[:2] = b"MZ"
        struct.pack_into("<I", header, 60, 64)
        header[64:68] = b"PE\0\0"
        struct.pack_into("<H", header, 68, 0xAA64 if arm else 0x8664)
    elif rid.startswith("linux-"):
        header[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", header, 18, 183 if arm else 62)
    else:
        header[:4] = b"\xcf\xfa\xed\xfe"
        struct.pack_into("<I", header, 4, 0x100000C if arm else 0x1000007)
    return header


class SatoriRuntimeTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.config = json.loads(runtime.CONFIG.read_text())

    def payload(self, rid):
        directory = self.root / rid
        directory.mkdir()
        names = runtime.runtime_files(rid)
        for name in names[:2]:
            (directory / name).write_bytes(native_header(rid))
        (directory / names[2]).write_bytes(b"corelib")
        (directory / "SATORI-LICENSE.txt").write_text("license")
        manifest = {**self.config, "schema": 1, "rid": rid, "files": {name: runtime.digest(directory / name) for name in names}}
        (directory / "satori-manifest.json").write_text(json.dumps(manifest))
        return directory

    def test_all_native_architectures(self):
        for rid in ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"):
            with self.subTest(rid=rid):
                runtime.validate(self.payload(rid), rid, self.config)

    def test_pin_matches_sdk(self):
        global_json = json.loads((runtime.CONFIG.parent.parent / "global.json").read_text())
        self.assertEqual(global_json["sdk"]["version"], self.config["sdk"])
        self.assertEqual(self.config["runtime_major"], 11)

    def test_changed_binary_rejected(self):
        directory = self.payload("win-x64")
        (directory / "coreclr.dll").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "checksum"):
            runtime.validate(directory, "win-x64", self.config)

    def test_other_source_commit_rejected(self):
        directory = self.payload("linux-x64")
        with self.assertRaisesRegex(ValueError, "commit"):
            runtime.validate(directory, "linux-x64", {**self.config, "commit": "0" * 40})

    def test_other_rid_rejected(self):
        directory = self.payload("osx-x64")
        with self.assertRaisesRegex(ValueError, "rid"):
            runtime.validate(directory, "osx-arm64", self.config)

    def test_wrong_machine_rejected_even_with_valid_checksum(self):
        directory = self.payload("win-x64")
        (directory / "coreclr.dll").write_bytes(native_header("win-arm64"))
        path = directory / "satori-manifest.json"
        manifest = json.loads(path.read_text())
        manifest["files"]["coreclr.dll"] = runtime.digest(directory / "coreclr.dll")
        path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "architecture"):
            runtime.validate(directory, "win-x64", self.config)

    def test_missing_runtime_file_rejected(self):
        directory = self.payload("linux-arm64")
        path = directory / "satori-manifest.json"
        manifest = json.loads(path.read_text())
        del manifest["files"]["System.Private.CoreLib.dll"]
        path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            runtime.validate(directory, "linux-arm64", self.config)

    def test_overlay_does_not_touch_net10(self):
        directory = self.payload("win-x64")
        publish = self.root / "publish"
        publish.mkdir()
        (publish / "torii.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": "net10.0"}}))
        (publish / "coreclr.dll").write_bytes(b"original")
        with self.assertRaisesRegex(ValueError, "different runtime"):
            runtime.overlay(directory, publish, "win-x64", self.config)
        self.assertEqual((publish / "coreclr.dll").read_bytes(), b"original")

    def test_net11_overlay_verified(self):
        directory = self.payload("osx-arm64")
        publish = self.root / "publish"
        publish.mkdir()
        (publish / "torii.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": "net11.0"}}))
        runtime.overlay(directory, publish, "osx-arm64", self.config)
        runtime.validate(publish, "osx-arm64", self.config)


if __name__ == "__main__":
    unittest.main()
