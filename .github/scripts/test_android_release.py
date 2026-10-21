import unittest

from android_release import MAX_VERSION_CODE, VERSION_CODE_BASE, android_version_code, android_version_hash


class AndroidReleaseTest(unittest.TestCase):
    def test_existing_apks_can_upgrade(self):
        self.assertGreater(android_version_code("1"), 2_026_101_912)

    def test_counter_increases_between_streams_and_dates(self):
        self.assertLess(android_version_code("9"), android_version_code("10"))
        self.assertLess(android_version_code("99"), android_version_code("100"))

    def test_rerunning_the_same_workflow_keeps_the_code(self):
        self.assertEqual(android_version_code("1234"), android_version_code("1234"))

    def test_android_limit(self):
        self.assertEqual(android_version_code(str(MAX_VERSION_CODE - VERSION_CODE_BASE)), MAX_VERSION_CODE)
        with self.assertRaises(ValueError):
            android_version_code(str(MAX_VERSION_CODE - VERSION_CODE_BASE + 1))

    def test_bad_counter(self):
        for value in ["", "0", "-1", "1.2", "abc", " 1", "1 ", "\u0661"]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                android_version_code(value)

    def test_runtime_hashes_from_current_apks(self):
        self.assertEqual(android_version_hash("2026.1020.0-torii"), "489ae12dcbbe653ca57b690b0cead78b")
        self.assertEqual(android_version_hash("2026.1020.1-nova"), "5170ff44b44c4bd1dc29e7aabde4a9ae")

    def test_tags_cannot_silently_register_a_different_hash(self):
        for value in ["v2026.1020.0-torii", "v2026.1020.1-nova", "", " 2026.1020.0-torii"]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                android_version_hash(value)


if __name__ == "__main__":
    unittest.main()
