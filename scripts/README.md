# ADB-Explorer Scripts

This directory contains utility scripts for working with ADB platform-tools.

## verify_adb_download.py

A Python script to verify the integrity of downloaded ADB platform-tools packages by computing and comparing checksums.

### Requirements
- Python 3.6 or higher
- No external dependencies (uses standard library only)

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

### Notes

- Always verify downloads from official sources
- Compare computed hashes with values from `OFFICIAL_ADB_VERSIONS.md`
- **Prefer SHA-256 for verification** - MD5 is cryptographically broken and should only be used for legacy compatibility with older ADB versions (pre-r28) that only provide MD5 checksums
- For security-critical applications, always use SHA-256 or stronger hash algorithms
