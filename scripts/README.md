# ADB-Explorer Scripts

This directory contains utility scripts for working with ADB platform-tools.

## verify_adb_download.py

A Python script to verify the integrity of downloaded ADB platform-tools packages by computing and comparing checksums.

### Requirements
- Python 3.6 or higher

### Usage

Basic verification (computes all checksums):
```bash
python verify_adb_download.py platform-tools_r35.0.2-windows.zip
```

Verify against a known hash:
```bash
python verify_adb_download.py platform-tools_r35.0.2-windows.zip --expected-hash abc123def456...
```

Compute only a specific algorithm:
```bash
python verify_adb_download.py platform-tools_r35.0.2-windows.zip --algorithm sha256
```

Fetch all available releases and compute missing SHA-256 values:
```bash
python verify_adb_download.py --fetch-missing-sha256
```

Preview missing SHA-256 entries without downloading:
```bash
python verify_adb_download.py --fetch-missing-sha256 --dry-run
```

Update the official list file with computed SHA-256 values:
```bash
python verify_adb_download.py --fetch-missing-sha256 --update-official-list
```

Append new releases (when not already present in the official list):
```bash
python verify_adb_download.py --fetch-missing-sha256 --append-new-releases
```

Check only for new stable versions (non-RC) not listed in `OFFICIAL_ADB_VERSIONS.md`:
```bash
python verify_adb_download.py --check-new-only
```

### Example Output

```
Verifying: platform-tools_r35.0.2-windows.zip
File size: 12.34 MB

Computing MD5...
MD5:    abc123def456...
Computing SHA-1...
SHA-1:  def456ghi789...
Computing SHA-256...
SHA-256: ghi789jkl012...

Verification complete!
```

### Available Options

- `file_path` - Path to the platform-tools zip file (required)
- `--expected-hash` - Expected hash value to compare against (optional)
- `--algorithm` - Hash algorithm to use: `md5`, `sha1`, `sha256`, or `all` (default: `all`)
- `--fetch-missing-sha256` - Fetch all available releases and compute missing SHA-256 hashes
- `--dry-run` - List missing SHA-256 entries without downloading
- `--update-official-list` - Update `OFFICIAL_ADB_VERSIONS.md` with newly computed SHA-256 values
- `--append-new-releases` - Append new releases to `OFFICIAL_ADB_VERSIONS.md` when missing
- Release dates for newly appended entries are pulled from the official platform-tools release notes when available.
 - The fetcher also uses the release notes list to discover older releases (down to 24.0.4) that aren't in `OFFICIAL_ADB_VERSIONS.md` yet.
 - If a download fails with 404, the script will try alternate URL patterns (e.g., `platform-tools_35.0.2` in addition to `platform-tools_r35.0.2`).
- `--normalize-official-list` - Sort versions and normalize URLs to the existing Google download pattern (r vs non-r).
- `--sync-official-list` - Rebuild the list using Google release notes only (removes versions not listed, such as 34.0.2 if absent).
- `--check-new-only` - Print new stable versions not listed in `OFFICIAL_ADB_VERSIONS.md` and exit non-zero if any are found.
- `--platform` - Filter releases by platform (default: `windows`)
- `--official-list` - Path to `OFFICIAL_ADB_VERSIONS.md` (default: repo root)

### Notes

- Always verify downloads from official sources
- Compare computed hashes with values from `OFFICIAL_ADB_VERSIONS.md`
- **Prefer SHA-256 for verification** - MD5 is cryptographically broken and should only be used for legacy compatibility with older ADB versions (pre-r28) that only provide MD5 checksums
- For security-critical applications, always use SHA-256 or stronger hash algorithms
