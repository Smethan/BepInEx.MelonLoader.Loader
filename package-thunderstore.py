#!/usr/bin/env python3
"""
Script to package BepInEx.MelonLoader.Loader for Thunderstore/r2modman distribution.

This script creates a Thunderstore-compatible package from the build output,
including manifest.json, icon.png, and README.md along with the compiled plugins.
"""

import argparse
import json
import os
import shutil
import sys
import zipfile
from pathlib import Path
from typing import Optional


class ThunderstorePackager:
    """Handles packaging of BepInEx.MelonLoader.Loader for Thunderstore."""

    REQUIRED_FILES = ['manifest.json', 'icon.png', 'README.md']
    ICON_SIZE = (256, 256)

    def __init__(self,
                 output_dir: Path,
                 thunderstore_dir: Path,
                 variant: str,
                 version: str):
        """
        Initialize the packager.

        Args:
            output_dir: Path to the Output directory containing built files
            thunderstore_dir: Path to store Thunderstore packages
            variant: Build variant (IL2CPP-BepInEx6, UnityMono-BepInEx5, UnityMono-BepInEx6)
            version: Version string (e.g., "2.1.0")
        """
        self.output_dir = output_dir
        self.thunderstore_dir = thunderstore_dir
        self.variant = variant
        self.version = version
        self.temp_dir = thunderstore_dir / f"temp_{variant}"

    def validate_icon(self, icon_path: Path) -> bool:
        """
        Validate that the icon meets Thunderstore requirements.

        Args:
            icon_path: Path to icon file

        Returns:
            True if valid, False otherwise
        """
        if not icon_path.exists():
            print(f"Warning: Icon not found at {icon_path}")
            return False

        try:
            from PIL import Image
            img = Image.open(icon_path)
            if img.size != self.ICON_SIZE:
                print(f"Warning: Icon must be {self.ICON_SIZE[0]}x{self.ICON_SIZE[1]}, found {img.size}")
                return False
            if img.format != 'PNG':
                print(f"Warning: Icon must be PNG format, found {img.format}")
                return False
            return True
        except ImportError:
            print("Warning: PIL/Pillow not installed, skipping icon validation")
            print("Install with: pip install Pillow")
            return True  # Assume valid if we can't check
        except Exception as e:
            print(f"Warning: Could not validate icon: {e}")
            return False

    def create_manifest(self,
                       name: str,
                       description: str,
                       website_url: str,
                       dependencies: list,
                       namespace: str = "BepInEx") -> dict:
        """
        Create a Thunderstore manifest.json content.

        Args:
            name: Package name (no spaces, only a-z A-Z 0-9 _)
            description: Short description (max 250 chars)
            website_url: Project URL
            dependencies: List of dependencies in format "Author-Name-Version"
            namespace: Thunderstore namespace/team name

        Returns:
            Dictionary containing manifest data
        """
        if len(description) > 250:
            print(f"Warning: Description exceeds 250 characters, truncating...")
            description = description[:247] + "..."

        manifest = {
            "name": name,
            "version_number": self.version,
            "website_url": website_url,
            "description": description,
            "dependencies": dependencies
        }

        return manifest

    def prepare_package(self,
                       manifest_data: dict,
                       icon_path: Optional[Path] = None,
                       readme_path: Optional[Path] = None) -> Path:
        """
        Prepare the package directory structure.

        Args:
            manifest_data: Manifest dictionary
            icon_path: Optional custom icon path
            readme_path: Optional custom README path

        Returns:
            Path to the temporary package directory
        """
        # Clean and create temp directory
        if self.temp_dir.exists():
            shutil.rmtree(self.temp_dir)
        self.temp_dir.mkdir(parents=True)

        # Write manifest.json
        manifest_path = self.temp_dir / "manifest.json"
        with open(manifest_path, 'w', encoding='utf-8') as f:
            json.dump(manifest_data, f, indent=4, ensure_ascii=False)
        print(f"✓ Created manifest.json")

        # Copy or create icon
        dest_icon = self.temp_dir / "icon.png"
        if icon_path and icon_path.exists():
            if self.validate_icon(icon_path):
                shutil.copy2(icon_path, dest_icon)
                print(f"✓ Copied icon from {icon_path}")
            else:
                print(f"✗ Icon validation failed, package may be rejected by Thunderstore")
                shutil.copy2(icon_path, dest_icon)
        else:
            print(f"✗ No icon.png found - you must provide a 256x256 PNG icon!")
            print(f"  Create one at: {self.temp_dir / 'icon.png'}")

        # Copy README
        dest_readme = self.temp_dir / "README.md"
        if readme_path and readme_path.exists():
            shutil.copy2(readme_path, dest_readme)
            print(f"✓ Copied README from {readme_path}")
        else:
            print(f"✗ No README.md found - you must provide one!")
            print(f"  Create one at: {self.temp_dir / 'README.md'}")

        # Extract and copy build files
        build_zip = self.output_dir / f"MLLoader-{self.variant}-v{self.version}.zip"
        if not build_zip.exists():
            raise FileNotFoundError(f"Build file not found: {build_zip}")

        print(f"✓ Extracting build files from {build_zip.name}...")
        with zipfile.ZipFile(build_zip, 'r') as zf:
            zf.extractall(self.temp_dir)

        return self.temp_dir

    def create_package(self, package_dir: Path) -> Path:
        """
        Create the final Thunderstore package zip file.

        Args:
            package_dir: Path to directory containing package files

        Returns:
            Path to created zip file
        """
        # Verify required files exist
        missing = []
        for required in self.REQUIRED_FILES:
            if not (package_dir / required).exists():
                missing.append(required)

        if missing:
            print(f"\n✗ ERROR: Missing required files: {', '.join(missing)}")
            print(f"Package created at {package_dir} but is INCOMPLETE")
            print(f"Please add missing files and manually zip the directory.")
            sys.exit(1)

        # Create output zip
        output_name = f"BepInEx-MelonLoader_Loader-{self.variant}-{self.version}.zip"
        output_path = self.thunderstore_dir / output_name

        print(f"\n📦 Creating Thunderstore package: {output_name}")

        with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zf:
            for root, dirs, files in os.walk(package_dir):
                for file in files:
                    file_path = Path(root) / file
                    arcname = file_path.relative_to(package_dir)
                    zf.write(file_path, arcname)
                    print(f"  + {arcname}")

        # Clean up temp directory
        shutil.rmtree(package_dir)

        return output_path


