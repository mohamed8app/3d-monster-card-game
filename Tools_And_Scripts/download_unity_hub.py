import urllib.request
import os
import sys

url = "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x64.exe"
dest = r"D:\UnityHubSetup-x64.exe"

print(f"Connecting to {url}...")
req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})

try:
    with urllib.request.urlopen(req) as resp:
        total_size = int(resp.headers.get("Content-Length", 0))
        print(f"Total file size: {total_size / (1024*1024):.2f} MB")
        
        downloaded = 0
        chunk_size = 1024 * 1024  # 1 MB chunks
        with open(dest, "wb") as f:
            while True:
                chunk = resp.read(chunk_size)
                if not chunk:
                    break
                f.write(chunk)
                downloaded += len(chunk)
                if total_size > 0:
                    pct = (downloaded / total_size) * 100
                    print(f"\rDownloading: {downloaded / (1024*1024):.1f} MB / {total_size / (1024*1024):.1f} MB ({pct:.1f}%)", end="", flush=True)
                else:
                    print(f"\rDownloaded: {downloaded / (1024*1024):.1f} MB", end="", flush=True)
                    
    print(f"\nDone! Saved to {dest} ({os.path.getsize(dest)/(1024*1024):.2f} MB)")
except Exception as e:
    print(f"\nFailed to download: {e}")
    sys.exit(1)
