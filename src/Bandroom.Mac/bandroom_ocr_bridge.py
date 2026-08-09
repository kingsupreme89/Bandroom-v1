#!/usr/bin/env python3
"""
Bandroom OCR Bridge for macOS
==============================
Captures screen regions using screencapture (built-in macOS tool) and extracts
text using Vision framework (via PyObjC) or Tesseract as fallback.

Usage:
  python3 bandroom_ocr_bridge.py --regions '[{"Name":"down","X":0,"Y":0.83,"W":1.0,"H":0.14},...]' --interval 250

Output (one JSON object per line on stdout):
  {"region":"down","text":"2ND & 7"}
  {"region":"quarter","text":"1ST QTR"}
  {"type":"status","message":"OCR engine: vision (PyObjC)"}

Requires one of:
  - pip3 install pyobjc-framework-Vision  (recommended, uses built-in Apple Vision)
  - brew install tesseract                (fallback, open-source OCR)
"""

import sys
import os
import json
import subprocess
import tempfile
import time
import argparse

# =============================================================================
# OCR Engine Detection
# =============================================================================

_ocr_engine = None  # "vision", "tesseract", or None


def _detect_engine():
    """Detect which OCR engine is available. Try Vision first, then Tesseract."""
    global _ocr_engine

    # Try Vision framework via PyObjC
    try:
        import Quartz
        import Vision
        _ocr_engine = "vision"
        return
    except ImportError:
        pass

    # Try Tesseract
    try:
        result = subprocess.run(
            ["tesseract", "--version"],
            capture_output=True, text=True, timeout=5
        )
        if result.returncode == 0:
            _ocr_engine = "tesseract"
            return
    except (FileNotFoundError, subprocess.TimeoutExpired):
        pass

    _ocr_engine = None


# =============================================================================
# Screen Capture
# =============================================================================


def capture_screen():
    """Capture the entire screen to a temporary PNG file. Returns file path."""
    fd, path = tempfile.mkstemp(suffix=".png", prefix="bandroom_ocr_")
    os.close(fd)

    result = subprocess.run(
        ["/usr/sbin/screencapture", "-x", "-t", "png", path],
        capture_output=True, text=True, timeout=10,
    )
    if result.returncode != 0:
        raise RuntimeError(f"screencapture failed: {result.stderr}")

    return path


def crop_region(image_path, region):
    """
    Crop a region from the image using sips (built-in macOS image tool).
    region is a dict with X, Y, W, H as fractions of image size.
    Returns path to cropped PNG.
    """
    # Get image dimensions
    result = subprocess.run(
        ["/usr/bin/sips", "-g", "pixelWidth", "-g", "pixelHeight", image_path],
        capture_output=True, text=True, timeout=5,
    )
    output = result.stdout
    import re
    w_match = re.search(r"pixelWidth:\s*(\d+)", output)
    h_match = re.search(r"pixelHeight:\s*(\d+)", output)
    if not w_match or not h_match:
        raise RuntimeError("Could not get image dimensions")
    img_w = int(w_match.group(1))
    img_h = int(h_match.group(2))

    # Calculate crop bounds
    x = int(region["X"] * img_w)
    y = int(region["Y"] * img_h)
    w = int(region["W"] * img_w)
    h = int(region["H"] * img_h)

    # Clamp to image bounds
    x = max(0, min(x, img_w - 1))
    y = max(0, min(y, img_h - 1))
    w = max(1, min(w, img_w - x))
    h = max(1, min(h, img_h - y))

    fd, cropped_path = tempfile.mkstemp(suffix=".png", prefix=f"bandroom_{region['Name']}_")
    os.close(fd)

    # Use sips to crop
    subprocess.run(
        ["/usr/bin/sips", "-c", f"{h}", f"{w}", image_path,
         "--cropOffset", f"{y}", f"{x}", "-o", cropped_path],
        capture_output=True, timeout=10,
    )

    return cropped_path


# =============================================================================
# OCR: Vision Framework (PyObjC)
# =============================================================================

