import sys
from huggingface_hub import snapshot_download

def download_models(repo_id, revision, local_dir):
    snapshot_download(
        repo_type="model",
        repo_id=repo_id,
        revision=revision,
        local_dir=local_dir,
        allow_patterns=["output/best/new/*"],
    )
    print("[MIA] Pretrained models downloaded successfully.")

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: download_models.py <repo_id> <revision> <local_dir>")
        sys.exit(1)
    download_models(sys.argv[1], sys.argv[2], sys.argv[3])
