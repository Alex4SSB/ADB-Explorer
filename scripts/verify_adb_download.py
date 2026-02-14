#!/usr/bin/env python3
"""
ADB Platform-Tools Download Verifier

This script helps verify the integrity of downloaded ADB platform-tools packages
by computing their checksums and comparing them with known values.

Note: MD5 is cryptographically broken and should only be used for legacy
compatibility with older ADB versions. Use SHA-256 when available.

Usage:
    python verify_adb_download.py <file_path>
    python verify_adb_download.py <file_path> --expected-hash <hash>

Examples:
    python verify_adb_download.py platform-tools_r35.0.2-windows.zip
    python verify_adb_download.py platform-tools_r35.0.2-windows.zip --expected-hash abc123...
"""

import argparse
import hashlib
import os
import re
import sys
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

# Use larger chunk size for better I/O performance
CHUNK_SIZE = 65536  # 64KB

# Official Android repository metadata sources
REPOSITORY_XML_URLS = (
    "https://dl.google.com/android/repository/repository2-3.xml",
    "https://dl.google.com/android/repository/repository2-2.xml",
)


def compute_md5(file_path):
    """Compute MD5 hash of a file.
    
    Note: MD5 is cryptographically broken. Use only for legacy compatibility.
    """
    hash_md5 = hashlib.md5()
    try:
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(CHUNK_SIZE), b""):
                hash_md5.update(chunk)
        return hash_md5.hexdigest()
    except FileNotFoundError:
        print(f"Error: File not found: {file_path}")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file: {e}")
        sys.exit(1)


def compute_sha256(file_path):
    """Compute SHA-256 hash of a file."""
    hash_sha256 = hashlib.sha256()
    try:
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(CHUNK_SIZE), b""):
                hash_sha256.update(chunk)
        return hash_sha256.hexdigest()
    except FileNotFoundError:
        print(f"Error: File not found: {file_path}")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file: {e}")
        sys.exit(1)


def compute_sha1(file_path):
    """Compute SHA-1 hash of a file."""
    hash_sha1 = hashlib.sha1()
    try:
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(CHUNK_SIZE), b""):
                hash_sha1.update(chunk)
        return hash_sha1.hexdigest()
    except FileNotFoundError:
        print(f"Error: File not found: {file_path}")
        sys.exit(1)
    except Exception as e:
        print(f"Error reading file: {e}")
        sys.exit(1)


def get_file_size(file_path):
    """Get file size in bytes."""
    try:
        return os.path.getsize(file_path)
    except Exception as e:
        print(f"Error getting file size: {e}")
        return None


def format_file_size(size_bytes):
    """Format file size in human-readable format."""
    for unit in ['B', 'KB', 'MB', 'GB']:
        if size_bytes < 1024.0:
            return f"{size_bytes:.2f} {unit}"
        size_bytes /= 1024.0
    return f"{size_bytes:.2f} TB"


def get_repo_root():
    """Get repository root based on script location."""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(script_dir, os.pardir))


def read_official_versions(file_path):
    """Parse OFFICIAL_ADB_VERSIONS.md and return URL -> SHA-256 mapping."""
    entries = read_official_entries(file_path)
    return {entry["url"]: entry.get("sha256") for entry in entries}


