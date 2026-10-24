"""Test bootstrap: make backend/ importable and pin DATABASE_URL before any module imports it."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"
