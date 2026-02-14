#!/usr/bin/env python3
"""
ADB Platform-Tools Download Verifier

This script helps verify the integrity of downloaded ADB platform-tools packages
by computing their checksums and comparing them with known values.

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
import sys


def compute_md5(file_path):
    """Compute MD5 hash of a file."""
    hash_md5 = hashlib.md5()
    try:
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(4096), b""):
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
            for chunk in iter(lambda: f.read(4096), b""):
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
            for chunk in iter(lambda: f.read(4096), b""):
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


def main():
    parser = argparse.ArgumentParser(
        description='Verify ADB platform-tools download integrity',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s platform-tools_r35.0.2-windows.zip
  %(prog)s platform-tools_r35.0.2-windows.zip --expected-hash abc123...
  %(prog)s platform-tools_r35.0.2-windows.zip --algorithm sha256
        """
    )
    
    parser.add_argument('file_path', help='Path to the platform-tools zip file')
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
    
    args = parser.parse_args()
    
    if not os.path.isfile(args.file_path):
        print(f"Error: File does not exist: {args.file_path}")
        sys.exit(1)
    
    print(f"Verifying: {args.file_path}")
    print(f"File size: {format_file_size(get_file_size(args.file_path))}")
    print()
    
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
        if args.algorithm in ['md5', 'all'] and expected_hash == md5_hash:
            print("✓ MD5 hash matches!")
            match_found = True
        elif args.algorithm in ['sha1', 'all'] and expected_hash == sha1_hash:
            print("✓ SHA-1 hash matches!")
            match_found = True
        elif args.algorithm in ['sha256', 'all'] and expected_hash == sha256_hash:
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
