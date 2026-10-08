import sys
from huggingface_hub import snapshot_download

def download_mixamo(repo_id, revision, local_dir):
    snapshot_download(
        repo_type="dataset",
        repo_id=repo_id,
        revision=revision,
        local_dir=local_dir,
        allow_patterns=["bones*.fbx"],
    )
    print("[MIA] Mixamo data downloaded successfully.")

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: download_mixamo.py <repo_id> <revision> <local_dir>")
        sys.exit(1)
    download_mixamo(sys.argv[1], sys.argv[2], sys.argv[3])
