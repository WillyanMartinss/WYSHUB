import pathlib
import unittest


class InstallerDependenciesTest(unittest.TestCase):
    def test_script_uses_requirements_file_from_app_dir(self):
        script = pathlib.Path(r"c:\Users\willy\Documents\WYSHUB\installer\WYSHUB.iss").read_text(encoding="utf-8")
        self.assertIn("requirements.txt", script)
        self.assertIn("{app}\\requirements.txt", script)


if __name__ == "__main__":
    unittest.main()
