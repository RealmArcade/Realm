import os

def extract_duplicate(filename, start_line, num_lines):
    with open(filename, 'r') as f:
        lines = f.readlines()
        if start_line > 0 and start_line + num_lines <= len(lines):
            return ''.join(lines[start_line-1:start_line+num_lines-1])
        return "Line range out of bounds"

# EnetMapTransferService vs LobbyManager
print("=== EnetMapTransferService & LobbyManager ===")
print("EnetMapTransferService:")
print(extract_duplicate("Realm.Client/Network/EnetMapTransferService.cs", 234, 13))
print("LobbyManager:")
print(extract_duplicate("Realm.Client/Network/LobbyManager.cs", 1536, 13))

# EditorService
print("=== EditorService ===")
print("EditorService 1:")
print(extract_duplicate("Realm.Client/Services/EditorService.cs", 4770, 8))
print("EditorService 2:")
print(extract_duplicate("Realm.Client/Services/EditorService.cs", 4787, 8))

# TemplateManagerDialog
print("=== TemplateManagerDialog ===")
print(extract_duplicate("Realm.Client/UI/MapEditor/TemplateManagerDialog.cs", 1939, 6))

# MapStorageService vs MapEditorHUD.Tools
print("=== MapStorageService & MapEditorHUD.Tools ===")
print("MapStorageService:")
print(extract_duplicate("Realm.Client/Services/MapStorageService.cs", 1326, 16))
print("MapEditorHUD.Tools:")
print(extract_duplicate("Realm.Client/UI/MapEditorHUD.Tools.cs", 2544, 16))

# MapDetails vs MapDiscovery
print("=== MapDetails & MapDiscovery ===")
print("MapDetails:")
print(extract_duplicate("Realm.Client/UI/MapDetails.cs", 793, 36))
print("MapDiscovery:")
print(extract_duplicate("Realm.Client/UI/MapDiscovery.cs", 615, 36))

# RuntimeTerrain
print("=== RuntimeTerrain ===")
print(extract_duplicate("Realm.Client/RuntimeTerrain.cs", 1036, 4))
print(extract_duplicate("Realm.Client/RuntimeTerrain.cs", 1086, 4))

# EditableTerrain
print("=== EditableTerrain ===")
print(extract_duplicate("Realm.Client/EditableTerrain.cs", 121, 24))
print(extract_duplicate("Realm.Client/EditableTerrain.cs", 346, 24))

# NetworkService
print("=== NetworkService ===")
print(extract_duplicate("Realm.Client/Services/NetworkService.cs", 841, 10))
print(extract_duplicate("Realm.Client/Services/NetworkService.cs", 864, 10))

# LobbyCreate vs MapEditorHUD.Tools
print("=== LobbyCreate & MapEditorHUD.Tools ===")
print(extract_duplicate("Realm.Client/UI/LobbyCreate.cs", 350, 13))
print(extract_duplicate("Realm.Client/UI/MapEditorHUD.Tools.cs", 2137, 13))

# ReplayListPanel vs UIManager
print("=== ReplayListPanel & UIManager ===")
print(extract_duplicate("Realm.Client/UI/ReplayListPanel.cs", 442, 17))
print(extract_duplicate("Realm.Client/UI/UIManager.cs", 635, 17))

