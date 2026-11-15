---
name: skill-assign-asset
description: Fills empty asset fields with random matching assets from the asset index / CAS storage, through the Map Editor IPC HTTP bridge. Bulk placeholder fill only; use skill-review-assets when an asset has to be chosen rather than filled.
---

# Skill: Assign Asset (skill-assign-asset)

## Purpose
Invokes `AssignRandomAssetsToTemplates(false)` via the Map Editor HTTP IPC bridge to automatically populate empty asset fields (`.rmesh`, `.rtex`, `.raud`, `.ranim`) for newly created or existing templates without overwriting custom assets.

## Filling is not choosing
This skill picks at random from the assets that match a template, so it exists to make a new template playable and complete. It cannot tell a good icon from a poor one, and it never looks at an image.

- Need a placeholder so the map loads: this skill.
- Need the asset that actually looks like the template: `skill-review-assets`, which renders the candidates, looks at them, and writes the chosen file into the map. That skill needs no running editor, so it also works when the bridge below is unreachable.

If the bridge does not answer, report it. Editing the same asset fields in `metadata.json` directly produces the same result, and the editor's static validation at map test and publish will report a reference that does not resolve.

## HTTP IPC Endpoint
- Primary URL: `http://127.0.0.1:8092/api/`
- Fallback URL: `http://127.0.0.1:8093/api/` (used if port 8092 is occupied)

## IPC Request Schema
Send an HTTP `POST` request with `Content-Type: application/json`:

```json
{
  "action": "invokeEditorApi",
  "method": "AssignRandomAssetsToTemplates",
  "requestId": "req-assign-assets-1",
  "args": {
    "overwriteExisting": false
  }
}
```

> [!NOTE]
> `overwriteExisting: false` ensures that existing asset fields with non-empty values are preserved, while any empty/whitespace fields receive randomly selected, matching indexed assets from storage.

## Expected Response
```json
{
  "action": "invokeEditorApiResult",
  "method": "AssignRandomAssetsToTemplates",
  "requestId": "req-assign-assets-1",
  "success": true
}
```

## Invocation Examples

### PowerShell
```powershell
$body = @{
    action = "invokeEditorApi"
    method = "AssignRandomAssetsToTemplates"
    requestId = "req-assign-assets-$(Get-Random)"
    args = @{
        overwriteExisting = $false
    }
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "http://127.0.0.1:8092/api/" -Method Post -Body $body -ContentType "application/json"
} catch {
    Invoke-RestMethod -Uri "http://127.0.0.1:8093/api/" -Method Post -Body $body -ContentType "application/json"
}
```

### Bash / cURL
```bash
curl -X POST http://127.0.0.1:8092/api/ \
  -H "Content-Type: application/json" \
  -d '{
    "action": "invokeEditorApi",
    "method": "AssignRandomAssetsToTemplates",
    "requestId": "req-assign-assets-1",
    "args": {
      "overwriteExisting": false
    }
  }' || curl -X POST http://127.0.0.1:8093/api/ \
  -H "Content-Type: application/json" \
  -d '{
    "action": "invokeEditorApi",
    "method": "AssignRandomAssetsToTemplates",
    "requestId": "req-assign-assets-1",
    "args": {
      "overwriteExisting": false
    }
  }'
```

### Python
```python
import urllib.request
import json

payload = json.dumps({
    "action": "invokeEditorApi",
    "method": "AssignRandomAssetsToTemplates",
    "requestId": "req-assign-assets-1",
    "args": {
        "overwriteExisting": False
    }
}).encode("utf-8")

for port in [8092, 8093]:
    try:
        req = urllib.request.Request(
            f"http://127.0.0.1:{port}/api/",
            data=payload,
            headers={"Content-Type": "application/json"}
        )
        with urllib.request.urlopen(req, timeout=5) as resp:
            print(resp.read().decode("utf-8"))
            break
    except Exception as e:
        continue
```