def read_official_entries(file_path):
    """Parse OFFICIAL_ADB_VERSIONS.md and return entries with version/date/url/sha256."""
    entries = []
    if not os.path.isfile(file_path):
        return entries

    header_pattern = re.compile(r"^####\s+Platform-Tools\s+([0-9.]+(?:\s+RC\d+)?)\s*(?:\(([^)]+)\))?", re.IGNORECASE)
    url_pattern = re.compile(r"^-\s*URL:\s*(.*)$", re.IGNORECASE)
    sha256_pattern = re.compile(r"^-\s*SHA-256:\s*(.*)$", re.IGNORECASE)

    current_version = None
    current_date = None
    current_url = None

    with open(file_path, "r", encoding="utf-8") as file_handle:
        for line in file_handle:
            stripped = line.strip()
            header_match = header_pattern.match(stripped)
            if header_match:
                if current_url:
                    entries.append({
                        "version": current_version,
                        "date": current_date,
                        "url": current_url,
                        "sha256": None,
                    })
                    current_url = None
                current_version = header_match.group(1).strip()
                current_date = header_match.group(2).strip() if header_match.group(2) else None
                continue

            url_match = url_pattern.match(stripped)
            if url_match:
                current_url = url_match.group(1).strip()
                continue

            sha_match = sha256_pattern.match(stripped)
            if sha_match and current_url:
                value = sha_match.group(1).strip()
                if not value or "to be verified" in value.lower():
                    sha_value = "???"
                else:
                    sha_value = value
                entries.append({
                    "version": current_version,
                    "date": current_date,
                    "url": current_url,
                    "sha256": sha_value,
                })
                current_url = None

    if current_url:
        entries.append({
            "version": current_version,
            "date": current_date,
            "url": current_url,
            "sha256": None,
        })

    return entries


def candidate_urls_for_version(version, platform="windows"):
    """Build candidate download URLs for a platform-tools version."""
    if not version or " RC" in version:
        return []
    base_url = "https://dl.google.com/android/repository/"
    suffix_map = {
        "windows": ["windows", "win"],
        "linux": ["linux"],
        "macosx": ["darwin", "macosx"],
    }
    suffixes = suffix_map.get(platform.lower(), [platform.lower()])
    prefixes = ["platform-tools_r", "platform-tools_"]
    urls = []
    for prefix in prefixes:
        for suffix in suffixes:
            urls.append(f"{base_url}{prefix}{version}-{suffix}.zip")
    return urls


def fetch_repository_xml(url):
    """Download and parse Android repository XML."""
    try:
        with urllib.request.urlopen(url) as response:
            data = response.read()
        return ET.fromstring(data)
    except Exception as exc:
        print(f"Error fetching repository metadata from {url}: {exc}")
        return None


def parse_version(revision_element):
    """Build a semantic version string from a revision element."""
    if revision_element is None:
        return None
    major = revision_element.findtext("{*}major")
    minor = revision_element.findtext("{*}minor")
    micro = revision_element.findtext("{*}micro")
    parts = [p for p in (major, minor, micro) if p is not None]
    return ".".join(parts) if parts else None


def parse_platform_tools_from_repo(xml_root, platform="windows"):
    """Extract platform-tools archives for the given platform from repository XML."""
    releases = []
    if xml_root is None:
        return releases

    for package in xml_root.findall(".//{*}remotePackage"):
        if package.get("path") != "platform-tools":
            continue

        version = parse_version(package.find("{*}revision"))
        archives = package.findall(".//{*}archive")
        for archive in archives:
            host_os = archive.findtext("{*}host-os")
            url = archive.findtext(".//{*}url")
            if not url:
                continue
            if host_os:
                if host_os.lower() != platform.lower():
                    continue
            else:
                if platform.lower() == "windows" and "win" not in url.lower():
                    continue
                if platform.lower() != "windows" and platform.lower() not in url.lower():
                    continue
            releases.append({"version": version, "url": f"https://dl.google.com/android/repository/{url}" if not url.startswith("http") else url})

    return releases


def fetch_release_dates():
    """Fetch release dates from the official platform-tools release notes."""
    url = "https://developer.android.com/tools/releases/platform-tools"
    try:
        with urllib.request.urlopen(url) as response:
            html = response.read().decode("utf-8", errors="ignore")
    except Exception as exc:
        print(f"Warning: Unable to fetch release dates from {url}: {exc}")
        return {}

    dates = {}
    patterns = [
        re.compile(r"^####\s+([0-9]+(?:\.[0-9]+){1,2})\s*(?:\(([^)]+)\))?", re.MULTILINE),
        re.compile(r"<h4[^>]*>\s*([0-9]+(?:\.[0-9]+){1,2})\s*(?:\(([^)]+)\))?\s*</h4>", re.IGNORECASE),
    ]
    for pattern in patterns:
        for match in pattern.finditer(html):
            version = match.group(1).strip()
            date = match.group(2).strip() if match.group(2) else None
            if date and version not in dates:
                dates[version] = date
    return dates


