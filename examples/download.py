#!/usr/bin/env python3
"""Download a specified Netdisk file using an existing BaiduPCS.NET profile."""

import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("remote_file", help="Absolute Netdisk path to a single file")
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("--baidupcs", default="baidupcs", help="Executable name or path")
    args = parser.parse_args()

    remote = args.remote_file
    if (
        not remote.startswith("/")
        or any(part in {"", ".", ".."} for part in remote[1:].split("/"))
        or any(char in remote for char in "*?\\")
        or any(part.startswith("!(") for part in remote.split("/"))
    ):
        parser.error("Specify an absolute file path without wildcards or relative components.")

    metadata = subprocess.run(
        [args.baidupcs, "metadata", "--json", remote],
        check=True,
        stdout=subprocess.PIPE,
        encoding="utf-8",
    )
    entries = json.loads(metadata.stdout)
    if len(entries) != 1 or entries[0]["path"] != remote or entries[0]["isDirectory"]:
        parser.error("The path must identify exactly one Netdisk file.")

    subprocess.run(
        [args.baidupcs, "download", "--output-directory", str(args.output_directory), remote],
        check=True,
    )


if __name__ == "__main__":
    main()