def ocr_vision(image_path):
    """Extract text from an image using Apple Vision framework via PyObjC."""
    try:
        import Quartz
        import Vision
    except ImportError:
        return ""

    # Load the image
    url = Quartz.CFURLCreateFromFileSystemRepresentation(
        None, image_path.encode("utf-8"), len(image_path.encode("utf-8")), False
    )
    if url is None:
        return ""

    source = Quartz.CGImageSourceCreateWithURL(url, None)
    if source is None:
        return ""

    cg_image = Quartz.CGImageSourceCreateImageAtIndex(source, 0, None)
    if cg_image is None:
        return ""

    # Create a text recognition request
    request = Vision.VNRecognizeTextRequest.alloc().init()
    request.setRecognitionLevel_(1)  # 1 = accurate (vs 0 = fast)
    request.setRecognitionLanguages_(["en-US"])

    # Perform the request
    handler = Vision.VNImageRequestHandler.alloc().initWithCGImage_options_(
        cg_image, None
    )

    success = handler.performRequests_error_([request], None)
    if not success:
        return ""

    # Collect results
    results = []
    observations = request.results()
    if observations:
        for obs in observations:
            top = obs.topCandidates_(1)
            if top and len(top) > 0:
                results.append(top[0].string())

    return "\n".join(results).strip()


# =============================================================================
# OCR: Tesseract Fallback
# =============================================================================

def ocr_tesseract(image_path):
    """Extract text from an image using Tesseract OCR."""
    fd, output_base = tempfile.mkstemp(prefix="bandroom_tess_")
    os.close(fd)

    try:
        result = subprocess.run(
            ["tesseract", image_path, output_base, "--psm", "6"],
            capture_output=True, text=True, timeout=15,
        )
        if result.returncode != 0:
            return ""

        txt_path = output_base + ".txt"
        if os.path.exists(txt_path):
            with open(txt_path, "r", encoding="utf-8", errors="ignore") as f:
                return f.read().strip()
        return ""
    finally:
        # Clean up tesseract output files
        for ext in [".txt", ".osd", ".tsv"]:
            try:
                os.unlink(output_base + ext)
            except OSError:
                pass


# =============================================================================
# Main OCR Loop
# =============================================================================

def process_regions(image_path, regions):
    """Capture once, crop each region, OCR, emit JSON to stdout."""
    try:
        # Crop each region
        for region in regions:
            cropped_path = None
            try:
                cropped_path = crop_region(image_path, region)

                if _ocr_engine == "vision":
                    text = ocr_vision(cropped_path)
                elif _ocr_engine == "tesseract":
                    text = ocr_tesseract(cropped_path)
                else:
                    text = ""

                if text:
                    print(json.dumps({
                        "region": region["Name"],
                        "text": text,
                    }), flush=True)
            except Exception as e:
                # Don't let one region failure kill the entire cycle
                pass
            finally:
                if cropped_path:
                    try:
                        os.unlink(cropped_path)
                    except OSError:
                        pass
    finally:
        try:
            os.unlink(image_path)
        except OSError:
            pass


def main():
    parser = argparse.ArgumentParser(description="Bandroom OCR Bridge")
    parser.add_argument("--regions", required=True, help="JSON array of region definitions")
    parser.add_argument("--interval", type=int, default=250, help="Polling interval in ms")
    args = parser.parse_args()

    # Parse regions
    try:
        regions = json.loads(args.regions)
    except json.JSONDecodeError as e:
        print(json.dumps({"type": "error", "message": f"Invalid regions JSON: {e}"}),
              flush=True)
        sys.exit(1)

    # Detect OCR engine
    _detect_engine()
    if _ocr_engine is None:
        print(json.dumps({
            "type": "error",
            "message": (
                "No OCR engine available. Install one:\n"
                "  pip3 install pyobjc-framework-Vision  (recommended)\n"
                "  brew install tesseract              (fallback)"
            ),
        }), flush=True)
        print(json.dumps({
            "type": "status",
            "message": "OCR engine: NONE (install pyobjc-framework-Vision or tesseract)",
        }), flush=True)
        # Don't exit — the C# side will see the error and handle it gracefully
        # Continue running so the user can install the dependency and restart the app

    else:
        print(json.dumps({
            "type": "status",
            "message": f"OCR engine: {_ocr_engine}",
        }), flush=True)

    # Main polling loop
    interval = max(200, args.interval) / 1000.0  # Convert to seconds

    while True:
        try:
            loop_start = time.monotonic()

            if _ocr_engine is not None:
                image_path = capture_screen()
                process_regions(image_path, regions)

            # Maintain polling interval
            elapsed = time.monotonic() - loop_start
            sleep_time = max(0, interval - elapsed)
            if sleep_time > 0:
                time.sleep(sleep_time)

        except KeyboardInterrupt:
            break
        except Exception as e:
            print(json.dumps({
                "type": "error",
                "message": str(e),
            }), flush=True)
            time.sleep(1.0)


if __name__ == "__main__":
    main()