def get_available_releases(platform="windows", repo_urls=REPOSITORY_XML_URLS):
    """Fetch available platform-tools releases from official repository XML."""
    releases_by_url = {}
    for repo_url in repo_urls:
        xml_root = fetch_repository_xml(repo_url)
        for entry in parse_platform_tools_from_repo(xml_root, platform=platform):
            releases_by_url[entry["url"]] = entry
    return list(releases_by_url.values())


def download_file(url, dest_path):
    """Download a file to the destination path."""
    try:
        with urllib.request.urlopen(url) as response, open(dest_path, "wb") as out_file:
            while True:
                chunk = response.read(CHUNK_SIZE)
                if not chunk:
                    break
                out_file.write(chunk)
        return True, None
    except urllib.error.HTTPError as exc:
        if os.path.exists(dest_path):
            try:
                os.remove(dest_path)
            except OSError:
                pass
        if exc.code != 404:
            print(f"Error downloading {url}: HTTP {exc.code}")
        return False, exc.code
    except Exception as exc:
        if os.path.exists(dest_path):
            try:
                os.remove(dest_path)
            except OSError:
                pass
        print(f"Error downloading {url}: {exc}")
        return False, None


def download_with_fallback(entry, platform, dest_path):
    """Download using primary URL and fallbacks; return resolved URL on success."""
    candidates = []
    if entry.get("url"):
        candidates.append(entry["url"])
    if entry.get("version"):
        for candidate in candidate_urls_for_version(entry["version"], platform=platform):
            if candidate not in candidates:
                candidates.append(candidate)

    for candidate in candidates:
        success, status = download_file(candidate, dest_path)
        if success:
            if entry.get("url") and entry["url"] != candidate:
                print(f"Note: Falling back to {candidate} (original URL not found)")
            return candidate
        if status is None:
            continue
        if status != 404:
            break

    return None


def url_exists(url):
    """Check if a URL exists using a HEAD request, fallback to GET if needed."""
    request = urllib.request.Request(url, method="HEAD")
    try:
        with urllib.request.urlopen(request) as response:
            return 200 <= response.status < 300
    except urllib.error.HTTPError as exc:
        if exc.code == 405:
            try:
                with urllib.request.urlopen(url) as response:
                    return 200 <= response.status < 300
            except Exception:
                return False
        if exc.code == 404:
            return False
        return False
    except Exception:
        return False


def parse_version_key(version):
    """Convert version string to sortable tuple."""
    if not version:
        return (0, 0, 0)
    version_clean = version.split(" RC")[0]
    parts = []
    for piece in version_clean.split('.'):
        try:
            parts.append(int(piece))
        except ValueError:
            parts.append(0)
    while len(parts) < 3:
        parts.append(0)
    rc_number = 0
    if " RC" in version:
        try:
            rc_number = int(version.split(" RC")[-1])
        except ValueError:
            rc_number = 0
    return tuple(parts + [rc_number])


