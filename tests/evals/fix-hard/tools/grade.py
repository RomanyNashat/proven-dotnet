"""Kept for the old command line: grades this fixture with the shared grader.
    python3 grade.py <results folder>
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "tools"))
import grade  # noqa: E402

if __name__ == "__main__":
    sys.exit(grade.main([str(pathlib.Path(__file__).resolve().parents[1]), *sys.argv[1:]]))
