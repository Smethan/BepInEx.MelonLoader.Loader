#!/usr/bin/env python3
"""
Helper script to create a placeholder icon.png for Thunderstore packages.
Creates a 256x256 PNG with text if Pillow is installed.
"""

import sys

try:
    from PIL import Image, ImageDraw, ImageFont
    HAS_PIL = True
except ImportError:
    HAS_PIL = False
    print("PIL/Pillow not installed. Install with: pip install Pillow")
    print("For now, you'll need to create the icon manually.")
    sys.exit(1)


def create_placeholder_icon(output_path: str = "icon.png"):
    """
    Create a simple placeholder icon for Thunderstore.

    Args:
        output_path: Where to save the icon
    """
    # Create 256x256 image with gradient background
    size = (256, 256)
    img = Image.new('RGB', size, color='#1a1a2e')
    draw = ImageDraw.Draw(img)

    # Draw a gradient-ish background
    for i in range(256):
        color = int(26 + (i / 256) * 40)  # Gradient from dark to slightly lighter
        draw.line([(0, i), (256, i)], fill=(color, color, color + 20))

    # Draw border
    border_color = '#16213e'
    border_width = 8
    draw.rectangle(
        [border_width//2, border_width//2, size[0]-border_width//2, size[1]-border_width//2],
        outline=border_color,
        width=border_width
    )

    # Try to add text
    try:
        # Try to use a reasonable font
        try:
            font_large = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 48)
            font_small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 24)
        except:
            # Fallback to default font
            font_large = ImageFont.load_default()
            font_small = ImageFont.load_default()

        # Draw text
        text_color = '#e94560'
        text1 = "MelonLoader"
        text2 = "BepInEx"

        # Calculate text positions (centered)
        bbox1 = draw.textbbox((0, 0), text1, font=font_large)
        text1_width = bbox1[2] - bbox1[0]
        text1_x = (size[0] - text1_width) // 2

        bbox2 = draw.textbbox((0, 0), text2, font=font_small)
        text2_width = bbox2[2] - bbox2[0]
        text2_x = (size[0] - text2_width) // 2

        # Draw the text
        draw.text((text1_x, 90), text1, fill=text_color, font=font_large)
        draw.text((text2_x, 150), text2, fill='#0f3460', font=font_small)

    except Exception as e:
        print(f"Warning: Could not add text to icon: {e}")

    # Save
    img.save(output_path, 'PNG')
    print(f"✓ Created placeholder icon: {output_path}")
    print(f"  Size: 256x256 PNG")
    print(f"\nNOTE: This is a placeholder! Consider creating a proper icon for better presentation.")


if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(description='Create a placeholder icon.png for Thunderstore')
    parser.add_argument(
        '--output',
        default='icon.png',
        help='Output path for icon (default: icon.png)'
    )

    args = parser.parse_args()
    create_placeholder_icon(args.output)