def normalize_official_versions_file(
    file_path,
    platform="windows",
    release_dates=None,
    strict_google_list=False,
    sha256_by_version=None,
):
    """Sort versions and normalize URLs to the pattern that exists on Google."""
    if not os.path.isfile(file_path):
        print(f"Error: Official versions file not found: {file_path}")
        return False

    release_dates = release_dates or {}
    entries = []
    if not strict_google_list:
        entries = read_official_entries(file_path)
        if not entries:
            return False

    by_version = {}
    for entry in entries:
        version = entry.get("version")
        if not version:
            continue
        current = by_version.get(version, {"version": version})
        if entry.get("date") and not current.get("date"):
            current["date"] = entry["date"]
        if entry.get("url") and not current.get("url"):
            current["url"] = entry["url"]
        if entry.get("sha256") and not current.get("sha256"):
            current["sha256"] = entry["sha256"]
        by_version[version] = current

    if strict_google_list:
        if not release_dates:
            print("Error: Google release list is unavailable for sync.")
            return False
        by_version = {
            version: {"version": version, "date": date_value}
            for version, date_value in release_dates.items()
        }
        if sha256_by_version:
            for version, sha_value in sha256_by_version.items():
                if version in by_version:
                    by_version[version]["sha256"] = sha_value
    else:
        for version, entry in by_version.items():
            if not entry.get("date"):
                entry["date"] = release_dates.get(version)

    normalized_entries = []
    for version in sorted(by_version.keys(), key=parse_version_key, reverse=True):
        entry = by_version[version]
        url = entry.get("url")
        selected_url = None
        if url and url_exists(url):
            selected_url = url
        else:
            if " RC" not in version:
                for candidate in candidate_urls_for_version(version, platform=platform):
                    if url_exists(candidate):
                        selected_url = candidate
                        break
        if selected_url:
            entry["url"] = selected_url
        normalized_entries.append(entry)

    with open(file_path, "r", encoding="utf-8") as file_handle:
        lines = file_handle.readlines()

    start_idx = None
    end_idx = None
    for idx, line in enumerate(lines):
        normalized = line.strip().lower()
        if normalized == "### latest stable release":
            start_idx = idx + 1
        if normalized.startswith("### older versions"):
            end_idx = idx
            break

    if start_idx is None:
        for idx, line in enumerate(lines):
            if line.strip().lower() == "## version history":
                start_idx = idx + 1
                break

    if end_idx is None:
        for idx, line in enumerate(lines):
            if line.strip().lower() == "## how to verify downloads":
                end_idx = idx
                break

    if start_idx is None or end_idx is None:
        print("Error: Could not locate version list section in OFFICIAL_ADB_VERSIONS.md")
        return False

    while start_idx < end_idx and lines[start_idx].strip() == "":
        start_idx += 1

    platform_label = format_platform_label(platform)
    blocks = []
    for entry in normalized_entries:
        date_value = entry.get("date") or "Unknown date"
        url_value = entry.get("url") or "(no official URL found)"
        sha_value = entry.get("sha256") or "To be verified by downloading from official source"
        blocks.extend([
            f"#### Platform-Tools {entry['version']} ({date_value})\n",
            f"**{platform_label}:**\n",
            f"- URL: {url_value}\n",
            f"- SHA-256: {sha_value}\n",
            "\n",
            "---\n",
            "\n",
        ])

    lines[start_idx:end_idx] = blocks
    with open(file_path, "w", encoding="utf-8") as file_handle:
        file_handle.writelines(lines)
    return True


def update_official_versions_file(file_path, updates, updates_by_version=None, url_updates=None, url_updates_by_version=None):
    """Update SHA-256 and URL entries in OFFICIAL_ADB_VERSIONS.md for matching URLs or versions."""
    if not updates and not updates_by_version and not url_updates and not url_updates_by_version:
        return 0
    if not os.path.isfile(file_path):
        print(f"Error: Official versions file not found: {file_path}")
        return 0

    url_pattern = re.compile(r"^-\s*URL:\s*(.*)$", re.IGNORECASE)
    sha_pattern = re.compile(r"^-\s*SHA-256:\s*(.*)$", re.IGNORECASE)
    header_pattern = re.compile(r"^####\s+Platform-Tools\s+([0-9.]+)", re.IGNORECASE)
    updated_lines = []
    current_url = None
    current_version = None
    updated = 0

    with open(file_path, "r", encoding="utf-8") as file_handle:
        for line in file_handle:
            stripped = line.strip()
            header_match = header_pattern.match(stripped)
            if header_match:
                current_version = header_match.group(1).strip()
                updated_lines.append(line)
                continue

            url_match = url_pattern.match(stripped)
            if url_match:
                current_url = url_match.group(1).strip()
                replacement_url = None
                if url_updates and current_url in url_updates:
                    replacement_url = url_updates[current_url]
                elif url_updates_by_version and current_version and current_version in url_updates_by_version:
                    replacement_url = url_updates_by_version[current_version]
                if replacement_url and replacement_url != current_url:
                    updated_lines.append(f"- URL: {replacement_url}\n")
                    updated += 1
                    current_url = replacement_url
                else:
                    updated_lines.append(line)
                continue

            sha_match = sha_pattern.match(stripped)
            if sha_match:
                sha_value = None
                if current_url and current_url in updates:
                    sha_value = updates[current_url]
                elif updates_by_version and current_version and current_version in updates_by_version:
                    sha_value = updates_by_version[current_version]
                if sha_value:
                    updated_lines.append(f"- SHA-256: {sha_value}\n")
                    updated += 1
                    current_url = None
                    continue

            updated_lines.append(line)

    if updated:
        with open(file_path, "w", encoding="utf-8") as file_handle:
            file_handle.writelines(updated_lines)

    return updated