def main():
    """Main entry point for the packaging script."""
    parser = argparse.ArgumentParser(
        description="Package BepInEx.MelonLoader.Loader for Thunderstore/r2modman",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Package IL2CPP variant with default settings
  %(prog)s --variant IL2CPP-BepInEx6

  # Package with custom icon and README
  %(prog)s --variant UnityMono-BepInEx6 --icon my-icon.png --readme THUNDERSTORE_README.md

  # Package with custom dependencies
  %(prog)s --variant IL2CPP-BepInEx6 --deps "BepInEx-BepInExPack-5.4.21"

Variants:
  - IL2CPP-BepInEx6       (IL2CPP games with BepInEx 6)
  - UnityMono-BepInEx5    (Unity Mono games with BepInEx 5)
  - UnityMono-BepInEx6    (Unity Mono games with BepInEx 6)
        """
    )

    parser.add_argument(
        '--variant',
        choices=['IL2CPP-BepInEx6', 'UnityMono-BepInEx5', 'UnityMono-BepInEx6'],
        default=['IL2CPP-BepInEx6'],
        help='Build variant to package'
    )

    parser.add_argument(
        '--version',
        default='2.1.0',
        help='Version number (semantic versioning: Major.Minor.Patch)'
    )

    parser.add_argument(
        '--name',
        default='MelonLoader_Loader',
        help='Package name (no spaces, only a-z A-Z 0-9 _)'
    )

    parser.add_argument(
        '--namespace',
        default='BepInEx',
        help='Thunderstore namespace/team name'
    )

    parser.add_argument(
        '--description',
        default='BepInEx loader plugin for running MelonLoader mods. Supports both Unity Mono and IL2CPP games.',
        help='Short package description (max 250 characters)'
    )

    parser.add_argument(
        '--website',
        default='https://github.com/BepInEx/BepInEx.MelonLoader.Loader',
        help='Project website URL'
    )

    parser.add_argument(
        '--deps',
        nargs='*',
        default=["BepInEx-BepInExPack_IL2CPP-6.0.733"],
        help='Dependencies in format "Author-Name-Version"'
    )

    parser.add_argument(
        '--icon',
        type=Path,
        help='Path to icon.png (must be 256x256 PNG)'
    )

    parser.add_argument(
        '--readme',
        type=Path,
        help='Path to README.md for Thunderstore (uses repo README.md if not specified)'
    )

    parser.add_argument(
        '--output-dir',
        type=Path,
        default=Path('Output'),
        help='Directory containing build outputs'
    )

    parser.add_argument(
        '--thunderstore-dir',
        type=Path,
        default=Path('Thunderstore'),
        help='Output directory for Thunderstore packages'
    )

    args = parser.parse_args()

    # Resolve paths
    output_dir = args.output_dir.resolve()
    thunderstore_dir = args.thunderstore_dir.resolve()

    if not output_dir.exists():
        print(f"Error: Output directory not found: {output_dir}")
        print(f"Please run the build first: ./build.sh")
        sys.exit(1)

    # Create Thunderstore output directory
    thunderstore_dir.mkdir(parents=True, exist_ok=True)

    # Initialize packager
    packager = ThunderstorePackager(
        output_dir=output_dir,
        thunderstore_dir=thunderstore_dir,
        variant=args.variant,
        version=args.version
    )

    # Create manifest
    manifest = packager.create_manifest(
        name=args.name,
        description=args.description,
        website_url=args.website,
        dependencies=args.deps,
        namespace=args.namespace
    )

    # Determine icon and README paths
    icon_path = args.icon if args.icon else Path('icon.png')
    readme_path = args.readme if args.readme else Path('README.md')

    print(f"\n{'='*60}")
    print(f"Packaging BepInEx.MelonLoader.Loader for Thunderstore")
    print(f"{'='*60}")
    print(f"Variant: {args.variant}")
    print(f"Version: {args.version}")
    print(f"Output: {thunderstore_dir}")
    print(f"{'='*60}\n")

    try:
        # Prepare package directory
        package_dir = packager.prepare_package(
            manifest_data=manifest,
            icon_path=icon_path,
            readme_path=readme_path
        )

        # Create final package
        package_path = packager.create_package(package_dir)

        print(f"\n{'='*60}")
        print(f"✓ SUCCESS!")
        print(f"{'='*60}")
        print(f"Package created: {package_path}")
        print(f"Size: {package_path.stat().st_size / 1024:.1f} KB")
        print(f"\nYou can now upload this to Thunderstore!")
        print(f"{'='*60}\n")

    except FileNotFoundError as e:
        print(f"\n✗ ERROR: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"\n✗ ERROR: An unexpected error occurred: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == '__main__':
    main()