def format_platform_label(platform):
    """Format platform name for markdown output."""
    if platform.lower() == "macosx":
        return "macOS"
    return platform.title()


def append_new_releases(file_path, new_entries, platform="windows"):
    """Append new release entries to OFFICIAL_ADB_VERSIONS.md."""
    if not new_entries:
        return False
    if not os.path.isfile(file_path):
        print(f"Error: Official versions file not found: {file_path}")
        return False

    with open(file_path, "r", encoding="utf-8") as file_handle:
        lines = file_handle.readlines()

    insert_index = None
    for idx, line in enumerate(lines):
        if line.strip().lower() == "### latest stable release":
            insert_index = idx + 1
            break

    if insert_index is None:
        print("Error: Could not find 'Latest Stable Release' section to append to.")
        return False

    while insert_index < len(lines) and lines[insert_index].strip() == "":
        insert_index += 1

    platform_label = format_platform_label(platform)
    new_entries_sorted = sorted(
        new_entries,
        key=lambda entry: entry.get("version") or "",
        reverse=True,
    )
    blocks = []
    for entry in new_entries_sorted:
        version = entry.get("version") or "Unknown"
        date_value = entry.get("date") or "Unknown date"
        url = entry.get("url")
        sha256_value = entry.get("sha256") or "???"
        blocks.extend([
            f"#### Platform-Tools {version} ({date_value})\n",
            f"**{platform_label}:**\n",
            f"- URL: {url}\n",
            f"- SHA-256: {sha256_value}\n",
            "\n",
            "---\n",
            "\n",
        ])

    lines[insert_index:insert_index] = blocks
    with open(file_path, "w", encoding="utf-8") as file_handle:
        file_handle.writelines(lines)
    return True


def main():
    parser = argparse.ArgumentParser(
        description='Verify ADB platform-tools download integrity',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s platform-tools_r35.0.2-windows.zip
  %(prog)s platform-tools_r35.0.2-windows.zip --expected-hash abc123...
  %(prog)s platform-tools_r35.0.2-windows.zip --algorithm sha256
  %(prog)s --fetch-missing-sha256
  %(prog)s --fetch-missing-sha256 --dry-run
  %(prog)s --fetch-missing-sha256 --update-official-list
        """
    )

    parser.add_argument('file_path', nargs='?', help='Path to the platform-tools zip file')
    parser.add_argument(
        '--expected-hash',
        help='Expected hash value to compare against',
        default=None
    )
    parser.add_argument(
        '--algorithm',
        choices=['md5', 'sha1', 'sha256', 'all'],
        default='all',
        help='Hash algorithm to use (default: all)'
    )
    parser.add_argument(
        '--fetch-missing-sha256',
        action='store_true',
        help='Fetch all available releases and compute missing SHA-256 hashes'
    )
    parser.add_argument(
        '--platform',
        default='windows',
        help='Platform to filter downloads (default: windows)'
    )
    parser.add_argument(
        '--official-list',
        default=None,
        help='Path to OFFICIAL_ADB_VERSIONS.md (default: repo root)'
    )
    parser.add_argument(
        '--update-official-list',
        action='store_true',
        help='Update OFFICIAL_ADB_VERSIONS.md with newly computed SHA-256 values'
    )
    parser.add_argument(
        '--append-new-releases',
        action='store_true',
        help='Append new releases to OFFICIAL_ADB_VERSIONS.md when missing'
    )
    parser.add_argument(
        '--dry-run',
        action='store_true',
        help='List missing SHA-256 entries without downloading'
    )
    parser.add_argument(
        '--normalize-official-list',
        action='store_true',
        help='Sort versions and normalize URLs in OFFICIAL_ADB_VERSIONS.md'
    )
    parser.add_argument(
        '--sync-official-list',
        action='store_true',
        help='Rebuild OFFICIAL_ADB_VERSIONS.md using the Google release notes list'
    )

    args = parser.parse_args()

    if args.fetch_missing_sha256:
        official_list = args.official_list
        if official_list is None:
            official_list = os.path.join(get_repo_root(), "OFFICIAL_ADB_VERSIONS.md")

        official_entries = []
        if not args.sync_official_list:
            official_entries = read_official_entries(official_list)
        existing_sha256 = {entry["url"]: entry.get("sha256") for entry in official_entries if entry.get("url")}
        known_urls = set(existing_sha256.keys())
        releases = get_available_releases(platform=args.platform)

        releases_by_version = {}

        def merge_entry(entry):
            version = entry.get("version")
            if not version:
                return
            current = releases_by_version.get(version, {"version": version})
            if entry.get("url") and not current.get("url"):
                current["url"] = entry["url"]
            if entry.get("date") and not current.get("date"):
                current["date"] = entry["date"]
            if entry.get("sha256") and not current.get("sha256"):
                current["sha256"] = entry["sha256"]
            releases_by_version[version] = current

        for entry in official_entries:
            merge_entry(entry)
        for entry in releases:
            merge_entry(entry)

        release_dates = fetch_release_dates()
        for version, date_value in release_dates.items():
            current = releases_by_version.get(version, {"version": version})
            if date_value and not current.get("date"):
                current["date"] = date_value
            releases_by_version[version] = current

        if args.sync_official_list and release_dates:
            google_versions = set(release_dates.keys())
            releases_by_version = {
                version: entry for version, entry in releases_by_version.items() if version in google_versions
            }

        releases = list(releases_by_version.values())

        missing = []
        for release in releases:
            url = release.get("url")
            if not url and release.get("version"):
                candidates = candidate_urls_for_version(release["version"], platform=args.platform)
                for candidate in candidates:
                    if url_exists(candidate):
                        release["url"] = candidate
                        url = candidate
                        break
                if not url and candidates:
                    release["url"] = candidates[0]
                    url = candidates[0]
            if url:
                current_sha = existing_sha256.get(url) or release.get("sha256")
                if not current_sha:
                    missing.append(release)
            else:
                missing.append(release)

        print(f"Available releases: {len(releases)}")
        print(f"Missing SHA-256 entries: {len(missing)}")

        if not missing:
            print("No missing SHA-256 values detected.")
            return

        if args.dry_run:
            print("\nMissing SHA-256 entries:")
            for entry in missing:
                url = entry.get("url") or "(no URL in list)"
                print(f"- {entry.get('version') or 'unknown'} | {url}")
            return

        updates = {}
        updates_by_version = {}
        url_updates = {}
        url_updates_by_version = {}
        with tempfile.TemporaryDirectory() as temp_dir:
            for entry in missing:
                url = entry.get("url")
                filename = os.path.basename(url) if url else None
                if not filename:
                    filename = "platform-tools.zip"
                destination = os.path.join(temp_dir, filename)
                label = url or entry.get("version") or "unknown"
                print(f"\nDownloading: {label}")
                resolved_url = download_with_fallback(entry, args.platform, destination)
                if not resolved_url:
                    print("Failed to download with available URL patterns.")
                    continue
                if url and resolved_url != url:
                    url_updates[url] = resolved_url
                entry["url"] = resolved_url
                sha256_value = compute_sha256(destination)
                updates[resolved_url] = sha256_value
                if entry.get("version"):
                    updates_by_version[entry["version"]] = sha256_value
                    url_updates_by_version[entry["version"]] = resolved_url
                print(f"SHA-256: {sha256_value}")

        if updates:
            print("\nComputed SHA-256 values:")
            for url, sha_value in updates.items():
                print(f"- {url}")
                print(f"  SHA-256: {sha_value}")

            if args.update_official_list:
                updated_count = update_official_versions_file(
                    official_list,
                    updates,
                    updates_by_version=updates_by_version,
                    url_updates=url_updates,
                    url_updates_by_version=url_updates_by_version,
                )
                if updated_count:
                    print(f"\nUpdated official versions file: {official_list}")
                else:
                    print("\nNo updates were applied to the official versions file.")

        if args.append_new_releases:
            new_entries = []
            for entry in missing:
                entry_url = entry.get("url")
                if not entry_url:
                    continue
                if entry_url not in known_urls:
                    entry_copy = dict(entry)
                    entry_copy["sha256"] = updates.get(entry_url)
                    if not entry_copy.get("date") and entry_copy.get("version"):
                        entry_copy["date"] = release_dates.get(entry_copy["version"])
                    new_entries.append(entry_copy)
            if new_entries:
                if append_new_releases(official_list, new_entries, platform=args.platform):
                    print(f"\nAppended new releases to: {official_list}")
                else:
                    print("\nNo new releases were appended to the official versions file.")

        if args.normalize_official_list or args.sync_official_list:
            if normalize_official_versions_file(
                official_list,
                platform=args.platform,
                release_dates=release_dates,
                strict_google_list=args.sync_official_list,
                sha256_by_version=updates_by_version,
            ):
                action_label = "Synced" if args.sync_official_list else "Normalized"
                print(f"\n{action_label} official versions file: {official_list}")
            else:
                print("\nNo normalization changes were applied.")
        return

    if not args.file_path:
        parser.error("file_path is required unless --fetch-missing-sha256 is used")

    if not os.path.isfile(args.file_path):
        print(f"Error: File does not exist: {args.file_path}")
        sys.exit(1)

    print(f"Verifying: {args.file_path}")
    print(f"File size: {format_file_size(get_file_size(args.file_path))}")
    print()

    # Initialize hash variables
    md5_hash = None
    sha1_hash = None
    sha256_hash = None

    # Compute hashes
    if args.algorithm in ['md5', 'all']:
        print("Computing MD5...")
        md5_hash = compute_md5(args.file_path)
        print(f"MD5:    {md5_hash}")

    if args.algorithm in ['sha1', 'all']:
        print("Computing SHA-1...")
        sha1_hash = compute_sha1(args.file_path)
        print(f"SHA-1:  {sha1_hash}")

    if args.algorithm in ['sha256', 'all']:
        print("Computing SHA-256...")
        sha256_hash = compute_sha256(args.file_path)
        print(f"SHA-256: {sha256_hash}")

    # Verify against expected hash if provided
    if args.expected_hash:
        print()
        expected_hash = args.expected_hash.lower()

        # Determine which hash matches
        match_found = False
        if md5_hash and expected_hash == md5_hash:
            print("✓ MD5 hash matches!")
            match_found = True
        elif sha1_hash and expected_hash == sha1_hash:
            print("✓ SHA-1 hash matches!")
            match_found = True
        elif sha256_hash and expected_hash == sha256_hash:
            print("✓ SHA-256 hash matches!")
            match_found = True

        if not match_found:
            print("✗ Hash does NOT match!")
            print(f"Expected: {expected_hash}")
            sys.exit(1)

    print()
    print("Verification complete!")


if __name__ == '__main__':
    main()
